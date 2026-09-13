using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal static class IndustrialAlternativePathPlanner
{
    public static IReadOnlyList<PlanAlternativePath> BuildAlternativePaths(
        IndustryPlan plan,
        PlanCostBreakdown costBreakdown,
        PlanProfitEstimate profitEstimate,
        PlanTimeEstimate timeEstimate,
        int alternativePathLimit,
        IndustryPlanComputationContext context,
        IndustrialRecipeResolver recipeResolver)
    {
        var totalCost = Math.Max(1m, costBreakdown.TotalCostIsk);
        var purchaseShare = Math.Clamp(costBreakdown.PurchaseCostIsk / totalCost, 0m, 1m);
        var industryShare = Math.Clamp(costBreakdown.IndustryCostIsk / totalCost, 0m, 1m);
        var recipeNodeShare = plan.Nodes.Count == 0
            ? 0m
            : Math.Clamp((decimal)context.RecipeBackedNodeIds.Count / plan.Nodes.Count, 0m, 1m);
        var semantics = AnalyzeSemantics(plan, recipeResolver);
        var burden = AnalyzeExecutionBurden(plan);

        var candidates = new List<AlternativeCandidate>
        {
            BuildCandidate(
                BuildAlternative(
                    "path-current",
                    PlanAlternativeStrategy.CurrentChain,
                    "Current chain",
                    $"Use the graph exactly as modelled today, with the current emphasis on {semantics.DominantSemanticLabel}.",
                    costBreakdown.TotalCostIsk,
                    profitEstimate.EstimatedRevenueIsk,
                    timeEstimate.TotalDurationHours,
                    plan.Nodes.Select(node => node.NodeId).ToArray(),
                    context.RecipeBackedNodeIds.Count > 0
                        ? [$"Matches the current graph and keeps {context.RecipeBackedNodeIds.Count} real recipe-backed node(s) unchanged.", semantics.Summary]
                        : ["Matches the current graph without extra assumptions."]),
                new AlternativeBurdenEstimate(
                    burden.ManualOperations,
                    burden.TransportLegs,
                    burden.DistinctLocations))
        };

        if (plan.Nodes.Any(node => node.Kind == PlanNodeKind.Trade) &&
            plan.Nodes.Any(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction))
        {
            var marketHeavyFactors = ResolveMarketHeavyFactors(semantics, industryShare, purchaseShare);
            var estimatedCost = costBreakdown.TotalCostIsk * marketHeavyFactors.CostMultiplier;
            var estimatedDuration = timeEstimate.TotalDurationHours * marketHeavyFactors.DurationMultiplier;
            candidates.Add(BuildCandidate(
                BuildAlternative(
                    "path-market-heavy",
                    PlanAlternativeStrategy.MarketHeavy,
                    "Buy more from market",
                    BuildMarketHeavySummary(semantics),
                    estimatedCost,
                    profitEstimate.EstimatedRevenueIsk,
                    estimatedDuration,
                    plan.Nodes.Where(node => node.Kind == PlanNodeKind.Trade).Select(node => node.NodeId).ToArray(),
                    BuildMarketHeavyTradeoffs(industryShare, semantics)),
                EstimateAlternativeBurden(
                    burden,
                    manualMultiplier: 1.05m + (purchaseShare * 0.10m),
                    extraManualOps: 1,
                    extraTransportLegs: 1,
                    extraLocations: 1)));
        }

        if (plan.Preferences.AllowOutsourcing &&
            plan.Nodes.Any(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction or PlanNodeKind.CopyOrInvention))
        {
            var outsourceFactors = ResolveOutsourceFactors(semantics, recipeNodeShare);
            var estimatedCost = costBreakdown.TotalCostIsk * outsourceFactors.CostMultiplier;
            var estimatedDuration = timeEstimate.TotalDurationHours * outsourceFactors.DurationMultiplier;
            candidates.Add(BuildCandidate(
                BuildAlternative(
                    "path-outsourced",
                    PlanAlternativeStrategy.OutsourceIntermediates,
                    "Outsource intermediates",
                    BuildOutsourceSummary(semantics),
                    estimatedCost,
                    profitEstimate.EstimatedRevenueIsk,
                    estimatedDuration,
                    plan.Nodes.Where(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction or PlanNodeKind.CopyOrInvention).Select(node => node.NodeId).ToArray(),
                    BuildOutsourceTradeoffs(context, semantics)),
                EstimateAlternativeBurden(
                    burden,
                    manualMultiplier: 1.08m + (recipeNodeShare * 0.08m),
                    extraManualOps: 1,
                    extraTransportLegs: 1,
                    extraLocations: 1)));
        }

        if (plan.Preferences.PreferExistingInventory || plan.Nodes.Any(node => node.Kind == PlanNodeKind.InventoryPool))
        {
            var estimatedCost = costBreakdown.TotalCostIsk * (0.90m + (purchaseShare * 0.04m));
            var estimatedDuration = timeEstimate.TotalDurationHours * (1.04m + ((1m - purchaseShare) * 0.05m));
            candidates.Add(BuildCandidate(
                BuildAlternative(
                    "path-inventory-first",
                    PlanAlternativeStrategy.InventoryFirst,
                    "Inventory-first",
                    "Delay purchases and consume existing pools first to reduce starting capital.",
                    estimatedCost,
                    profitEstimate.EstimatedRevenueIsk,
                    estimatedDuration,
                    plan.Nodes.Where(node => node.Kind == PlanNodeKind.InventoryPool).Select(node => node.NodeId).ToArray(),
                    [
                        $"Lower upfront ISK when purchase lines currently represent {purchaseShare:P0} of modeled cost.",
                        "Can extend completion time if inventory substitutions are incomplete.",
                        "Sensitive to inventory accuracy."
                    ]),
                EstimateAlternativeBurden(
                    burden,
                    manualMultiplier: 0.88m,
                    extraManualOps: 0,
                    extraTransportLegs: 0,
                    extraLocations: 0)));
        }

        if (plan.Nodes.Any(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction))
        {
            var profitMaxFactors = ResolveProfitMaxFactors(semantics, purchaseShare, industryShare, recipeNodeShare);
            var estimatedCost = costBreakdown.TotalCostIsk * profitMaxFactors.CostMultiplier;
            var estimatedRevenue = profitEstimate.EstimatedRevenueIsk * profitMaxFactors.RevenueMultiplier;
            var estimatedDuration = timeEstimate.TotalDurationHours * profitMaxFactors.DurationMultiplier;
            candidates.Add(BuildCandidate(
                BuildAlternative(
                    "path-profit-max",
                    PlanAlternativeStrategy.ProfitMaximized,
                    "Profit-maximized",
                    BuildProfitMaxSummary(semantics),
                    estimatedCost,
                    estimatedRevenue,
                    estimatedDuration,
                    plan.Nodes.Where(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction).Select(node => node.NodeId).ToArray(),
                    BuildProfitMaxTradeoffs(context, semantics)),
                EstimateAlternativeBurden(
                    burden,
                    manualMultiplier: 1.20m + (recipeNodeShare * 0.05m),
                    extraManualOps: 1,
                    extraTransportLegs: plan.Preferences.AllowMultiLocationExecution ? 1 : 0,
                    extraLocations: plan.Preferences.AllowMultiLocationExecution ? 1 : 0)));
        }

        var materialized = candidates
            .Take(alternativePathLimit)
            .Select(candidate => ApplyPreferenceTradeoffs(candidate, plan.Preferences))
            .ToArray();

        if (materialized.Length == 0)
        {
            return Array.Empty<PlanAlternativePath>();
        }

        var preferred = SelectPreferredAlternative(materialized, plan.Preferences);
        return materialized
            .Select(candidate => candidate.Path.PathId == preferred.PathId
                ? candidate.Path with { IsPreferred = true }
                : candidate.Path)
            .ToArray();
    }

    private static AlternativeCandidate BuildCandidate(PlanAlternativePath path, AlternativeBurdenEstimate burden)
    {
        return new AlternativeCandidate(path, burden);
    }

    private static PlanAlternativePath BuildAlternative(
        string pathId,
        PlanAlternativeStrategy strategy,
        string title,
        string summary,
        decimal estimatedCost,
        decimal estimatedRevenue,
        decimal estimatedDurationHours,
        IReadOnlyList<string> affectedNodeIds,
        IReadOnlyList<string> tradeoffs)
    {
        var revenue = Math.Round(estimatedRevenue, 2);
        var cost = Math.Round(estimatedCost, 2);
        return new PlanAlternativePath
        {
            PathId = pathId,
            Strategy = strategy,
            Title = title,
            Summary = summary,
            EstimatedCostIsk = cost,
            EstimatedRevenueIsk = revenue,
            EstimatedProfitIsk = Math.Round(revenue - cost, 2),
            EstimatedDurationHours = Math.Round(estimatedDurationHours, 2),
            Tradeoffs = tradeoffs,
            AffectedNodeIds = affectedNodeIds
        };
    }

    private static AlternativeBurdenEstimate EstimateAlternativeBurden(
        ExecutionBurdenProfile current,
        decimal manualMultiplier,
        int extraManualOps,
        int extraTransportLegs,
        int extraLocations)
    {
        var manualOps = Math.Max(1, (int)Math.Ceiling(current.ManualOperations * manualMultiplier) + extraManualOps);
        var transportLegs = Math.Max(0, current.TransportLegs + extraTransportLegs);
        var distinctLocations = Math.Max(1, current.DistinctLocations + extraLocations);
        return new AlternativeBurdenEstimate(manualOps, transportLegs, distinctLocations);
    }

    private static AlternativeCandidate ApplyPreferenceTradeoffs(AlternativeCandidate candidate, PlanPreferences preferences)
    {
        var tradeoffs = new List<string>(candidate.Path.Tradeoffs)
        {
            $"Estimated manual touchpoints: {candidate.Burden.ManualOperations}.",
            $"Estimated transport legs: {candidate.Burden.TransportLegs}.",
            $"Estimated execution locations: {candidate.Burden.DistinctLocations}."
        };

        if (preferences.MaxDailyManualOperations.HasValue &&
            candidate.Burden.ManualOperations > preferences.MaxDailyManualOperations.Value)
        {
            tradeoffs.Add($"Exceeds manual-operation budget {preferences.MaxDailyManualOperations.Value} by {candidate.Burden.ManualOperations - preferences.MaxDailyManualOperations.Value}.");
        }

        if (preferences.MaxAcceptedTransportLegs.HasValue &&
            candidate.Burden.TransportLegs > preferences.MaxAcceptedTransportLegs.Value)
        {
            tradeoffs.Add($"Exceeds transport-leg budget {preferences.MaxAcceptedTransportLegs.Value} by {candidate.Burden.TransportLegs - preferences.MaxAcceptedTransportLegs.Value}.");
        }

        if (preferences.LogisticsTolerance == PlanLogisticsTolerance.Minimal &&
            candidate.Burden.TransportLegs > 0)
        {
            tradeoffs.Add("Conflicts with minimal logistics tolerance because it still needs hauling.");
        }
        else if (preferences.LogisticsTolerance == PlanLogisticsTolerance.Balanced &&
                 candidate.Burden.TransportLegs > 2)
        {
            tradeoffs.Add("Pushes beyond balanced logistics tolerance because it spreads across too many hauling legs.");
        }

        return candidate with
        {
            Path = candidate.Path with
            {
                Tradeoffs = tradeoffs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            }
        };
    }

    private static PlanAlternativePath SelectPreferredAlternative(
        IReadOnlyList<AlternativeCandidate> alternatives,
        PlanPreferences preferences)
    {
        return preferences.PrimaryPriority switch
        {
            PlanningPriority.TimeFirst => alternatives
                .OrderBy(path => path.Path.EstimatedDurationHours + ResolveDurationPenaltyHours(path.Burden, preferences))
                .ThenBy(path => path.Path.EstimatedCostIsk)
                .Select(path => path.Path)
                .First(),
            PlanningPriority.ProfitFirst => alternatives
                .OrderByDescending(path => path.Path.EstimatedProfitIsk - ResolveIskPenalty(path.Burden, preferences))
                .ThenBy(path => path.Path.EstimatedDurationHours)
                .Select(path => path.Path)
                .First(),
            PlanningPriority.CapitalFirst => alternatives
                .OrderBy(path => path.Path.EstimatedCostIsk + ResolveIskPenalty(path.Burden, preferences))
                .ThenBy(path => path.Path.EstimatedDurationHours)
                .Select(path => path.Path)
                .First(),
            _ => alternatives
                .OrderByDescending(path =>
                    path.Path.EstimatedProfitIsk
                    - (path.Path.EstimatedDurationHours * 100000m)
                    - ResolveIskPenalty(path.Burden, preferences))
                .ThenBy(path => path.Path.EstimatedCostIsk)
                .Select(path => path.Path)
                .First()
        };
    }

    private static decimal ResolveDurationPenaltyHours(AlternativeBurdenEstimate burden, PlanPreferences preferences)
    {
        var penalty = 0m;

        if (preferences.MaxDailyManualOperations.HasValue &&
            burden.ManualOperations > preferences.MaxDailyManualOperations.Value)
        {
            penalty += (burden.ManualOperations - preferences.MaxDailyManualOperations.Value) * 6m;
        }

        if (preferences.MaxAcceptedTransportLegs.HasValue &&
            burden.TransportLegs > preferences.MaxAcceptedTransportLegs.Value)
        {
            penalty += (burden.TransportLegs - preferences.MaxAcceptedTransportLegs.Value) * 8m;
        }

        penalty += preferences.LogisticsTolerance switch
        {
            PlanLogisticsTolerance.Minimal when burden.TransportLegs > 0 => burden.TransportLegs * 12m,
            PlanLogisticsTolerance.Balanced when burden.TransportLegs > 2 => (burden.TransportLegs - 2) * 6m,
            _ => 0m
        };

        if (preferences.PreferSimplerChains &&
            burden.ManualOperations > 0 &&
            burden.DistinctLocations > 1)
        {
            penalty += 2m;
        }

        return penalty;
    }

    private static decimal ResolveIskPenalty(AlternativeBurdenEstimate burden, PlanPreferences preferences)
    {
        var penalty = 0m;

        if (preferences.MaxDailyManualOperations.HasValue &&
            burden.ManualOperations > preferences.MaxDailyManualOperations.Value)
        {
            penalty += (burden.ManualOperations - preferences.MaxDailyManualOperations.Value) * 25_000_000m;
        }

        if (preferences.MaxAcceptedTransportLegs.HasValue &&
            burden.TransportLegs > preferences.MaxAcceptedTransportLegs.Value)
        {
            penalty += (burden.TransportLegs - preferences.MaxAcceptedTransportLegs.Value) * 40_000_000m;
        }

        penalty += preferences.LogisticsTolerance switch
        {
            PlanLogisticsTolerance.Minimal when burden.TransportLegs > 0 => burden.TransportLegs * 60_000_000m,
            PlanLogisticsTolerance.Balanced when burden.TransportLegs > 2 => (burden.TransportLegs - 2) * 15_000_000m,
            _ => 0m
        };

        if (preferences.PreferSimplerChains && burden.ManualOperations >= 6)
        {
            penalty += 10_000_000m;
        }

        return penalty;
    }

    private static ExecutionBurdenProfile AnalyzeExecutionBurden(IndustryPlan plan)
    {
        var manualOperations = plan.Nodes.Sum(EstimateManualTouchpoints);
        var transportLegs = plan.Nodes.Count(node => node.Kind == PlanNodeKind.Transport);
        var distinctLocations = CollectExecutionLocations(plan).Count;
        return new ExecutionBurdenProfile(
            Math.Max(1, manualOperations),
            transportLegs,
            Math.Max(1, distinctLocations));
    }

    private static int EstimateManualTouchpoints(PlanNode node)
    {
        return node.Kind switch
        {
            PlanNodeKind.InventoryPool => 0,
            PlanNodeKind.Transport => 1,
            PlanNodeKind.Trade when node.Details is TradeNodeDetails { TradeMode: PlanTradeMode.Sale } => 1,
            PlanNodeKind.Trade => 1,
            PlanNodeKind.Production or PlanNodeKind.Reaction or PlanNodeKind.CopyOrInvention => 1,
            _ => 1
        };
    }

    private static IReadOnlyCollection<string> CollectExecutionLocations(IndustryPlan plan)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in plan.Nodes)
        {
            var locationLabel = PlanningMetadataReader.NormalizeOptional(node.LocationLabel);
            if (locationLabel is not null)
            {
                locations.Add(locationLabel);
            }

            if (node.Details is TransportNodeDetails transport)
            {
                var source = PlanningMetadataReader.NormalizeOptional(transport.SourceLocation);
                var destination = PlanningMetadataReader.NormalizeOptional(transport.DestinationLocation);
                if (source is not null)
                {
                    locations.Add(source);
                }

                if (destination is not null)
                {
                    locations.Add(destination);
                }
            }
        }

        return locations;
    }

    private static AlternativeEstimateFactors ResolveMarketHeavyFactors(
        SemanticProfile semantics,
        decimal industryShare,
        decimal purchaseShare)
    {
        return semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => new AlternativeEstimateFactors(
                1.08m + (industryShare * 0.10m),
                1m,
                0.70m - (purchaseShare * 0.05m)),
            "invention_chain" => new AlternativeEstimateFactors(
                1.03m + (industryShare * 0.06m),
                1m,
                0.72m - (purchaseShare * 0.06m)),
            "reaction_chain" => new AlternativeEstimateFactors(
                1.05m + (industryShare * 0.07m),
                1m,
                0.74m - (purchaseShare * 0.05m)),
            _ => new AlternativeEstimateFactors(
                1.04m + (industryShare * 0.08m),
                1m,
                0.78m - (purchaseShare * 0.08m))
        };
    }

    private static AlternativeEstimateFactors ResolveOutsourceFactors(
        SemanticProfile semantics,
        decimal recipeNodeShare)
    {
        return semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => new AlternativeEstimateFactors(
                1.10m + (recipeNodeShare * 0.10m),
                1m,
                0.74m - (recipeNodeShare * 0.05m)),
            "invention_chain" => new AlternativeEstimateFactors(
                1.04m + (recipeNodeShare * 0.06m),
                1m,
                0.76m - (recipeNodeShare * 0.05m)),
            "reaction_chain" => new AlternativeEstimateFactors(
                1.05m + (recipeNodeShare * 0.07m),
                1m,
                0.77m - (recipeNodeShare * 0.05m)),
            _ => new AlternativeEstimateFactors(
                1.06m + (recipeNodeShare * 0.08m),
                1m,
                0.82m - (recipeNodeShare * 0.06m))
        };
    }

    private static AlternativeEstimateFactors ResolveProfitMaxFactors(
        SemanticProfile semantics,
        decimal purchaseShare,
        decimal industryShare,
        decimal recipeNodeShare)
    {
        return semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => new AlternativeEstimateFactors(
                0.97m - Math.Min(0.02m, purchaseShare * 0.02m),
                1.008m + (industryShare * 0.012m),
                1.18m + (recipeNodeShare * 0.12m)),
            "reaction_chain" => new AlternativeEstimateFactors(
                0.96m - Math.Min(0.025m, purchaseShare * 0.025m),
                1.006m + (industryShare * 0.011m),
                1.13m + (recipeNodeShare * 0.11m)),
            "invention_chain" => new AlternativeEstimateFactors(
                0.96m - Math.Min(0.025m, purchaseShare * 0.025m),
                1.007m + (industryShare * 0.011m),
                1.15m + (recipeNodeShare * 0.11m)),
            _ => new AlternativeEstimateFactors(
                0.95m - Math.Min(0.03m, purchaseShare * 0.03m),
                1.005m + (industryShare * 0.01m),
                1.10m + (recipeNodeShare * 0.10m))
        };
    }

    private static SemanticProfile AnalyzeSemantics(IndustryPlan plan, IndustrialRecipeResolver recipeResolver)
    {
        var counts = plan.Nodes
            .Where(node => node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction or PlanNodeKind.CopyOrInvention)
            .GroupBy(node => recipeResolver.ResolvePlannerSemanticClass(node), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var dominantSemantic = counts.Count == 0
            ? "standard_manufacturing"
            : counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First()
                .Key;

        var dominantSemanticLabel = recipeResolver.DescribePlannerSemanticClass(dominantSemantic);
        var summary = counts.Count == 0
            ? "No recipe-backed semantic mix was detected."
            : "Current recipe mix: "
              + string.Join(", ",
                  counts.OrderByDescending(pair => pair.Value)
                      .Select(pair => $"{recipeResolver.DescribePlannerSemanticClass(pair.Key)} x {pair.Value}"));

        return new SemanticProfile(dominantSemantic, dominantSemanticLabel, summary);
    }

    private static string BuildMarketHeavySummary(SemanticProfile semantics) =>
        semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => "Trade more ISK for a shorter capital build chain by buying more capital-stage inputs.",
            "invention_chain" => "Reduce invention-chain depth by buying more researched or invented intermediates from market.",
            "reaction_chain" => "Shorten the reaction pipeline by buying more reaction outputs from market.",
            _ => "Trade more ISK for a shorter chain by leaning on market purchases."
        };

    private static IReadOnlyList<string> BuildMarketHeavyTradeoffs(decimal industryShare, SemanticProfile semantics)
    {
        var first = semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => $"Higher cash burn, especially because capital-stage industry currently represents {industryShare:P0} of modeled cost.",
            "invention_chain" => $"Higher cash burn, especially because invention-stage work currently represents {industryShare:P0} of modeled cost.",
            "reaction_chain" => $"Higher cash burn, especially because reaction-stage work currently represents {industryShare:P0} of modeled cost.",
            _ => $"Higher cash burn, especially because industry currently represents {industryShare:P0} of modeled cost."
        };

        return
        [
            first,
            "Shorter critical path by externalizing more intermediate work.",
            "Depends more on market access and market depth."
        ];
    }

    private static string BuildOutsourceSummary(SemanticProfile semantics) =>
        semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => "Reduce internal capital-yard pressure by outsourcing selected capital intermediates.",
            "invention_chain" => "Reduce internal invention-lab pressure by outsourcing selected invention-stage outputs.",
            "reaction_chain" => "Reduce internal reaction pressure by outsourcing selected reaction intermediates.",
            _ => "Reduce internal industry load by outsourcing selected intermediates."
        };

    private static IReadOnlyList<string> BuildOutsourceTradeoffs(IndustryPlanComputationContext context, SemanticProfile semantics)
    {
        var first = semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => $"Lower capital-yard pressure across {context.RecipeBackedNodeIds.Count} recipe-backed industrial node(s).",
            "invention_chain" => $"Lower invention-lab pressure across {context.RecipeBackedNodeIds.Count} recipe-backed industrial node(s).",
            "reaction_chain" => $"Lower reaction-slot pressure across {context.RecipeBackedNodeIds.Count} recipe-backed industrial node(s).",
            _ => $"Lower internal slot pressure across {context.RecipeBackedNodeIds.Count} recipe-backed industrial node(s)."
        };

        return
        [
            first,
            "More dependent on external suppliers.",
            "Can simplify the graph when internal chain depth is the current bottleneck."
        ];
    }

    private static string BuildProfitMaxSummary(SemanticProfile semantics) =>
        semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => "Keep more capital-stage work in-house and accept a slower chain for higher expected margin.",
            "reaction_chain" => "Keep more reaction throughput in-house and accept a slower chain for higher expected margin.",
            _ => "Keep more work in-house and accept a slower chain for higher expected margin."
        };

    private static IReadOnlyList<string> BuildProfitMaxTradeoffs(IndustryPlanComputationContext context, SemanticProfile semantics)
    {
        var second = semantics.DominantSemanticClass switch
        {
            "capital_manufacturing" => $"Better expected margin if the in-house capital chain can realize its recipe-backed efficiencies across {context.RecipeBackedNodeIds.Count} node(s).",
            "reaction_chain" => $"Better expected margin if the in-house reaction chain can realize its recipe-backed efficiencies across {context.RecipeBackedNodeIds.Count} node(s).",
            "invention_chain" => $"Better expected margin if the in-house invention chain can realize its recipe-backed efficiencies across {context.RecipeBackedNodeIds.Count} node(s).",
            _ => $"Better expected margin if the in-house chain can realize its recipe-backed efficiencies across {context.RecipeBackedNodeIds.Count} node(s)."
        };

        return
        [
            "Higher execution complexity.",
            second,
            "Consumes more reserved slots."
        ];
    }

    private sealed record SemanticProfile(
        string DominantSemanticClass,
        string DominantSemanticLabel,
        string Summary);

    private sealed record AlternativeEstimateFactors(
        decimal CostMultiplier,
        decimal RevenueMultiplier,
        decimal DurationMultiplier);

    private sealed record ExecutionBurdenProfile(
        int ManualOperations,
        int TransportLegs,
        int DistinctLocations);

    private sealed record AlternativeBurdenEstimate(
        int ManualOperations,
        int TransportLegs,
        int DistinctLocations);

    private sealed record AlternativeCandidate(
        PlanAlternativePath Path,
        AlternativeBurdenEstimate Burden);
}
