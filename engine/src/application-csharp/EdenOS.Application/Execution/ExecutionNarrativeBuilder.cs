using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Execution;

internal static class ExecutionNarrativeBuilder
{
    public static IReadOnlyList<string> BuildNextActionExplanations(ExecutionContextData context)
    {
        var explanations = new List<string>
        {
            "Next actions are derived from plan dependencies, current execution state, current blocker set, and task-pool linkage.",
            "Execution continues to consume plan.compute outputs instead of recomputing planning logic locally.",
            "Each plan node now has a unified execution status so blockers, readiness, and task-pool handoff use the same state model."
        };

        if (context.State.CompletedNodeIds.Count > 0)
        {
            explanations.Add($"Execution already marked {context.State.CompletedNodeIds.Count} node(s) done.");
        }

        return explanations
            .Concat(context.Computation.Explanations.Take(2))
            .ToArray();
    }

    public static IReadOnlyList<string> BuildBlockerExplanations()
    {
        return
        [
            "Blockers combine plan.compute conflicts, unresolved execution events, and exposed fact uncertainty.",
            "Placeholder market dependencies remain visible as warnings instead of being silently ignored."
        ];
    }

    public static IReadOnlyList<string> BuildEventExplanations(IReadOnlyList<ExecutionEventRecord> events)
    {
        if (events.Count == 0)
        {
            return ["No execution events were recorded, so replan output only reflects the current plan model."];
        }

        return events
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(5)
            .Select(evt => $"{evt.Kind}: {evt.Summary}")
            .ToArray();
    }

    public static string BuildReplanReason(IReadOnlyList<ExecutionEventRecord> events, string? requestReason)
    {
        var explicitReason = NormalizeOptional(requestReason);
        if (explicitReason is not null)
        {
            return explicitReason;
        }

        if (events.Count == 0)
        {
            return "execution context requested replanning without additional event history";
        }

        return string.Join("; ", events
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(3)
            .Select(evt => evt.Summary));
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
