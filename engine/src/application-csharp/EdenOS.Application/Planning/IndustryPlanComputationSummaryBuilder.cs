using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal static class IndustryPlanComputationSummaryBuilder
{
    public static PlanCostBreakdown BuildCostBreakdown(
        IReadOnlyList<PlanCostLineItem> lineItems,
        IndustryPlanComputationContext context)
    {
        var purchaseCost = lineItems
            .Where(item => item.Category == "purchase")
            .Sum(item => item.AmountIsk);
        var industryCost = lineItems
            .Where(item => item.Category == "industry_job")
            .Sum(item => item.AmountIsk);
        var logisticsCost = lineItems
            .Where(item => item.Category == "logistics")
            .Sum(item => item.AmountIsk);
        var otherCost = lineItems
            .Where(item => item.Category is not ("purchase" or "industry_job" or "logistics" or "revenue"))
            .Sum(item => item.AmountIsk);
        var totalCost = purchaseCost + industryCost + logisticsCost + otherCost;

        var explanations = new List<string>
        {
            $"Purchase cost lines: {lineItems.Count(item => item.Category == "purchase")}.",
            $"Industry job cost lines: {lineItems.Count(item => item.Category == "industry_job")}.",
            $"Logistics cost lines: {lineItems.Count(item => item.Category == "logistics")}."
        };

        if (context.RecipeBackedNodeIds.Count > 0)
        {
            explanations.Add("Some purchase lines were derived from real SDE recipe material gaps rather than only explicit trade nodes.");
        }

        if (context.RecipeSemanticClassByNodeId.Count > 0)
        {
            var semanticMix = context.RecipeSemanticClassByNodeId.Values
                .GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{DescribePlannerSemanticClass(group.Key)} x {group.Count()}")
                .ToArray();
            explanations.Add($"Recipe-backed semantic mix: {string.Join(", ", semanticMix)}.");
        }

        if (context.ProbabilityAdjustedNodeIds.Count > 0)
        {
            explanations.Add("Expected invention attempts were expanded by blueprint success probability before pricing material gaps.");
        }

        if (context.CharacterAdjustedProbabilityNodeIds.Count > 0)
        {
            explanations.Add("Suggested character invention bonuses further reduced expected invention attempts where a qualified assignee was selected.");
        }

        if (context.UsedPlaceholderMarketFacts)
        {
            explanations.Add("Purchase pricing includes placeholder market facts from the current in-memory market surface.");
        }

        explanations.AddRange(context.CostTraceExplanations);

        return new PlanCostBreakdown
        {
            TotalCostIsk = Math.Round(totalCost, 2),
            PurchaseCostIsk = Math.Round(purchaseCost, 2),
            IndustryCostIsk = Math.Round(industryCost, 2),
            LogisticsCostIsk = Math.Round(logisticsCost, 2),
            OtherCostIsk = Math.Round(otherCost, 2),
            LineItems = lineItems.Where(item => item.Category != "revenue").ToArray(),
            Explanations = explanations
        };
    }

    private static string DescribePlannerSemanticClass(string plannerSemanticClass)
    {
        return plannerSemanticClass switch
        {
            "standard_manufacturing" => "standard manufacturing",
            "capital_manufacturing" => "capital manufacturing",
            "reaction_chain" => "reaction chain",
            "invention_chain" => "invention chain",
            "blueprint_copying" => "blueprint copying",
            "blueprint_research" => "blueprint research",
            _ => "industrial work"
        };
    }

    public static PlanProfitEstimate BuildProfitEstimate(
        IndustryPlan plan,
        PlanCostBreakdown costBreakdown,
        IReadOnlyList<PlanCostLineItem> lineItems,
        IndustryPlanComputationContext context)
    {
        var revenue = lineItems
            .Where(item => item.Category == "revenue")
            .Sum(item => item.AmountIsk);
        var profit = revenue - costBreakdown.TotalCostIsk;
        var margin = revenue <= 0m ? 0m : Math.Round((profit / revenue) * 100m, 2);

        var confidence = context.HardConflicts.Count > 0 || context.UncertainFacts.Count > 0
            ? PlanEstimateConfidence.Low
            : context.UsedPlaceholderMarketFacts
                ? PlanEstimateConfidence.Medium
                : PlanEstimateConfidence.High;

        var explanations = new List<string>();
        if (plan.Goal is not null)
        {
            explanations.Add($"Profit estimate is anchored to goal '{plan.Goal.TargetName}' with quantity {plan.Goal.Quantity}.");
        }

        explanations.Add(revenue > 0m
            ? "Revenue uses market sale-side approximations or explicit trade node pricing."
            : "No revenue line was available, so profit remains cost-only and should not drive decisions.");

        return new PlanProfitEstimate
        {
            EstimatedRevenueIsk = Math.Round(revenue, 2),
            EstimatedCostIsk = costBreakdown.TotalCostIsk,
            EstimatedProfitIsk = Math.Round(profit, 2),
            MarginPercent = margin,
            Confidence = confidence,
            Explanations = explanations
        };
    }

    public static PlanFeasibilitySummary BuildFeasibilitySummary(
        IndustryPlanComputationContext context,
        PlanCostBreakdown costBreakdown,
        PlanProfitEstimate profitEstimate)
    {
        var reasons = new List<string>();

        if (context.HardConflicts.Count == 0)
        {
            reasons.Add("No hard structural conflicts were found in the current plan graph.");
        }
        else
        {
            reasons.Add($"Detected {context.HardConflicts.Count} hard conflict(s) that block clean execution.");
        }

        if (costBreakdown.TotalCostIsk > 0m)
        {
            reasons.Add($"Estimated total cost is {costBreakdown.TotalCostIsk:N2} ISK.");
        }

        if (profitEstimate.EstimatedRevenueIsk > 0m)
        {
            reasons.Add($"Estimated revenue is {profitEstimate.EstimatedRevenueIsk:N2} ISK.");
        }

        if (context.UncertainFacts.Count > 0)
        {
            reasons.Add("Computation completed with unresolved fact gaps that lower confidence.");
        }

        var confidence = context.HardConflicts.Count > 0 || context.UncertainFacts.Count > 0
            ? PlanEstimateConfidence.Low
            : context.UsedPlaceholderMarketFacts
                ? PlanEstimateConfidence.Medium
                : PlanEstimateConfidence.High;

        return new PlanFeasibilitySummary
        {
            IsFeasible = context.HardConflicts.Count == 0,
            Confidence = confidence,
            Reasons = reasons,
            BlockingNodeIds = context.BlockingNodeIds.ToArray()
        };
    }

    public static IReadOnlyList<PlanResourceReservationSummary> BuildReservationSummary(IndustryPlanComputationContext context)
    {
        var reservationLinks = context.Plan.Links
            .Where(link => link.Kind == PlanLinkKind.Reservation)
            .ToArray();

        var summaries = new List<PlanResourceReservationSummary>();
        AddReservationSummary(context, summaries, "character", reservationLinks, node => node.ResourceProfile.CharacterSlots);
        AddReservationSummary(context, summaries, "blueprint", reservationLinks, node => node.ResourceProfile.BlueprintSlots);
        AddReservationSummary(context, summaries, "bpc", reservationLinks, node => node.ResourceProfile.BpcSlots);
        AddReservationSummary(context, summaries, "job", reservationLinks, node => node.ResourceProfile.JobSlots);

        if (context.InheritedBlueprintQualityNodeIds.Count > 0)
        {
            summaries.Add(new PlanResourceReservationSummary
            {
                ResourceKind = "blueprint_quality",
                DeclaredSlots = 0,
                ReservedNodeCount = context.InheritedBlueprintQualityNodeIds.Count,
                NodeIds = context.InheritedBlueprintQualityNodeIds.OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase).ToArray(),
                Explanation = "These industrial nodes inherited blueprint ME/TE from upstream copy or invention nodes through reservation/dependency links."
            });
        }

        return summaries;
    }

    public static IReadOnlyList<PlanNodeConstraintStatus> BuildNodeConstraintStatuses(IndustryPlanComputationContext context)
    {
        var reservationComponents = BuildReservationComponents(context.Plan);
        var reservationPartnersByNodeId = reservationComponents
            .SelectMany(component => component.Select(nodeId => new
            {
                NodeId = nodeId,
                PartnerIds = component
                    .Where(otherNodeId => !otherNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(otherNodeId => otherNodeId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            }))
            .ToDictionary(item => item.NodeId, item => (IReadOnlyList<string>)item.PartnerIds, StringComparer.OrdinalIgnoreCase);
        var contentionReasonsByNodeId = BuildPotentialContentionReasons(context.Plan, reservationComponents);

        return context.Plan.Nodes
            .Select(node =>
            {
                var dependsOn = context.Plan.Links
                    .Where(link => link.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase))
                    .Select(link => link.FromNodeId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                context.NodeHardConflicts.TryGetValue(node.NodeId, out var hardConflicts);
                context.NodeSoftWarnings.TryGetValue(node.NodeId, out var softWarnings);
                contentionReasonsByNodeId.TryGetValue(node.NodeId, out var contentionReasons);
                context.NodeCandidateCharacters.TryGetValue(node.NodeId, out var candidateCharacters);
                context.SuggestedCharactersByNodeId.TryGetValue(node.NodeId, out var suggestedCharacter);
                context.SuggestedCharacterRationalesByNodeId.TryGetValue(node.NodeId, out var suggestedCharacterRationale);
                var suggestedOperator = suggestedCharacter?.OperatorId is null
                    ? null
                    : context.OperatorsById.GetValueOrDefault(suggestedCharacter.OperatorId);

                var reasons = new List<string>();
                if (hardConflicts is not null)
                {
                    reasons.AddRange(hardConflicts);
                }

                if (softWarnings is not null)
                {
                    reasons.AddRange(softWarnings);
                }

                if (contentionReasons is not null)
                {
                    reasons.AddRange(contentionReasons);
                }

                var statusKind = hardConflicts is { Count: > 0 }
                    ? PlanNodeConstraintStatusKind.Blocked
                    : contentionReasons is { Count: > 0 }
                        ? PlanNodeConstraintStatusKind.PotentialResourceContention
                        : softWarnings is { Count: > 0 }
                            ? PlanNodeConstraintStatusKind.Warning
                            : PlanNodeConstraintStatusKind.Clear;

                return new PlanNodeConstraintStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = statusKind,
                    Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    DependsOnNodeIds = dependsOn,
                    ReservationConnectedNodeIds = reservationPartnersByNodeId.TryGetValue(node.NodeId, out var partnerIds)
                        ? partnerIds
                        : Array.Empty<string>(),
                    CandidateCharacterIds = candidateCharacters?.Select(character => character.CharacterId).ToArray()
                        ?? Array.Empty<string>(),
                    CandidateCharacterNames = candidateCharacters?.Select(character => character.DisplayName).ToArray()
                        ?? Array.Empty<string>(),
                    SuggestedCharacterId = suggestedCharacter?.CharacterId,
                    SuggestedCharacterName = suggestedCharacter?.DisplayName,
                    SuggestedCharacterRationale = suggestedCharacterRationale,
                    SuggestedOperatorId = suggestedOperator?.OperatorId,
                    SuggestedOperatorName = suggestedOperator?.DisplayName
                };
            })
            .ToArray();
    }

    private static void AddReservationSummary(
        IndustryPlanComputationContext context,
        ICollection<PlanResourceReservationSummary> summaries,
        string resourceKind,
        IReadOnlyCollection<PlanLink> reservationLinks,
        Func<PlanNode, int> selector)
    {
        var nodes = context.Plan.Nodes
            .Where(node => selector(node) > 0)
            .ToArray();

        if (nodes.Length == 0)
        {
            return;
        }

        var reservedNodeIds = reservationLinks
            .SelectMany(link => new[] { link.FromNodeId, link.ToNodeId })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(nodeId => nodes.Any(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        summaries.Add(new PlanResourceReservationSummary
        {
            ResourceKind = resourceKind,
            DeclaredSlots = nodes.Sum(selector),
            ReservedNodeCount = reservedNodeIds.Length,
            NodeIds = nodes.Select(node => node.NodeId).ToArray(),
            Explanation = reservedNodeIds.Length == 0
                ? $"Nodes declare {resourceKind} slots, but no explicit reservation edges connect them yet."
                : $"Nodes declare {resourceKind} slots and participate in {reservedNodeIds.Length} reservation-connected node(s)."
        });
    }

    private static IReadOnlyList<HashSet<string>> BuildReservationComponents(IndustryPlan plan)
    {
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in plan.Links.Where(link => link.Kind == PlanLinkKind.Reservation))
        {
            if (!adjacency.TryGetValue(link.FromNodeId, out var fromNeighbors))
            {
                fromNeighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adjacency[link.FromNodeId] = fromNeighbors;
            }

            if (!adjacency.TryGetValue(link.ToNodeId, out var toNeighbors))
            {
                toNeighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adjacency[link.ToNodeId] = toNeighbors;
            }

            fromNeighbors.Add(link.ToNodeId);
            toNeighbors.Add(link.FromNodeId);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<HashSet<string>>();

        foreach (var nodeId in adjacency.Keys)
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static Dictionary<string, List<string>> BuildPotentialContentionReasons(
        IndustryPlan plan,
        IReadOnlyList<HashSet<string>> reservationComponents)
    {
        var nodesById = plan.Nodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);
        var reasonsByNodeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in reservationComponents)
        {
            var componentNodes = component
                .Where(nodesById.ContainsKey)
                .Select(nodeId => nodesById[nodeId])
                .ToArray();

            foreach (var node in componentNodes)
            {
                var sharedResources = componentNodes
                    .Where(otherNode => !otherNode.NodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase))
                    .Select(otherNode => new
                    {
                        Node = otherNode,
                        Shared = GetSharedConstrainedResources(node, otherNode)
                    })
                    .Where(item => item.Shared.Count > 0)
                    .ToArray();

                if (sharedResources.Length == 0)
                {
                    continue;
                }

                var messages = sharedResources
                    .Select(item => $"Potential reservation contention with '{item.Node.Title}' on {FormatSharedResources(item.Shared)}.")
                    .ToList();

                reasonsByNodeId[node.NodeId] = messages;
            }
        }

        return reasonsByNodeId;
    }

    private static IReadOnlyList<string> GetSharedConstrainedResources(PlanNode left, PlanNode right)
    {
        var sharedResources = new List<string>();

        if (left.ResourceProfile.CharacterSlots > 0 && right.ResourceProfile.CharacterSlots > 0)
        {
            sharedResources.Add("character capacity");
        }

        if (left.ResourceProfile.BlueprintSlots > 0 && right.ResourceProfile.BlueprintSlots > 0)
        {
            sharedResources.Add("blueprint access");
        }

        if (left.ResourceProfile.BpcSlots > 0 && right.ResourceProfile.BpcSlots > 0)
        {
            sharedResources.Add("BPC tracking");
        }

        if (left.ResourceProfile.JobSlots > 0 && right.ResourceProfile.JobSlots > 0)
        {
            sharedResources.Add("job slots");
        }

        var leftAdditionalSlots = left.ResourceProfile.AdditionalSlots
            .Where(slot => slot.Exclusive)
            .ToDictionary(CreateAdditionalSlotKey, slot => slot, StringComparer.OrdinalIgnoreCase);
        var rightAdditionalSlots = right.ResourceProfile.AdditionalSlots
            .Where(slot => slot.Exclusive)
            .ToDictionary(CreateAdditionalSlotKey, slot => slot, StringComparer.OrdinalIgnoreCase);

        foreach (var sharedSlotKey in leftAdditionalSlots.Keys.Intersect(rightAdditionalSlots.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var label = !string.IsNullOrWhiteSpace(leftAdditionalSlots[sharedSlotKey].Label)
                ? leftAdditionalSlots[sharedSlotKey].Label
                : rightAdditionalSlots[sharedSlotKey].Label;
            sharedResources.Add($"additional slot '{label}'");
        }

        return sharedResources;
    }

    private static string CreateAdditionalSlotKey(ResourceSlotSpec slot)
    {
        return $"{slot.Kind}:{slot.SlotId.Trim()}";
    }

    private static string FormatSharedResources(IReadOnlyList<string> sharedResources)
    {
        return sharedResources.Count switch
        {
            0 => "reserved resources",
            1 => sharedResources[0],
            2 => $"{sharedResources[0]} and {sharedResources[1]}",
            _ => $"{string.Join(", ", sharedResources.Take(sharedResources.Count - 1))}, and {sharedResources[^1]}"
        };
    }
}
