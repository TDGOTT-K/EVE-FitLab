using System.Text.Json.Serialization;
using EdenOS.Contracts.Planning;

namespace EdenOS.Contracts.Tasks;

public enum PendingTaskStatus
{
    Pending,
    ReadyForPublish
}

public enum PendingTaskKind
{
    Transport,
    Trade,
    Outsource
}

public enum TaskSourceKind
{
    TransportNode,
    TradeNeed,
    OutsourceNeed
}

public sealed record TaskOriginReference
{
    public required TaskSourceKind SourceKind { get; init; }

    public required string PlanId { get; init; }

    public required string PlanName { get; init; }

    public required string NodeId { get; init; }

    public required PlanNodeKind NodeKind { get; init; }

    public required string NodeTitle { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TransportPendingTaskDetails), "transport")]
[JsonDerivedType(typeof(TradePendingTaskDetails), "trade")]
[JsonDerivedType(typeof(OutsourcePendingTaskDetails), "outsource")]
public abstract record PendingTaskDetails(PendingTaskKind Kind);

public sealed record TransportPendingTaskDetails() : PendingTaskDetails(PendingTaskKind.Transport)
{
    public required string SourceLocation { get; init; }

    public required string DestinationLocation { get; init; }

    public bool AllowPartialCompletion { get; init; } = true;

    public string? RouteGroupId { get; init; }

    public string? CargoSummary { get; init; }

    public decimal? VolumeCubicMeters { get; init; }

    public decimal? RewardHintIsk { get; init; }

    public decimal? CollateralHintIsk { get; init; }
}

public sealed record TradePendingTaskDetails() : PendingTaskDetails(PendingTaskKind.Trade)
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

public sealed record OutsourcePendingTaskDetails() : PendingTaskDetails(PendingTaskKind.Outsource)
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

public sealed record PendingTask
{
    public required string TaskId { get; init; }

    public required string WorkspaceId { get; init; }

    public required TaskOriginReference Origin { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? Notes { get; init; }

    public PendingTaskStatus Status { get; init; } = PendingTaskStatus.Pending;

    public required PendingTaskDetails Details { get; init; }

    public PendingTaskKind Kind => Details.Kind;

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? ReadyForPublishAtUtc { get; init; }
}

public sealed record PendingTaskPoolView
{
    public required string WorkspaceId { get; init; }

    public string? PlanId { get; init; }

    public IReadOnlyList<PendingTask> Tasks { get; init; } = Array.Empty<PendingTask>();

    public int PendingCount { get; init; }

    public int ReadyForPublishCount { get; init; }
}

public sealed record ManualPublishTaskPayload
{
    public required string TaskId { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string PublishText { get; init; }

    public required TaskOriginReference Origin { get; init; }

    public required PendingTaskDetails Details { get; init; }

    public PendingTaskKind Kind => Details.Kind;

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public DateTimeOffset ReadyForPublishAtUtc { get; init; }
}

public sealed record ManualPublishExportEnvelope
{
    public required string WorkspaceId { get; init; }

    public string? PlanId { get; init; }

    public DateTimeOffset ExportedAtUtc { get; init; }

    public IReadOnlyList<ManualPublishTaskPayload> Tasks { get; init; } = Array.Empty<ManualPublishTaskPayload>();
}
