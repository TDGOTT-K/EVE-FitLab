using EdenOS.Application.RuntimeState;
using EdenOS.Application.Accounts;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Planning;

public static class IndustryPlanServiceCollection
{
    public static IIndustryPlanService CreateInMemory() =>
        new InMemoryIndustryPlanService(new InMemoryAccountCharacterPoolService());

    public static IIndustryPlanService CreateInMemory(IWorkspaceService workspaceService) =>
        new InMemoryIndustryPlanService(workspaceService);

    public static IIndustryPlanService CreatePersistent(string rootPath) =>
        new InMemoryIndustryPlanService(
            new InMemoryAccountCharacterPoolService(),
            new LocalJsonStateStore<IndustryPlanRuntimeState>(
                RuntimeStatePaths.GetPlanStatePath(rootPath),
                () => new IndustryPlanRuntimeState()));

    public static IIndustryPlanService CreatePersistent(string rootPath, IWorkspaceService workspaceService) =>
        new InMemoryIndustryPlanService(
            workspaceService,
            new LocalJsonStateStore<IndustryPlanRuntimeState>(
                RuntimeStatePaths.GetPlanStatePath(rootPath),
                () => new IndustryPlanRuntimeState()));
}
