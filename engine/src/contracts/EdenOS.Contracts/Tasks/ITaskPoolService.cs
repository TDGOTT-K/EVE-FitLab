using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Tasks;

public interface ITaskPoolService
{
    UseCaseResult<PendingTaskPoolView> ListPending(ListPendingTasksRequest request);

    UseCaseResult<PendingTask> CreateFromTransportNode(CreateTaskFromTransportNodeRequest request);

    UseCaseResult<PendingTask> CreateFromTradeNeed(CreateTaskFromTradeNeedRequest request);

    UseCaseResult<PendingTask> CreateFromOutsourceNeed(CreateTaskFromOutsourceNeedRequest request);

    UseCaseResult<PendingTask> UpdatePendingTask(UpdatePendingTaskRequest request);

    UseCaseResult<PendingTask> MarkReadyForPublish(MarkReadyForPublishRequest request);

    UseCaseResult<ManualPublishExportEnvelope> ExportManualPublishPayload(ExportManualPublishPayloadRequest request);
}
