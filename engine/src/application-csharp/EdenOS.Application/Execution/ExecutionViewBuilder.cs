using EdenOS.Contracts.Execution;

namespace EdenOS.Application.Execution;

internal static class ExecutionViewBuilder
{
    public static ExecutionNextActionsView BuildNextActionsView(
        ExecutionContextData context,
        ExecutionEvaluation evaluation,
        DateTimeOffset evaluatedAtUtc)
    {
        return new ExecutionNextActionsView
        {
            WorkspaceId = context.WorkspaceId,
            PlanId = context.Plan.PlanId,
            EvaluatedAtUtc = evaluatedAtUtc,
            State = context.State,
            Feasibility = context.Computation.Feasibility,
            NodeStatuses = evaluation.NodeStatuses,
            Blockers = evaluation.Blockers,
            NextActions = evaluation.NextActions,
            Explanations = ExecutionNarrativeBuilder.BuildNextActionExplanations(context)
        };
    }

    public static ExecutionBlockerView BuildBlockerView(
        ExecutionContextData context,
        ExecutionEvaluation evaluation,
        DateTimeOffset evaluatedAtUtc)
    {
        return new ExecutionBlockerView
        {
            WorkspaceId = context.WorkspaceId,
            PlanId = context.Plan.PlanId,
            EvaluatedAtUtc = evaluatedAtUtc,
            State = context.State,
            NodeStatuses = evaluation.NodeStatuses,
            Blockers = evaluation.Blockers,
            CriticalCount = evaluation.Blockers.Count(blocker => blocker.Severity == ExecutionBlockerSeverity.Critical),
            WarningCount = evaluation.Blockers.Count(blocker => blocker.Severity == ExecutionBlockerSeverity.Warning),
            Explanations = ExecutionNarrativeBuilder.BuildBlockerExplanations()
        };
    }

    public static ExecutionReplanResult BuildReplanResult(
        ExecutionContextData context,
        ExecutionEvaluation evaluation,
        string replanReason,
        DateTimeOffset replannedAtUtc)
    {
        return new ExecutionReplanResult
        {
            WorkspaceId = context.WorkspaceId,
            PlanId = context.Plan.PlanId,
            ReplanReason = replanReason,
            ReplannedAtUtc = replannedAtUtc,
            State = context.State,
            Computation = context.Computation,
            NodeStatuses = evaluation.NodeStatuses,
            Blockers = evaluation.Blockers,
            NextActions = evaluation.NextActions,
            EventExplanations = ExecutionNarrativeBuilder.BuildEventExplanations(context.State.Events)
        };
    }
}
