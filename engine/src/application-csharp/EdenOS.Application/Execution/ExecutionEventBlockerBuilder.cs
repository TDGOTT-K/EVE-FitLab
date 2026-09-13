using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Application.Planning;

namespace EdenOS.Application.Execution;

internal static class ExecutionEventBlockerBuilder
{
    public static IReadOnlyList<ExecutionBlocker> Build(
        IndustryPlan plan,
        ExecutionState state,
        PlanComputationResult computation)
    {
        var blockers = new List<ExecutionBlocker>();

        blockers.AddRange(computation.ConstraintSummary.HardConflicts.Select((message, index) =>
            new ExecutionBlocker
            {
                BlockerId = $"constraint-{index + 1}",
                Kind = ExecutionBlockerKind.ConstraintConflict,
                Severity = ExecutionBlockerSeverity.Critical,
                Title = "Plan constraint conflict",
                Summary = message,
                SuggestedResolution = "Review the relevant node semantics or run execution.replan after correcting the plan."
            }));

        blockers.AddRange(computation.ConstraintSummary.NodeStatuses
            .Where(status => status.StatusKind == PlanNodeConstraintStatusKind.Blocked)
            .Select(status => new ExecutionBlocker
            {
                BlockerId = $"plan-constraint-{status.NodeId}",
                Kind = ExecutionBlockerKind.ConstraintConflict,
                Severity = ExecutionBlockerSeverity.Critical,
                Title = $"Plan constraint blocks '{status.Title}'",
                Summary = status.Reasons.Count == 0
                    ? $"Plan computation marked '{status.Title}' as blocked."
                    : string.Join(" ", status.Reasons),
                NodeId = status.NodeId,
                SuggestedResolution = "Correct the node contract or upstream plan shape, then rerun execution.replan."
            }));

        blockers.AddRange(BuildMaterialDeltaBlockers(state.Events));

        var latestMarketChange = state.Events
            .Where(evt => evt.Kind == ExecutionEventKind.MarketChange)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .FirstOrDefault();
        if (latestMarketChange is not null)
        {
            blockers.AddRange(BuildMarketChangeBlockers(latestMarketChange, plan, computation));
        }

        var latestIndustryCostChange = state.Events
            .Where(evt => evt.Kind == ExecutionEventKind.IndustryCostChange)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .FirstOrDefault();
        if (latestIndustryCostChange is not null)
        {
            blockers.AddRange(BuildIndustryCostChangeBlockers(latestIndustryCostChange, plan, computation));
        }

        blockers.AddRange(state.Events
            .Where(evt => evt.Kind == ExecutionEventKind.LocationChange)
            .Select(evt => new
            {
                Event = evt,
                ScopeKey = NormalizeOptional((evt.Details as LocationChangeEventDetails)?.NodeId) ?? "global"
            })
            .GroupBy(entry => entry.ScopeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.Event.OccurredAtUtc)
                .ThenByDescending(entry => entry.Event.RecordedAtUtc)
                .First()
                .Event)
            .SelectMany(evt => BuildLocationChangeBlockers(evt, plan)));

        blockers.AddRange(BuildManualOverrideBlockers(state.Events, plan));

        blockers.AddRange(computation.ConstraintSummary.UncertainFacts.Select((message, index) =>
            new ExecutionBlocker
            {
                BlockerId = $"uncertain-{index + 1}",
                Kind = ExecutionBlockerKind.UncertainFact,
                Severity = ExecutionBlockerSeverity.Warning,
                Title = "Fact uncertainty is still exposed",
                Summary = message,
                SuggestedResolution = "Treat the recommendation as provisional until the missing fact is confirmed."
            }));

