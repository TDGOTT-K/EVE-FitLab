namespace EdenOS.Contracts.Planning;

public abstract record PlanAnalysisRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public long? MarketLocationId { get; init; }

    public string? MarketAccountKey { get; init; }
}

public sealed record PlanComputeRequest : PlanAnalysisRequest
{
    public int AlternativePathLimit { get; init; } = 3;
}

public sealed record PlanRecomputeRequest : PlanAnalysisRequest
{
    public string? Reason { get; init; }

    public int AlternativePathLimit { get; init; } = 3;
}

public sealed record GetPlanFeasibilityRequest : PlanAnalysisRequest;

public sealed record GetPlanCostBreakdownRequest : PlanAnalysisRequest;

public sealed record GetPlanProfitEstimateRequest : PlanAnalysisRequest;

public sealed record GetPlanTimeEstimateRequest : PlanAnalysisRequest;

public sealed record GetPlanConstraintSummaryRequest : PlanAnalysisRequest;

public sealed record GetPlanAlternativePathsRequest : PlanAnalysisRequest
{
    public int AlternativePathLimit { get; init; } = 3;
}
