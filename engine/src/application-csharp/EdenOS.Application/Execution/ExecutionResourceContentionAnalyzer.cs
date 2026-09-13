using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Execution;

internal static class ExecutionResourceContentionAnalyzer
{
    public static IReadOnlyList<ExecutionBlocker> BuildBlockers(
        IndustryPlan plan,
        ExecutionState state,
        PlanComputationResult computation)
    {
        var reservationComponents = BuildReservationComponents(plan);
        if (reservationComponents.Count == 0)
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var readyNodes = GetDependencyReadyNodes(plan, state);
        if (readyNodes.Count <= 1)
        {
            return Array.Empty<ExecutionBlocker>();
        }

        var planOrder = plan.Nodes
            .Select((node, index) => new { node.NodeId, Index = index })
            .ToDictionary(item => item.NodeId, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var blockers = new List<ExecutionBlocker>();

        foreach (var component in reservationComponents)
        {
            var candidateNodes = readyNodes
                .Where(node => component.Contains(node.NodeId))
                .OrderByDescending(node => computation.TimeEstimate.CriticalNodeIds.Contains(node.NodeId, StringComparer.OrdinalIgnoreCase))
                .ThenBy(node => planOrder[node.NodeId])
                .ToArray();

            if (candidateNodes.Length <= 1)
            {
                continue;
            }

            var selectedNodes = new List<PlanNode>();

            foreach (var candidateNode in candidateNodes)
            {
                var contention = selectedNodes
                    .Select(selectedNode => new
                    {
                        Node = selectedNode,
                        SharedResources = GetSharedConstrainedResources(selectedNode, candidateNode)
                    })
                    .FirstOrDefault(item => item.SharedResources.Count > 0);

                if (contention is null)
                {
                    selectedNodes.Add(candidateNode);
                    continue;
                }

                blockers.Add(new ExecutionBlocker
                {
                    BlockerId = $"resource-contention-{candidateNode.NodeId}",
                    Kind = ExecutionBlockerKind.ResourceContention,
                    Severity = ExecutionBlockerSeverity.Critical,
                    Title = $"Reserved resource contention blocks '{candidateNode.Title}'",
                    Summary = $"'{candidateNode.Title}' currently contends with '{contention.Node.Title}' for {FormatSharedResources(contention.SharedResources)}.",
                    NodeId = candidateNode.NodeId,
                    SuggestedResolution = $"Finish '{contention.Node.Title}' first, or change the reservation layout if both nodes should run in parallel."
                });
            }
        }

        return blockers;
    }

    private static IReadOnlyList<PlanNode> GetDependencyReadyNodes(IndustryPlan plan, ExecutionState state)
    {
        return plan.Nodes
            .Where(node => !state.CompletedNodeIds.Contains(node.NodeId))
            .Where(node => plan.Links
                .Where(link => link.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase))
                .Select(link => link.FromNodeId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .All(parentNodeId => state.CompletedNodeIds.Contains(parentNodeId)))
            .ToArray();
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

        if (adjacency.Count == 0)
        {
            return Array.Empty<HashSet<string>>();
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
            var leftSlot = leftAdditionalSlots[sharedSlotKey];
            var rightSlot = rightAdditionalSlots[sharedSlotKey];
            var slotLabel = !string.IsNullOrWhiteSpace(leftSlot.Label)
                ? leftSlot.Label
                : rightSlot.Label;
            sharedResources.Add($"additional slot '{slotLabel}'");
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
