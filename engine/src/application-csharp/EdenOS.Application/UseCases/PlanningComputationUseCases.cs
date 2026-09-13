using EdenOS.Contracts.Application;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class PlanComputeUseCase(IIndustryPlanComputationService computationService)
    : IWorkflowUseCase<PlanComputeRequest, PlanComputationResult>
{
    public Task<UseCaseResult<PlanComputationResult>> ExecuteAsync(PlanComputeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.Compute(request));
    }
}

public sealed class PlanRecomputeUseCase(IIndustryPlanComputationService computationService)
    : IWorkflowUseCase<PlanRecomputeRequest, PlanComputationResult>
{
    public Task<UseCaseResult<PlanComputationResult>> ExecuteAsync(PlanRecomputeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.Recompute(request));
    }
}

public sealed class PlanGetFeasibilityUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanFeasibilityRequest, PlanFeasibilitySummary>
{
    public Task<UseCaseResult<PlanFeasibilitySummary>> ExecuteAsync(GetPlanFeasibilityRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetFeasibility(request));
    }
}

public sealed class PlanGetCostBreakdownUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanCostBreakdownRequest, PlanCostBreakdown>
{
    public Task<UseCaseResult<PlanCostBreakdown>> ExecuteAsync(GetPlanCostBreakdownRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetCostBreakdown(request));
    }
}

public sealed class PlanGetProfitEstimateUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanProfitEstimateRequest, PlanProfitEstimate>
{
    public Task<UseCaseResult<PlanProfitEstimate>> ExecuteAsync(GetPlanProfitEstimateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetProfitEstimate(request));
    }
}

public sealed class PlanGetTimeEstimateUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanTimeEstimateRequest, PlanTimeEstimate>
{
    public Task<UseCaseResult<PlanTimeEstimate>> ExecuteAsync(GetPlanTimeEstimateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetTimeEstimate(request));
    }
}

public sealed class PlanGetConstraintSummaryUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanConstraintSummaryRequest, PlanConstraintSummary>
{
    public Task<UseCaseResult<PlanConstraintSummary>> ExecuteAsync(GetPlanConstraintSummaryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetConstraintSummary(request));
    }
}

public sealed class PlanGetAlternativePathsUseCase(IIndustryPlanComputationService computationService)
    : IQueryUseCase<GetPlanAlternativePathsRequest, IReadOnlyList<PlanAlternativePath>>
{
    public Task<UseCaseResult<IReadOnlyList<PlanAlternativePath>>> ExecuteAsync(GetPlanAlternativePathsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(computationService.GetAlternativePaths(request));
    }
}
