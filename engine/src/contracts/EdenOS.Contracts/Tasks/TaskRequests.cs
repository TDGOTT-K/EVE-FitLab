using EdenOS.Contracts.Planning;

namespace EdenOS.Contracts.Tasks;

public sealed record ListPendingTasksRequest
{
    public required string WorkspaceId { get; init; }

    public string? PlanId { get; init; }

    public PendingTaskStatus? Status { get; init; }
}

public sealed record CreateTaskFromTransportNodeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }

    public string? TitleOverride { get; init; }

    public string? SummaryOverride { get; init; }

    public string? Notes { get; init; }

    public string? CargoSummary { get; init; }

    public decimal? VolumeCubicMeters { get; init; }

    public decimal? RewardHintIsk { get; init; }

    public decimal? CollateralHintIsk { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record TradeNeedDefinition
{
    public required PlanTradeMode TradeMode { get; init; }

    public required string TargetTypeId { get; init; }

    public required string TargetName { get; init; }

    public required decimal Quantity { get; init; }

    public string? MarketScope { get; init; }

    public decimal? UnitPricePreference { get; init; }

    public bool UsesMarketFacts { get; init; } = true;

    public bool AllowSplitFulfillment { get; init; } = true;
}

public sealed record CreateTaskFromTradeNeedRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }

    public required TradeNeedDefinition Need { get; init; }

    public string? TitleOverride { get; init; }

    public string? SummaryOverride { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record OutsourceNeedDefinition
{
    public required string ActivityLabel { get; init; }

    public required string TargetTypeId { get; init; }

    public required string TargetName { get; init; }

    public required decimal Quantity { get; init; }

    public string? DeliveryLocation { get; init; }

    public bool MaterialsProvidedByRequester { get; init; }

    public bool BlueprintProvidedByRequester { get; init; }

    public decimal? QuoteHintIsk { get; init; }

    public DateTimeOffset? DueByUtc { get; init; }
}

public sealed record CreateTaskFromOutsourceNeedRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }

    public required OutsourceNeedDefinition Need { get; init; }

    public string? TitleOverride { get; init; }

    public string? SummaryOverride { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record UpdatePendingTaskRequest
{
    public required string WorkspaceId { get; init; }

    public required string TaskId { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? Notes { get; init; }

    public PendingTaskDetails? Details { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record MarkReadyForPublishRequest
{
    public required string WorkspaceId { get; init; }

    public required string TaskId { get; init; }
}

public sealed record ExportManualPublishPayloadRequest
{
    public required string WorkspaceId { get; init; }

    public string? PlanId { get; init; }

    public IReadOnlyList<string> TaskIds { get; init; } = Array.Empty<string>();
}
