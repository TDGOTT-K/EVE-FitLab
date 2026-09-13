using EdenOS.Contracts.Tasks;

namespace EdenOS.Application.Tasks;

internal interface ITaskPoolExecutionCoordinator
{
    IReadOnlyList<PendingTask> ReturnReadyTasksToPending(
        string workspaceId,
        string planId,
        IReadOnlyCollection<string> blockedNodeIds,
        string reason);
}
