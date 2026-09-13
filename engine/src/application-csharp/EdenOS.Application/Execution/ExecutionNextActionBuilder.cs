using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Execution;

internal static class ExecutionNextActionBuilder
{
    public static IReadOnlyList<ExecutionNextAction> Build(
        IndustryPlan plan,
        IReadOnlyList<ExecutionBlocker> blockers,
        IReadOnlyList<ExecutionNodeStatus> nodeStatuses,
        int actionLimit)
    {
        var actions = new List<ExecutionNextAction>();
        var criticalBlockers = blockers.Where(blocker => blocker.Severity == ExecutionBlockerSeverity.Critical).ToArray();
        var nodeStatusesByNodeId = nodeStatuses.ToDictionary(status => status.NodeId, StringComparer.OrdinalIgnoreCase);

        actions.AddRange(criticalBlockers
            .Where(blocker => NormalizeOptional(blocker.NodeId) is null)
            .Select(blocker => new ExecutionNextAction
            {
                ActionId = $"action-{blocker.BlockerId}",
                Kind = ExecutionActionKind.ResolveBlocker,
                Title = blocker.Title,
                WhyThisAction = blocker.Summary,
                NodeId = blocker.NodeId,
                RelatedBlockerIds = [blocker.BlockerId],
                RelatedEventIds = blocker.RelatedEventIds
            }));

        foreach (var node in plan.Nodes)
        {
            if (!nodeStatusesByNodeId.TryGetValue(node.NodeId, out var nodeStatus) ||
                nodeStatus.StatusKind == ExecutionNodeStatusKind.Completed)
            {
                continue;
            }

            switch (nodeStatus.StatusKind)
            {
                case ExecutionNodeStatusKind.BlockedByConstraint:
                case ExecutionNodeStatusKind.BlockedByMaterial:
                case ExecutionNodeStatusKind.BlockedByOverride:
                case ExecutionNodeStatusKind.BlockedByResource:
                    actions.Add(new ExecutionNextAction
                    {
                        ActionId = $"action-node-blocker-{node.NodeId}",
                        Kind = ExecutionActionKind.ResolveBlocker,
                        Title = $"Resolve blocker for '{node.Title}'",
                        WhyThisAction = nodeStatus.Summary,
                        NodeId = node.NodeId,
                        RelatedBlockerIds = nodeStatus.RelatedBlockerIds,
                        RelatedEventIds = nodeStatus.RelatedEventIds,
                        DependsOnNodeIds = nodeStatus.DependsOnNodeIds
                    });
                    break;

                case ExecutionNodeStatusKind.WaitingOnTaskReview:
                    actions.Add(new ExecutionNextAction
                    {
                        ActionId = $"action-task-{nodeStatus.TaskId}",
                        Kind = ExecutionActionKind.ReviewPendingTask,
                        Title = $"Review task for '{node.Title}'",
                        WhyThisAction = nodeStatus.Summary,
                        NodeId = node.NodeId,
                        TaskId = nodeStatus.TaskId,
                        TaskKind = nodeStatus.TaskKind,
                        DependsOnNodeIds = nodeStatus.DependsOnNodeIds
                    });
                    break;

                case ExecutionNodeStatusKind.ReadyForTaskCreation:
                    actions.Add(new ExecutionNextAction
                    {
                        ActionId = $"action-create-task-{node.NodeId}",
                        Kind = ExecutionActionKind.CreatePendingTask,
                        Title = $"Create task-pool entry for '{node.Title}'",
                        WhyThisAction = nodeStatus.Summary,
                        NodeId = node.NodeId,
                        TaskKind = nodeStatus.TaskKind,
                        DependsOnNodeIds = nodeStatus.DependsOnNodeIds
                    });
                    break;

                case ExecutionNodeStatusKind.ReadyToExecute:
                    actions.Add(new ExecutionNextAction
                    {
                        ActionId = $"action-execute-{node.NodeId}",
                        Kind = ExecutionActionKind.ExecuteNode,
                        Title = $"Execute '{node.Title}'",
                        WhyThisAction = nodeStatus.Summary,
                        NodeId = node.NodeId,
                        DependsOnNodeIds = nodeStatus.DependsOnNodeIds
                    });
                    break;
            }
        }

        if (blockers.Any(blocker => blocker.Kind is ExecutionBlockerKind.MarketShift or ExecutionBlockerKind.IndustryCostShift or ExecutionBlockerKind.LocationDrift or ExecutionBlockerKind.ManualOverride))
        {
            actions.Add(new ExecutionNextAction
            {
                ActionId = "action-replan-review",
                Kind = ExecutionActionKind.ReplanReview,
                Title = "Review execution.replan output",
                WhyThisAction = "Recent execution events changed assumptions enough that a replanning pass should be reviewed before continuing."
            });
        }

        return actions
            .DistinctBy(action => action.ActionId, StringComparer.OrdinalIgnoreCase)
            .Take(actionLimit)
            .ToArray();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
