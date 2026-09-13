using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketOrdersServiceWorker
{
    public UseCaseResult<MarketOrdersServiceDispatchView> DispatchDueOperations(
        LocalMarketOrdersRuntime runtime,
        MarketOrdersServiceOptions options,
        MarketOrdersServiceCyclePlan plan)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);

        var traceId = $"market.orders.service.dispatch:{Guid.NewGuid():N}";
        var dueOperations = plan.Operations
            .Where(operation => operation.IsDue)
            .ToArray();

        if (dueOperations.Length == 0)
        {
            return UseCaseResult<MarketOrdersServiceDispatchView>.Success(
                new MarketOrdersServiceDispatchView
                {
                    Status = "idle",
                    ExecutedRuns = Array.Empty<MarketOrdersRuntimeRunView>(),
                    Errors = Array.Empty<string>(),
                    Notes = ["No market orders import is due in this cycle."]
                },
                "market orders service found no due operations.",
                traceId);
        }

        var executedRuns = new List<MarketOrdersRuntimeRunView>();
        var errors = new List<string>();
        var notes = new List<string>();
        string status = "completed";
        string summary = $"market orders service executed {dueOperations.Length} due operation(s).";
        UseCaseStatus failureStatus = UseCaseStatus.Error;

        foreach (var operation in dueOperations)
        {
            var runResult = runtime.Import(new MarketOrdersRuntimeRunRequest
            {
                PayloadPath = operation.PayloadPath,
                MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
                RequestedBy = options.RequestedBy,
                Cursor = operation.GeneratedCursor,
                TriggerKind = options.TriggerKind
            });

            if (runResult.Data is not null)
            {
                executedRuns.Add(runResult.Data);
            }

            errors.AddRange(runResult.Errors);
            notes.AddRange(runResult.Warnings);

            if (!runResult.IsSuccess)
            {
                failureStatus = runResult.Status;
                summary = runResult.Summary;
                if (errors.Count == 0)
                {
                    errors.Add(runResult.Summary);
                }

                status = executedRuns.Count > 0
                    ? "completed_with_failures"
                    : "failed";
                break;
            }
        }

        var dispatch = new MarketOrdersServiceDispatchView
        {
            Status = status,
            ExecutedRuns = executedRuns,
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };

        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new UseCaseResult<MarketOrdersServiceDispatchView>
            {
                Status = failureStatus,
                Summary = summary,
                Data = dispatch,
                Errors = dispatch.Errors,
                Warnings = dispatch.Notes,
                TraceId = traceId
            };
        }

        return UseCaseResult<MarketOrdersServiceDispatchView>.Success(
            dispatch,
            summary,
            traceId,
            dispatch.Notes);
    }
}
