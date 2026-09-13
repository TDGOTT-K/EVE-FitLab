namespace EdenOS.Contracts.Execution;

public sealed record GetNextActionsRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public long? MarketLocationId { get; init; }

    public string? MarketAccountKey { get; init; }

    public int ActionLimit { get; init; } = 5;
}

public sealed record MarkStepDoneRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordMaterialArrivalRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? NodeId { get; init; }

    public required string ResourceTypeId { get; init; }

    public required string ResourceName { get; init; }

    public required decimal Quantity { get; init; }

    public string? LocationLabel { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordMaterialLossRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? NodeId { get; init; }

    public required string ResourceTypeId { get; init; }

    public required string ResourceName { get; init; }

    public required decimal Quantity { get; init; }

    public string? LocationLabel { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordMarketChangeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? ResourceTypeId { get; init; }

    public string? ResourceName { get; init; }

    public long? MarketLocationId { get; init; }

    public required string ChangeSummary { get; init; }

    public string? ImpactHint { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordIndustryCostChangeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? NodeId { get; init; }

    public string? LocationLabel { get; init; }

    public decimal? PreviousSystemCostIndex { get; init; }

    public required decimal CurrentSystemCostIndex { get; init; }

    public required string ChangeSummary { get; init; }

    public string? ImpactHint { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordLocationChangeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? NodeId { get; init; }

    public required string CurrentLocationLabel { get; init; }

    public required string ChangeSummary { get; init; }

    public bool IsRecovered { get; init; }

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record RecordManualOverrideRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string OverrideSummary { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> AffectedNodeIds { get; init; } = Array.Empty<string>();

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record ResolveManualOverrideRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string ResolutionSummary { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> AffectedNodeIds { get; init; } = Array.Empty<string>();

    public DateTimeOffset? OccurredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record GetBlockersRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public long? MarketLocationId { get; init; }

    public string? MarketAccountKey { get; init; }
}

public sealed record ExecutionReplanRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public long? MarketLocationId { get; init; }

    public string? MarketAccountKey { get; init; }

    public string? Reason { get; init; }

    public int AlternativePathLimit { get; init; } = 3;

    public int ActionLimit { get; init; } = 5;
}
