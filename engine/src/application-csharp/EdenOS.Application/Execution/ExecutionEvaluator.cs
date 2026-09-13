using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;

namespace EdenOS.Application.Execution;

internal sealed class ExecutionEvaluator
{
    public ExecutionEvaluation Evaluate(
        IndustryPlan plan,
        ExecutionState state,
        PlanComputationResult computation,
        PendingTaskPoolView taskPool,
        int? actionLimit)
    {
        var blockers = ExecutionEventBlockerBuilder.Build(plan, state, computation)
            .Concat(ExecutionResourceContentionAnalyzer.BuildBlockers(plan, state, computation))
            .DistinctBy(blocker => blocker.BlockerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nodeStatuses = ExecutionNodeStatusBuilder.Build(plan, state, computation, taskPool, blockers);
        var nextActions = actionLimit.HasValue
            ? ExecutionNextActionBuilder.Build(plan, blockers, nodeStatuses, actionLimit.Value)
            : Array.Empty<ExecutionNextAction>();

        return new ExecutionEvaluation(blockers, nodeStatuses, nextActions);
    }
}

internal sealed record ExecutionEvaluation(
    IReadOnlyList<ExecutionBlocker> Blockers,
    IReadOnlyList<ExecutionNodeStatus> NodeStatuses,
    IReadOnlyList<ExecutionNextAction> NextActions);
