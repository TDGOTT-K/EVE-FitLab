using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Planning;

public interface IIndustryPlanService
{
    UseCaseResult<IndustryPlan> Create(CreatePlanRequest request);

    UseCaseResult<IndustryPlan> Get(GetPlanRequest request);

    UseCaseResult<IReadOnlyList<PlanSummary>> List(ListPlansRequest request);

    UseCaseResult<IndustryPlan> SetGoal(SetPlanGoalRequest request);

    UseCaseResult<IndustryPlan> SetPreferences(SetPlanPreferencesRequest request);

    UseCaseResult<IndustryPlan> AddProductionNode(AddProductionNodeRequest request);

    UseCaseResult<IndustryPlan> AddReactionNode(AddReactionNodeRequest request);

    UseCaseResult<IndustryPlan> AddCopyOrInventionNode(AddCopyOrInventionNodeRequest request);

    UseCaseResult<IndustryPlan> AddInventoryPool(AddInventoryPoolNodeRequest request);

    UseCaseResult<IndustryPlan> AddTransportNode(AddTransportNodeRequest request);

    UseCaseResult<IndustryPlan> AddTradeNode(AddTradeNodeRequest request);

    UseCaseResult<IndustryPlan> LinkNodes(LinkPlanNodesRequest request);

    UseCaseResult<IndustryPlan> UnlinkNodes(UnlinkPlanNodesRequest request);

    UseCaseResult<IndustryPlan> UpdateNode(UpdatePlanNodeRequest request);

    UseCaseResult<IndustryPlan> RemoveNode(RemovePlanNodeRequest request);
}
