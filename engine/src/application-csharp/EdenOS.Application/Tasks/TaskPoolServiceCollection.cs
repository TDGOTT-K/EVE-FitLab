using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Tasks;

public static class TaskPoolServiceCollection
{
    public static ITaskPoolService CreateInMemory(
        IIndustryPlanService planService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider) =>
        new InMemoryTaskPoolService(planService, workspaceService, timeProvider);

    public static ITaskPoolService CreatePersistent(
        string rootPath,
        IIndustryPlanService planService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider) =>
        new InMemoryTaskPoolService(
            planService,
            workspaceService,
            timeProvider,
            new LocalJsonStateStore<PendingTaskRuntimeState>(
                RuntimeStatePaths.GetTaskStatePath(rootPath),
                () => new PendingTaskRuntimeState()));
}
