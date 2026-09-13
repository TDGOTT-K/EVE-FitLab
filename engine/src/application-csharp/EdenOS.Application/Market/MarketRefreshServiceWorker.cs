using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketRefreshServiceWorker
{
    public UseCaseResult<MarketRefreshServiceDispatchView> DispatchDueOperations(
        LocalMarketRefreshRuntime runtime,
        MarketRefreshServiceOptions options,
        MarketRefreshServiceCyclePlan plan)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);

        var traceId = $"market.refresh.service.dispatch:{Guid.NewGuid():N}";
        var dueOperations = plan.Operations
            .Where(operation => operation.IsDue)
            .ToArray();

        if (dueOperations.Length == 0)
        {
            return UseCaseResult<MarketRefreshServiceDispatchView>.Success(
                new MarketRefreshServiceDispatchView
                {
                    Status = "idle",
                    ExecutedRuns = Array.Empty<MarketRefreshRunView>(),
                    Errors = Array.Empty<string>(),
                    Notes = ["No market refresh operation is due in this cycle."]
                },
                "market refresh service found no due operations.",
                traceId);
        }

        var batchRequest = new MarketRefreshBatchRequest
        {
            BatchName = $"{options.ServiceName}-{plan.CycleId}",
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RequestedBy = options.RequestedBy,
            ContinueOnError = options.ContinueOnError,
            TriggerKind = options.TriggerKind,
            Operations = dueOperations
                .Select(operation => new MarketRefreshBatchOperationRequest
                {
                    Operation = operation.Operation,
                    PayloadPath = operation.PayloadPath,
                    Cursor = operation.GeneratedCursor
                })
                .ToArray()
        };

        var batchResult = runtime.RunBatch(batchRequest);
        var batch = batchResult.Data;
        var dispatch = new MarketRefreshServiceDispatchView
        {
            Status = batch is null
                ? "failed"
                : batchResult.IsSuccess
                    ? "completed"
                    : batch.Status,
            Batch = batch,
            ExecutedRuns = batch?.Runs ?? Array.Empty<MarketRefreshRunView>(),
            Errors = batchResult.Errors,
            Notes = batchResult.Warnings
        };

        if (!batchResult.IsSuccess)
        {
            return new UseCaseResult<MarketRefreshServiceDispatchView>
            {
                Status = batchResult.Status,
                Summary = batchResult.Summary,
                Data = dispatch,
                Errors = batchResult.Errors,
                Warnings = batchResult.Warnings,
                TraceId = traceId
            };
        }

        return UseCaseResult<MarketRefreshServiceDispatchView>.Success(
            dispatch,
            batchResult.Summary,
            traceId,
            batchResult.Warnings);
    }
}