        return blockers
            .DistinctBy(blocker => blocker.BlockerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ExecutionBlocker> BuildMaterialDeltaBlockers(IReadOnlyList<ExecutionEventRecord> events)
    {
        return events
            .Where(evt => evt.Kind is ExecutionEventKind.MaterialArrival or ExecutionEventKind.MaterialLoss)
            .GroupBy(CreateMaterialKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var lossQuantity = group
                    .Where(evt => evt.Details is MaterialLossEventDetails)
                    .Select(evt => ((MaterialLossEventDetails)evt.Details).Quantity)
                    .DefaultIfEmpty(0m)
                    .Sum();
                var arrivalQuantity = group
                    .Where(evt => evt.Details is MaterialArrivalEventDetails)
                    .Select(evt => ((MaterialArrivalEventDetails)evt.Details).Quantity)
                    .DefaultIfEmpty(0m)
                    .Sum();

                if (lossQuantity <= arrivalQuantity)
                {
                    return null;
                }

                var latestLoss = group.Last(evt => evt.Details is MaterialLossEventDetails);
                var lossDetails = (MaterialLossEventDetails)latestLoss.Details;
                return new ExecutionBlocker
                {
                    BlockerId = $"material-{group.Key}",
                    Kind = ExecutionBlockerKind.MaterialShortage,
                    Severity = ExecutionBlockerSeverity.Critical,
                    Title = "Material shortage remains unresolved",
                    Summary = $"Recorded loss still exceeds arrivals for '{lossDetails.ResourceName}' by {lossQuantity - arrivalQuantity:0.##}.",
                    NodeId = lossDetails.NodeId,
                    ResourceTypeId = lossDetails.ResourceTypeId,
                    RelatedEventIds = group.Select(evt => evt.EventId).ToArray(),
                    SuggestedResolution = "Record replacement arrivals or adjust the plan and rerun execution.replan."
                };
            })
            .Where(blocker => blocker is not null)
            .Cast<ExecutionBlocker>()
            .ToArray();
    }

    private static IReadOnlyList<ExecutionBlocker> BuildLocationChangeBlockers(ExecutionEventRecord evt, IndustryPlan plan)
    {
        var details = evt.Details as LocationChangeEventDetails;
        if (details is null || details.IsRecovered)
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var nodeId = NormalizeOptional(details.NodeId);
        var title = ResolveNodeTitle(plan, nodeId) ?? details.NodeTitle ?? "target node";

        return
        [
            new ExecutionBlocker
            {
                BlockerId = $"location-{evt.EventId}-{nodeId ?? "global"}",
                Kind = ExecutionBlockerKind.LocationDrift,
                Severity = ExecutionBlockerSeverity.Warning,
                Title = $"Execution location changed for '{title}'",
                Summary = evt.Summary,
                NodeId = nodeId,
                RelatedEventIds = [evt.EventId],
                SuggestedResolution = "Review transport and multi-location assumptions, then replan if the new location persists."
            }
        ];
    }

