using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Planning;

public interface IIndustryPlanComputationService
{
    UseCaseResult<PlanComputationResult> Compute(PlanComputeRequest request);

    UseCaseResult<PlanComputationResult> Recompute(PlanRecomputeRequest request);

    UseCaseResult<PlanFeasibilitySummary> GetFeasibility(GetPlanFeasibilityRequest request);

    UseCaseResult<PlanCostBreakdown> GetCostBreakdown(GetPlanCostBreakdownRequest request);

    UseCaseResult<PlanProfitEstimate> GetProfitEstimate(GetPlanProfitEstimateRequest request);

    UseCaseResult<PlanTimeEstimate> GetTimeEstimate(GetPlanTimeEstimateRequest request);

    UseCaseResult<PlanConstraintSummary> GetConstraintSummary(GetPlanConstraintSummaryRequest request);

    UseCaseResult<IReadOnlyList<PlanAlternativePath>> GetAlternativePaths(GetPlanAlternativePathsRequest request);
}
