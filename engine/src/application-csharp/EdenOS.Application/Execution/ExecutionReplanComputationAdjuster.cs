using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Application.Planning;

namespace EdenOS.Application.Execution;

internal static class ExecutionReplanComputationAdjuster
{
    private const decimal DefaultNpcFacilityTaxRate = 0.0025m;
    private const decimal DefaultSccSurchargeRate = 0.04m;
    private const decimal DefaultOmegaAlphaCloneRate = 0m;
    private const decimal DefaultFacilityBonusMultiplier = 1m;

    public static PlanComputationResult ApplyExecutionEventBiases(
        IndustryPlan plan,
        PlanComputationResult computation,
        IReadOnlyList<ExecutionEventRecord> events)
    {
        var adjusted = ApplyIndustryCostBiases(plan, computation, events);

        var latestMarketChange = events
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Select(evt => evt.Details)
            .OfType<MarketChangeEventDetails>()
            .FirstOrDefault();

        if (latestMarketChange is null || adjusted.AlternativePaths.Count == 0)
        {
            return ApplyIndustryCostPreferenceBiases(plan, adjusted, events);
        }

        var preferredPath = adjusted.AlternativePaths.FirstOrDefault(path => path.IsPreferred);
        var reprioritized = ResolveReprioritizedPath(adjusted.AlternativePaths, latestMarketChange, preferredPath);
        if (reprioritized is null ||
            preferredPath is null ||
            reprioritized.PathId.Equals(preferredPath.PathId, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyIndustryCostPreferenceBiases(plan, adjusted, events);
        }

        adjusted = adjusted with
        {
            AlternativePaths = adjusted.AlternativePaths
                .Select(path => path with
                {
                    IsPreferred = path.PathId.Equals(reprioritized.PathId, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray(),
            Explanations = adjusted.Explanations
                .Concat([
                    $"Execution replan reprioritized '{reprioritized.Title}' after market change: {latestMarketChange.ChangeSummary}"
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        return ApplyIndustryCostPreferenceBiases(plan, adjusted, events);
    }

    private static PlanAlternativePath? ResolveReprioritizedPath(
        IReadOnlyList<PlanAlternativePath> alternatives,
        MarketChangeEventDetails marketChange,
        PlanAlternativePath? preferredPath)
    {
        var signal = string.Join(
            ' ',
            [
                marketChange.ChangeSummary,
                marketChange.ImpactHint ?? string.Empty,
                marketChange.ResourceName ?? string.Empty
            ]).ToLowerInvariant();

        if (IsRecoverySignal(signal))
        {
            if (preferredPath is not null &&
                preferredPath.Strategy is PlanAlternativeStrategy.CurrentChain or PlanAlternativeStrategy.ProfitMaximized)
            {
                return preferredPath;
            }

            if (TryFind(alternatives, PlanAlternativeStrategy.CurrentChain, out var currentChainPath))
            {
                return currentChainPath;
            }

            if (TryFind(alternatives, PlanAlternativeStrategy.ProfitMaximized, out var profitPath))
            {
                return profitPath;
            }
        }

        if (ContainsAny(signal, "outsource", "supplier", "external build", "external supplier") &&
            TryFind(alternatives, PlanAlternativeStrategy.OutsourceIntermediates, out var outsourcePath))
        {
            return outsourcePath;
        }

        if (ContainsAny(signal, "buy", "market", "purchase", "finished", "cheaper", "spike", "price", "cost") &&
            TryFind(alternatives, PlanAlternativeStrategy.MarketHeavy, out var marketHeavyPath))
        {
            return marketHeavyPath;
        }

        if (preferredPath is not null &&
            preferredPath.Strategy is PlanAlternativeStrategy.CurrentChain or PlanAlternativeStrategy.ProfitMaximized)
        {
            if (TryFind(alternatives, PlanAlternativeStrategy.MarketHeavy, out marketHeavyPath))
            {
                return marketHeavyPath;
            }

            if (TryFind(alternatives, PlanAlternativeStrategy.OutsourceIntermediates, out outsourcePath))
            {
                return outsourcePath;
            }
        }

        return null;
    }

    private static bool IsRecoverySignal(string signal)
    {
        return ContainsAny(
            signal,
            "normalize",
            "normalized",
            "recover",
            "recovered",
            "stabilized",
            "stable again",
            "favored again",
            "current chain is favored again",
            "original internal chain",
            "internal production is cheaper again",
            "return to the current chain",
            "return to internal production");
    }

    private static bool TryFind(
        IReadOnlyList<PlanAlternativePath> alternatives,
        PlanAlternativeStrategy strategy,
        out PlanAlternativePath? path)
    {
        path = alternatives.FirstOrDefault(candidate => candidate.Strategy == strategy);
        return path is not null;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.Ordinal));
    }

    private static PlanComputationResult ApplyIndustryCostBiases(
        IndustryPlan plan,
        PlanComputationResult computation,
        IReadOnlyList<ExecutionEventRecord> events)
    {
        var latestIndustryCostChange = events
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Select(evt => evt.Details)
            .OfType<IndustryCostChangeEventDetails>()
            .FirstOrDefault();

        if (latestIndustryCostChange is null || string.IsNullOrWhiteSpace(latestIndustryCostChange.NodeId))
        {
            return computation;
        }

        var affectedNodeId = latestIndustryCostChange.NodeId.Trim();
        var affectedNode = plan.Nodes.FirstOrDefault(node => node.NodeId.Equals(affectedNodeId, StringComparison.OrdinalIgnoreCase));
        if (affectedNode is null)
        {
            return computation;
        }

        var baselineLineItems = computation.CostBreakdown.LineItems.ToArray();
        var adjustedLineItems = baselineLineItems
            .Select(item => item.NodeId?.Equals(affectedNodeId, StringComparison.OrdinalIgnoreCase) == true &&
                            string.Equals(item.Category, "industry_job", StringComparison.OrdinalIgnoreCase)
                ? item with { AmountIsk = ResolveAdjustedIndustryCostAmount(item.AmountIsk, affectedNode, latestIndustryCostChange) }
                : item)
            .ToArray();

        if (adjustedLineItems.SequenceEqual(baselineLineItems))
        {
            return computation;
        }

        var purchaseCost = adjustedLineItems
            .Where(item => item.Category == "purchase")
            .Sum(item => item.AmountIsk);
        var industryCost = adjustedLineItems
            .Where(item => item.Category == "industry_job")
            .Sum(item => item.AmountIsk);
        var logisticsCost = adjustedLineItems
            .Where(item => item.Category == "logistics")
            .Sum(item => item.AmountIsk);
        var revenue = adjustedLineItems
            .Where(item => item.Category == "revenue")
            .Sum(item => item.AmountIsk);
        var otherCost = adjustedLineItems
            .Where(item => item.Category is not ("purchase" or "industry_job" or "logistics" or "revenue"))
            .Sum(item => item.AmountIsk);
        var totalCost = purchaseCost + industryCost + logisticsCost + otherCost;
        var adjustedBreakdown = computation.CostBreakdown with
        {
            TotalCostIsk = Math.Round(totalCost, 2),
            PurchaseCostIsk = Math.Round(purchaseCost, 2),
            IndustryCostIsk = Math.Round(industryCost, 2),
            LogisticsCostIsk = Math.Round(logisticsCost, 2),
            OtherCostIsk = Math.Round(otherCost, 2),
            LineItems = adjustedLineItems,
            Explanations = computation.CostBreakdown.Explanations
                .Concat([
                    $"Execution replan updated industry job cost for '{affectedNode.Title}' after system cost index changed to {latestIndustryCostChange.CurrentSystemCostIndex:P2}."
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        return computation with
        {
            CostBreakdown = adjustedBreakdown,
            ProfitEstimate = computation.ProfitEstimate with
            {
                EstimatedCostIsk = Math.Round(totalCost, 2),
                EstimatedProfitIsk = Math.Round(revenue - totalCost, 2),
                MarginPercent = revenue <= 0m ? 0m : Math.Round(((revenue - totalCost) / revenue) * 100m, 2),
                Explanations = computation.ProfitEstimate.Explanations
                    .Concat([
                        $"Execution replan recalculated profit after industry cost changed for '{affectedNode.Title}'."
                    ])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            },
            Explanations = computation.Explanations
                .Concat([
                    $"Industry cost update applied to '{affectedNode.Title}' using current system cost index {latestIndustryCostChange.CurrentSystemCostIndex:P2}."
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static PlanComputationResult ApplyIndustryCostPreferenceBiases(
        IndustryPlan plan,
        PlanComputationResult computation,
        IReadOnlyList<ExecutionEventRecord> events)
    {
        var latestIndustryCostChange = events
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Select(evt => evt.Details)
            .OfType<IndustryCostChangeEventDetails>()
            .FirstOrDefault();

        if (latestIndustryCostChange is null || computation.AlternativePaths.Count == 0)
        {
            return computation;
        }

        var preferredPath = computation.AlternativePaths.FirstOrDefault(path => path.IsPreferred);
        var reprioritized = ResolveIndustryCostReprioritizedPath(plan, computation.AlternativePaths, latestIndustryCostChange, preferredPath);
        if (reprioritized is null ||
            preferredPath is null ||
            reprioritized.PathId.Equals(preferredPath.PathId, StringComparison.OrdinalIgnoreCase))
        {
            return computation;
        }

        return computation with
        {
            AlternativePaths = computation.AlternativePaths
                .Select(path => path with
                {
                    IsPreferred = path.PathId.Equals(reprioritized.PathId, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray(),
            Explanations = computation.Explanations
                .Concat([
                    $"Execution replan reprioritized '{reprioritized.Title}' after industry cost changed: {latestIndustryCostChange.ChangeSummary}"
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static decimal ResolveAdjustedIndustryCostAmount(
        decimal baselineAmount,
        PlanNode node,
        IndustryCostChangeEventDetails change)
    {
        if (baselineAmount <= 0m)
        {
            return baselineAmount;
        }

        var baselineSystemCostIndex = ReadFirstDecimal(node.Metadata, "industry_system_cost_index", "system_cost_index");
        var previousIndex = baselineSystemCostIndex
            ?? change.PreviousSystemCostIndex
            ?? 0m;
        var facilityTaxRate = ReadFirstDecimal(node.Metadata, "industry_facility_tax_rate", "facility_tax_rate")
            ?? DefaultNpcFacilityTaxRate;
        var sccSurchargeRate = ReadFirstDecimal(node.Metadata, "industry_scc_surcharge_rate", "scc_surcharge_rate")
            ?? DefaultSccSurchargeRate;
        var alphaCloneRate = ReadFirstDecimal(node.Metadata, "industry_alpha_clone_rate", "alpha_clone_rate")
            ?? DefaultOmegaAlphaCloneRate;
        var facilityBonusMultiplier = ReadFirstDecimal(node.Metadata, "industry_facility_bonus_multiplier", "facility_bonus_multiplier")
            ?? DefaultFacilityBonusMultiplier;

        var previousRate =
            (Math.Max(0m, previousIndex) * Math.Max(0m, facilityBonusMultiplier))
            + Math.Max(0m, facilityTaxRate)
            + Math.Max(0m, sccSurchargeRate)
            + Math.Max(0m, alphaCloneRate);
        var currentRate =
            (Math.Max(0m, change.CurrentSystemCostIndex) * Math.Max(0m, facilityBonusMultiplier))
            + Math.Max(0m, facilityTaxRate)
            + Math.Max(0m, sccSurchargeRate)
            + Math.Max(0m, alphaCloneRate);

        if (previousRate <= 0m)
        {
            return baselineAmount;
        }

        return Math.Round(baselineAmount * (currentRate / previousRate), 2);
    }

    private static PlanAlternativePath? ResolveIndustryCostReprioritizedPath(
        IndustryPlan plan,
        IReadOnlyList<PlanAlternativePath> alternatives,
        IndustryCostChangeEventDetails industryCostChange,
        PlanAlternativePath? preferredPath)
    {
        var signal = string.Join(
            ' ',
            [
                industryCostChange.ChangeSummary,
                industryCostChange.ImpactHint ?? string.Empty,
                industryCostChange.LocationLabel ?? string.Empty
            ]).ToLowerInvariant();

        var increased = !industryCostChange.PreviousSystemCostIndex.HasValue ||
                        industryCostChange.CurrentSystemCostIndex > industryCostChange.PreviousSystemCostIndex.Value;

        if (!increased &&
            ContainsAny(signal, "recover", "recovered", "normalized", "stabilized", "internal again", "current chain again"))
        {
            if (preferredPath is not null &&
                preferredPath.Strategy is PlanAlternativeStrategy.CurrentChain or PlanAlternativeStrategy.ProfitMaximized)
            {
                return preferredPath;
            }

            if (TryFind(alternatives, PlanAlternativeStrategy.CurrentChain, out var currentChainPath))
            {
                return currentChainPath;
            }

            if (TryFind(alternatives, PlanAlternativeStrategy.ProfitMaximized, out var profitPath))
            {
                return profitPath;
            }
        }

        if (TryFind(alternatives, PlanAlternativeStrategy.OutsourceIntermediates, out var outsourcePath) &&
            (ContainsAny(signal, "outsource", "supplier", "external", "facility became too expensive", "cost index spiked") ||
             increased))
        {
            return outsourcePath;
        }

        if (TryFind(alternatives, PlanAlternativeStrategy.MarketHeavy, out var marketHeavyPath) &&
            ContainsAny(signal, "buy", "market", "purchase", "finished") &&
            increased)
        {
            return marketHeavyPath;
        }

        return preferredPath;
    }

    private static decimal? ReadFirstDecimal(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (PlanningMetadataReader.ReadDecimal(metadata, key) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