    private static IReadOnlyList<ExecutionBlocker> BuildMarketChangeBlockers(
        ExecutionEventRecord evt,
        IndustryPlan plan,
        PlanComputationResult computation)
    {
        if (!ShouldSurfaceMarketChangeBlocker(evt, computation))
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var preferredAlternative = computation.AlternativePaths.FirstOrDefault(path => path.IsPreferred);
        var candidateNodeIds = preferredAlternative?.AffectedNodeIds
            .Select(NormalizeOptional)
            .Where(nodeId => nodeId is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var marketChange = evt.Details as MarketChangeEventDetails;
        var affectedNodeIds = candidateNodeIds?
            .Where(nodeId => MarketChangeAppliesToNode(plan, nodeId, marketChange))
            .ToArray();

        if (affectedNodeIds is null || affectedNodeIds.Length == 0)
        {
            return
            [
                new ExecutionBlocker
                {
                    BlockerId = $"market-{evt.EventId}",
                    Kind = ExecutionBlockerKind.MarketShift,
                    Severity = ExecutionBlockerSeverity.Warning,
                    Title = "Market conditions changed",
                    Summary = evt.Summary,
                    RelatedEventIds = [evt.EventId],
                    SuggestedResolution = "Run execution.replan so the latest market change is reflected in cost, profit, and next-action guidance."
                }
            ];
        }

        return affectedNodeIds
            .Select(nodeId =>
            {
                var title = ResolveNodeTitle(plan, nodeId) ?? nodeId;
                return new ExecutionBlocker
                {
                    BlockerId = $"market-{evt.EventId}-{nodeId}",
                    Kind = ExecutionBlockerKind.MarketShift,
                    Severity = ExecutionBlockerSeverity.Warning,
                    Title = $"Market conditions changed for '{title}'",
                    Summary = evt.Summary,
                    NodeId = nodeId,
                    RelatedEventIds = [evt.EventId],
                    SuggestedResolution = "Review the affected market-driven node and rerun execution.replan before publishing work."
                };
            })
            .ToArray();
    }

    private static bool MarketChangeAppliesToNode(
        IndustryPlan plan,
        string nodeId,
        MarketChangeEventDetails? marketChange)
    {
        var node = plan.Nodes.FirstOrDefault(candidate =>
            candidate.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
        if (node?.Details is not TradeNodeDetails tradeNode || !tradeNode.UsesMarketFacts)
        {
            return false;
        }

        if (marketChange is null)
        {
            return true;
        }

        if (marketChange.MarketLocationId.HasValue &&
            (!PlanningMetadataReader.TryReadLong(node.Metadata, "market_location_id", out var nodeMarketLocationId) ||
             nodeMarketLocationId != marketChange.MarketLocationId.Value))
        {
            return false;
        }

        var normalizedResourceTypeId = PlanningMetadataReader.NormalizeOptional(marketChange.ResourceTypeId);
        if (normalizedResourceTypeId is null)
        {
            return true;
        }

        return plan.Links.Any(link =>
            (link.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) ||
             link.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(
                PlanningMetadataReader.NormalizeOptional(link.ResourceTypeId),
                normalizedResourceTypeId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldSurfaceMarketChangeBlocker(
        ExecutionEventRecord evt,
        PlanComputationResult computation)
    {
        if (evt.Details is not MarketChangeEventDetails marketChange)
        {
            return true;
        }

        var signal = string.Join(
            ' ',
            [
                marketChange.ChangeSummary,
                marketChange.ImpactHint ?? string.Empty,
                marketChange.ResourceName ?? string.Empty
            ]).ToLowerInvariant();

        var preferredStrategy = computation.AlternativePaths
            .FirstOrDefault(path => path.IsPreferred)
            ?.Strategy;

        if (preferredStrategy is PlanAlternativeStrategy.CurrentChain or PlanAlternativeStrategy.ProfitMaximized &&
            ContainsAny(
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
                "return to internal production"))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<ExecutionBlocker> BuildManualOverrideBlockers(
        IReadOnlyList<ExecutionEventRecord> events,
        IndustryPlan plan)
    {
        return events
            .SelectMany(ExpandManualOverrideScopeEvents)
            .GroupBy(entry => entry.ScopeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => entry.Event.OccurredAtUtc)
                .ThenByDescending(entry => entry.Event.RecordedAtUtc)
                .First())
            .Where(entry => entry.Event.Kind == ExecutionEventKind.ManualOverride)
            .Select(entry => CreateManualOverrideBlocker(entry.Event, plan, entry.NodeId))
            .ToArray();
    }

    private static IEnumerable<(string ScopeKey, string? NodeId, ExecutionEventRecord Event)> ExpandManualOverrideScopeEvents(ExecutionEventRecord evt)
    {
        return evt.Details switch
        {
            ManualOverrideEventDetails overrideDetails => ExpandScopedEntries(
                evt,
                overrideDetails.AffectedNodeIds,
                globalScopeKey: "global"),
            ManualOverrideResolvedEventDetails resolutionDetails => ExpandScopedEntries(
                evt,
                resolutionDetails.AffectedNodeIds,
                globalScopeKey: "global"),
            _ => Array.Empty<(string ScopeKey, string? NodeId, ExecutionEventRecord Event)>()
        };
    }

    private static IEnumerable<(string ScopeKey, string? NodeId, ExecutionEventRecord Event)> ExpandScopedEntries(
        ExecutionEventRecord evt,
        IReadOnlyList<string> affectedNodeIds,
        string globalScopeKey)
    {
        var normalizedNodeIds = affectedNodeIds
            .Select(NormalizeOptional)
            .Where(nodeId => nodeId is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedNodeIds.Length == 0)
        {
            return [(globalScopeKey, null, evt)];
        }

        return normalizedNodeIds.Select(nodeId => (nodeId, (string?)nodeId, evt));
    }

    private static ExecutionBlocker CreateManualOverrideBlocker(
        ExecutionEventRecord evt,
        IndustryPlan plan,
        string? nodeId)
    {
        if (NormalizeOptional(nodeId) is null)
        {
            return new ExecutionBlocker
            {
                BlockerId = $"override-{evt.EventId}",
                Kind = ExecutionBlockerKind.ManualOverride,
                Severity = ExecutionBlockerSeverity.Warning,
                Title = "Manual override requires review",
                Summary = evt.Summary,
                RelatedEventIds = [evt.EventId],
                SuggestedResolution = "Confirm the manual override is still desired, then use execution.resolve_manual_override or rerun execution.replan after clearing it."
            };
        }

        var title = ResolveNodeTitle(plan, nodeId) ?? nodeId;
        return new ExecutionBlocker
        {
            BlockerId = $"override-{evt.EventId}-{nodeId}",
            Kind = ExecutionBlockerKind.ManualOverride,
            Severity = ExecutionBlockerSeverity.Warning,
            Title = $"Manual override blocks '{title}'",
            Summary = evt.Summary,
            NodeId = nodeId,
            RelatedEventIds = [evt.EventId],
            SuggestedResolution = "Confirm the manual override is still desired, then use execution.resolve_manual_override or rerun execution.replan after clearing it."
        };
    }

    private static IReadOnlyList<ExecutionBlocker> BuildIndustryCostChangeBlockers(
        ExecutionEventRecord evt,
        IndustryPlan plan,
        PlanComputationResult computation)
    {
        if (!ShouldSurfaceIndustryCostChangeBlocker(evt, computation))
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var details = evt.Details as IndustryCostChangeEventDetails;
        if (details is null)
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var affectedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(details.NodeId))
        {
            affectedNodeIds.Add(details.NodeId.Trim());
        }

        if (affectedNodeIds.Count == 0)
        {
            return
            [
                new ExecutionBlocker
                {
                    BlockerId = $"industry-cost-{evt.EventId}",
                    Kind = ExecutionBlockerKind.IndustryCostShift,
                    Severity = ExecutionBlockerSeverity.Warning,
                    Title = "Industry cost conditions changed",
                    Summary = evt.Summary,
                    RelatedEventIds = [evt.EventId],
                    SuggestedResolution = "Review affected industry work and rerun execution.replan before publishing related tasks."
                }
            ];
        }

        return affectedNodeIds
            .Select(nodeId =>
            {
                var title = ResolveNodeTitle(plan, nodeId) ?? details.NodeTitle ?? nodeId;
                return new ExecutionBlocker
                {
                    BlockerId = $"industry-cost-{evt.EventId}-{nodeId}",
                    Kind = ExecutionBlockerKind.IndustryCostShift,
                    Severity = ExecutionBlockerSeverity.Warning,
                    Title = $"Industry cost conditions changed for '{title}'",
                    Summary = evt.Summary,
                    NodeId = nodeId,
                    RelatedEventIds = [evt.EventId],
                    SuggestedResolution = "Review whether this node should stay internal, be outsourced, or use a different facility before publishing work."
                };
            })
            .ToArray();
    }

    private static string? ResolveNodeTitle(IndustryPlan plan, string? nodeId)
    {
        var normalizedNodeId = NormalizeOptional(nodeId);
        if (normalizedNodeId is null)
        {
            return null;
        }

        return plan.Nodes
            .FirstOrDefault(node => node.NodeId.Equals(normalizedNodeId, StringComparison.OrdinalIgnoreCase))
            ?.Title;
    }

    private static string CreateMaterialKey(ExecutionEventRecord evt)
    {
        return evt.Details switch
        {
            MaterialArrivalEventDetails arrival => $"{arrival.NodeId ?? "global"}:{arrival.ResourceTypeId}",
            MaterialLossEventDetails loss => $"{loss.NodeId ?? "global"}:{loss.ResourceTypeId}",
            _ => evt.EventId
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool ShouldSurfaceIndustryCostChangeBlocker(
        ExecutionEventRecord evt,
        PlanComputationResult computation)
    {
        if (evt.Details is not IndustryCostChangeEventDetails industryCostChange)
        {
            return true;
        }

        var signal = string.Join(
            ' ',
            [
                industryCostChange.ChangeSummary,
                industryCostChange.ImpactHint ?? string.Empty,
                industryCostChange.LocationLabel ?? string.Empty
            ]).ToLowerInvariant();

        var preferredStrategy = computation.AlternativePaths
            .FirstOrDefault(path => path.IsPreferred)
            ?.Strategy;

        if (preferredStrategy is PlanAlternativeStrategy.CurrentChain or PlanAlternativeStrategy.ProfitMaximized &&
            industryCostChange.PreviousSystemCostIndex.HasValue &&
            industryCostChange.CurrentSystemCostIndex <= industryCostChange.PreviousSystemCostIndex.Value &&
            ContainsAny(signal, "recovered", "normalized", "stabilized", "cheaper again", "internal again"))
        {
            return false;
        }

        return true;
    }
}
