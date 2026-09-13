using EdenOS.Contracts.Application;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class TaskPoolListPendingUseCase(ITaskPoolService taskPoolService)
    : IQueryUseCase<ListPendingTasksRequest, PendingTaskPoolView>
{
    public Task<UseCaseResult<PendingTaskPoolView>> ExecuteAsync(
        ListPendingTasksRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.ListPending(request));
    }
}

public sealed class TaskPoolCreateFromTransportNodeUseCase(ITaskPoolService taskPoolService)
    : ICommandUseCase<CreateTaskFromTransportNodeRequest, PendingTask>
{
    public Task<UseCaseResult<PendingTask>> ExecuteAsync(
        CreateTaskFromTransportNodeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.CreateFromTransportNode(request));
    }
}

public sealed class TaskPoolCreateFromTradeNeedUseCase(ITaskPoolService taskPoolService)
    : ICommandUseCase<CreateTaskFromTradeNeedRequest, PendingTask>
{
    public Task<UseCaseResult<PendingTask>> ExecuteAsync(
        CreateTaskFromTradeNeedRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.CreateFromTradeNeed(request));
    }
}

public sealed class TaskPoolCreateFromOutsourceNeedUseCase(ITaskPoolService taskPoolService)
    : ICommandUseCase<CreateTaskFromOutsourceNeedRequest, PendingTask>
{
    public Task<UseCaseResult<PendingTask>> ExecuteAsync(
        CreateTaskFromOutsourceNeedRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.CreateFromOutsourceNeed(request));
    }
}

public sealed class TaskPoolUpdatePendingTaskUseCase(ITaskPoolService taskPoolService)
    : ICommandUseCase<UpdatePendingTaskRequest, PendingTask>
{
    public Task<UseCaseResult<PendingTask>> ExecuteAsync(
        UpdatePendingTaskRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.UpdatePendingTask(request));
    }
}

public sealed class TaskPoolMarkReadyForPublishUseCase(ITaskPoolService taskPoolService)
    : ICommandUseCase<MarkReadyForPublishRequest, PendingTask>
{
    public Task<UseCaseResult<PendingTask>> ExecuteAsync(
        MarkReadyForPublishRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.MarkReadyForPublish(request));
    }
}

public sealed class TaskPoolExportManualPublishPayloadUseCase(ITaskPoolService taskPoolService)
    : IQueryUseCase<ExportManualPublishPayloadRequest, ManualPublishExportEnvelope>
{
    public Task<UseCaseResult<ManualPublishExportEnvelope>> ExecuteAsync(
        ExportManualPublishPayloadRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(taskPoolService.ExportManualPublishPayload(request));
    }
}
