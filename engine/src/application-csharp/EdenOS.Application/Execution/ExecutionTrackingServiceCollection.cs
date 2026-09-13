using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Execution;

public static class ExecutionTrackingServiceCollection
{
    public static IExecutionTrackingService CreateInMemory(
        IIndustryPlanService planService,
        IIndustryPlanComputationService computationService,
        ITaskPoolService taskPoolService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider) =>
        new InMemoryExecutionTrackingService(planService, computationService, taskPoolService, workspaceService, timeProvider);

    public static IExecutionTrackingService CreatePersistent(
        string rootPath,
        IIndustryPlanService planService,
        IIndustryPlanComputationService computationService,
        ITaskPoolService taskPoolService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider) =>
        new InMemoryExecutionTrackingService(
            planService,
            computationService,
            taskPoolService,
            workspaceService,
            timeProvider,
            new LocalJsonStateStore<ExecutionRuntimeState>(
                RuntimeStatePaths.GetExecutionStatePath(rootPath),
                () => new ExecutionRuntimeState()));
}
