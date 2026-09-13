using EdenOS.Contracts.Application;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class PlanCreateUseCase(IIndustryPlanService planService)
    : ICommandUseCase<CreatePlanRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.Create(request));
    }
}

public sealed class PlanGetUseCase(IIndustryPlanService planService)
    : IQueryUseCase<GetPlanRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(GetPlanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.Get(request));
    }
}

public sealed class PlanListUseCase(IIndustryPlanService planService)
    : IQueryUseCase<ListPlansRequest, IReadOnlyList<PlanSummary>>
{
    public Task<UseCaseResult<IReadOnlyList<PlanSummary>>> ExecuteAsync(ListPlansRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.List(request));
    }
}

public sealed class PlanSetGoalUseCase(IIndustryPlanService planService)
    : ICommandUseCase<SetPlanGoalRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(SetPlanGoalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.SetGoal(request));
    }
}

public sealed class PlanSetPreferencesUseCase(IIndustryPlanService planService)
    : ICommandUseCase<SetPlanPreferencesRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(SetPlanPreferencesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.SetPreferences(request));
    }
}

public sealed class PlanAddProductionNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddProductionNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddProductionNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddProductionNode(request));
    }
}

public sealed class PlanAddReactionNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddReactionNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddReactionNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddReactionNode(request));
    }
}

public sealed class PlanAddCopyOrInventionNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddCopyOrInventionNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddCopyOrInventionNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddCopyOrInventionNode(request));
    }
}

public sealed class PlanAddInventoryPoolUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddInventoryPoolNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddInventoryPoolNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddInventoryPool(request));
    }
}

public sealed class PlanAddTransportNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddTransportNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddTransportNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddTransportNode(request));
    }
}

public sealed class PlanAddTradeNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<AddTradeNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(AddTradeNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.AddTradeNode(request));
    }
}

public sealed class PlanLinkNodesUseCase(IIndustryPlanService planService)
    : ICommandUseCase<LinkPlanNodesRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(LinkPlanNodesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.LinkNodes(request));
    }
}

public sealed class PlanUnlinkNodesUseCase(IIndustryPlanService planService)
    : ICommandUseCase<UnlinkPlanNodesRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(UnlinkPlanNodesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.UnlinkNodes(request));
    }
}

public sealed class PlanUpdateNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<UpdatePlanNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(UpdatePlanNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.UpdateNode(request));
    }
}

public sealed class PlanRemoveNodeUseCase(IIndustryPlanService planService)
    : ICommandUseCase<RemovePlanNodeRequest, IndustryPlan>
{
    public Task<UseCaseResult<IndustryPlan>> ExecuteAsync(RemovePlanNodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(planService.RemoveNode(request));
    }
}
