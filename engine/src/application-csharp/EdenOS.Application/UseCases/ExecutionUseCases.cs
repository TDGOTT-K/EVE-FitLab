using EdenOS.Contracts.Application;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class ExecutionGetNextActionsUseCase(IExecutionTrackingService executionService)
    : IQueryUseCase<GetNextActionsRequest, ExecutionNextActionsView>
{
    public Task<UseCaseResult<ExecutionNextActionsView>> ExecuteAsync(GetNextActionsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.GetNextActions(request));
    }
}

public sealed class ExecutionMarkStepDoneUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<MarkStepDoneRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(MarkStepDoneRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.MarkStepDone(request));
    }
}

public sealed class ExecutionRecordMaterialArrivalUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordMaterialArrivalRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordMaterialArrivalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordMaterialArrival(request));
    }
}

public sealed class ExecutionRecordMaterialLossUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordMaterialLossRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordMaterialLossRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordMaterialLoss(request));
    }
}

public sealed class ExecutionRecordMarketChangeUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordMarketChangeRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordMarketChangeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordMarketChange(request));
    }
}

public sealed class ExecutionRecordIndustryCostChangeUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordIndustryCostChangeRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordIndustryCostChangeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordIndustryCostChange(request));
    }
}

public sealed class ExecutionRecordLocationChangeUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordLocationChangeRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordLocationChangeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordLocationChange(request));
    }
}

public sealed class ExecutionRecordManualOverrideUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<RecordManualOverrideRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(RecordManualOverrideRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.RecordManualOverride(request));
    }
}

public sealed class ExecutionResolveManualOverrideUseCase(IExecutionTrackingService executionService)
    : ICommandUseCase<ResolveManualOverrideRequest, ExecutionState>
{
    public Task<UseCaseResult<ExecutionState>> ExecuteAsync(ResolveManualOverrideRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.ResolveManualOverride(request));
    }
}

public sealed class ExecutionGetBlockersUseCase(IExecutionTrackingService executionService)
    : IQueryUseCase<GetBlockersRequest, ExecutionBlockerView>
{
    public Task<UseCaseResult<ExecutionBlockerView>> ExecuteAsync(GetBlockersRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.GetBlockers(request));
    }
}

public sealed class ExecutionReplanUseCase(IExecutionTrackingService executionService)
    : IWorkflowUseCase<ExecutionReplanRequest, ExecutionReplanResult>
{
    public Task<UseCaseResult<ExecutionReplanResult>> ExecuteAsync(ExecutionReplanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(executionService.Replan(request));
    }
}
