using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;

namespace EdenOS.Application.Execution;

internal static class ExecutionNodeStatusBuilder
{
    public static IReadOnlyList<ExecutionNodeStatus> Build(
        IndustryPlan plan,
        ExecutionState state,
        PlanComputationResult computation,
        PendingTaskPoolView taskPool,
        IReadOnlyList<ExecutionBlocker> blockers)
    {
        var planConstraintStatusesByNodeId = computation.ConstraintSummary.NodeStatuses
            .ToDictionary(status => status.NodeId, StringComparer.OrdinalIgnoreCase);
        var blockersByNodeId = blockers
            .Where(blocker => NormalizeOptional(blocker.NodeId) is not null)
            .GroupBy(blocker => blocker.NodeId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ExecutionBlocker>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var tasksByNodeId = taskPool.Tasks
            .GroupBy(task => task.Origin.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var statuses = new List<ExecutionNodeStatus>(plan.Nodes.Count);

        foreach (var node in plan.Nodes)
        {
            var dependsOn = GetDependsOnNodeIds(plan, node.NodeId);
            var unmetDependencies = dependsOn
                .Where(parentNodeId => !state.CompletedNodeIds.Contains(parentNodeId))
                .ToArray();
            planConstraintStatusesByNodeId.TryGetValue(node.NodeId, out var planConstraintStatus);

            if (state.CompletedNodeIds.Contains(node.NodeId))
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = ExecutionNodeStatusKind.Completed,
                    Summary = $"'{node.Title}' is already marked completed.",
                    DependsOnNodeIds = dependsOn
                });
                continue;
            }

            if (blockersByNodeId.TryGetValue(node.NodeId, out var nodeBlockers) &&
                nodeBlockers.Count > 0)
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = DetermineNodeBlockedStatusKind(nodeBlockers),
                    Summary = string.Join(" ", nodeBlockers.Select(blocker => blocker.Summary)),
                    DependsOnNodeIds = dependsOn,
                    UnmetDependencyNodeIds = unmetDependencies,
                    RelatedBlockerIds = nodeBlockers.Select(blocker => blocker.BlockerId).ToArray(),
                    RelatedEventIds = nodeBlockers.SelectMany(blocker => blocker.RelatedEventIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                });
                continue;
            }

            if (planConstraintStatus?.StatusKind == PlanNodeConstraintStatusKind.Blocked)
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = ExecutionNodeStatusKind.BlockedByConstraint,
                    Summary = BuildPlanConstraintSummary(planConstraintStatus),
                    DependsOnNodeIds = dependsOn,
                    UnmetDependencyNodeIds = unmetDependencies
                });
                continue;
            }

            if (unmetDependencies.Length > 0)
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = ExecutionNodeStatusKind.WaitingOnDependency,
                    Summary = AppendPlanningConstraintHint(
                        $"Waiting for {unmetDependencies.Length} upstream node(s) to complete before '{node.Title}' can start.",
                        planConstraintStatus),
                    DependsOnNodeIds = dependsOn,
                    UnmetDependencyNodeIds = unmetDependencies
                });
                continue;
            }

            if (tasksByNodeId.TryGetValue(node.NodeId, out var relatedTask))
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = ExecutionNodeStatusKind.WaitingOnTaskReview,
                    Summary = AppendPlanningConstraintHint(relatedTask.Status == PendingTaskStatus.Pending
                        ? "The node is ready, and there is already a pending task in the internal publish pool."
                        : "The node is ready, and its task is already marked ready for publish.",
                        planConstraintStatus),
                    DependsOnNodeIds = dependsOn,
                    TaskId = relatedTask.TaskId,
                    TaskKind = relatedTask.Kind
                });
                continue;
            }

            var taskKind = GetEligibleTaskKind(node);
            if (taskKind.HasValue)
            {
                statuses.Add(new ExecutionNodeStatus
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    NodeKind = node.Kind,
                    StatusKind = ExecutionNodeStatusKind.ReadyForTaskCreation,
                    Summary = AppendPlanningConstraintHint(
                        "This node is dependency-ready and should move into the internal task pool next.",
                        planConstraintStatus),
                    DependsOnNodeIds = dependsOn,
                    TaskKind = taskKind
                });
                continue;
            }

            statuses.Add(new ExecutionNodeStatus
            {
                NodeId = node.NodeId,
                Title = node.Title,
                NodeKind = node.Kind,
                StatusKind = ExecutionNodeStatusKind.ReadyToExecute,
                Summary = AppendPlanningConstraintHint(
                    computation.TimeEstimate.CriticalNodeIds.Contains(node.NodeId, StringComparer.OrdinalIgnoreCase)
                        ? "This node is ready and sits on the current critical path."
                        : "This node is dependency-ready under the current execution state.",
                    planConstraintStatus),
                DependsOnNodeIds = dependsOn
            });
        }

        return statuses;
    }

    private static string[] GetDependsOnNodeIds(IndustryPlan plan, string nodeId)
    {
        return plan.Links
            .Where(link => link.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(link => link.FromNodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ExecutionNodeStatusKind DetermineNodeBlockedStatusKind(IReadOnlyList<ExecutionBlocker> blockers)
    {
        if (blockers.Any(blocker => blocker.Kind == ExecutionBlockerKind.MaterialShortage))
        {
            return ExecutionNodeStatusKind.BlockedByMaterial;
        }

        if (blockers.Any(blocker => blocker.Kind == ExecutionBlockerKind.ResourceContention))
        {
            return ExecutionNodeStatusKind.BlockedByResource;
        }

        if (blockers.Any(blocker => blocker.Kind == ExecutionBlockerKind.ManualOverride))
        {
            return ExecutionNodeStatusKind.BlockedByOverride;
        }

        return ExecutionNodeStatusKind.BlockedByConstraint;
    }

    private static string BuildPlanConstraintSummary(PlanNodeConstraintStatus status)
    {
        if (status.Reasons.Count == 0)
        {
            return $"Plan computation marked '{status.Title}' as blocked.";
        }

        return string.Join(" ", status.Reasons);
    }

    private static string AppendPlanningConstraintHint(string baseSummary, PlanNodeConstraintStatus? planConstraintStatus)
    {
        if (planConstraintStatus is null)
        {
            return baseSummary;
        }

        return planConstraintStatus.StatusKind switch
        {
            PlanNodeConstraintStatusKind.Warning when planConstraintStatus.Reasons.Count > 0 =>
                $"{baseSummary} Planning warning: {string.Join(" ", planConstraintStatus.Reasons)}",
            PlanNodeConstraintStatusKind.PotentialResourceContention when planConstraintStatus.Reasons.Count > 0 =>
                $"{baseSummary} Planning hint: {string.Join(" ", planConstraintStatus.Reasons)}",
            _ => baseSummary
        };
    }

    private static PendingTaskKind? GetEligibleTaskKind(PlanNode node)
    {
        return node.Details switch
        {
            TransportNodeDetails => PendingTaskKind.Transport,
            TradeNodeDetails trade when trade.TradeMode != PlanTradeMode.OutsourceInput => PendingTaskKind.Trade,
            TradeNodeDetails trade when trade.TradeMode == PlanTradeMode.OutsourceInput => PendingTaskKind.Outsource,
            ProductionNodeDetails => PendingTaskKind.Outsource,
            ReactionNodeDetails => PendingTaskKind.Outsource,
            CopyOrInventionNodeDetails => PendingTaskKind.Outsource,
            _ => null
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
}
