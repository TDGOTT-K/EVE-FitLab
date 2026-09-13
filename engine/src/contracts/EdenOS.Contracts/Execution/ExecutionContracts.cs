using System.Text.Json.Serialization;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;

namespace EdenOS.Contracts.Execution;

public enum ExecutionEventKind
{
    StepDone,
    MaterialArrival,
    MaterialLoss,
    MarketChange,
    IndustryCostChange,
    LocationChange,
    ManualOverride,
    ManualOverrideResolved
}

public enum ExecutionActionKind
{
    ResolveBlocker,
    ExecuteNode,
    CreatePendingTask,
    ReviewPendingTask,
    ReplanReview
}

public enum ExecutionBlockerKind
{
    ConstraintConflict,
    ResourceContention,
    MaterialShortage,
    MarketShift,
    IndustryCostShift,
    LocationDrift,
    ManualOverride,
    UncertainFact
}

public enum ExecutionBlockerSeverity
{
    Warning,
    Critical
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StepDoneEventDetails), "step_done")]
[JsonDerivedType(typeof(MaterialArrivalEventDetails), "material_arrival")]
[JsonDerivedType(typeof(MaterialLossEventDetails), "material_loss")]
[JsonDerivedType(typeof(MarketChangeEventDetails), "market_change")]
[JsonDerivedType(typeof(IndustryCostChangeEventDetails), "industry_cost_change")]
[JsonDerivedType(typeof(LocationChangeEventDetails), "location_change")]
[JsonDerivedType(typeof(ManualOverrideEventDetails), "manual_override")]
[JsonDerivedType(typeof(ManualOverrideResolvedEventDetails), "manual_override_resolved")]
public abstract record ExecutionEventDetails(ExecutionEventKind Kind);

public sealed record StepDoneEventDetails() : ExecutionEventDetails(ExecutionEventKind.StepDone)
{
    public required string NodeId { get; init; }

    public required string NodeTitle { get; init; }
}

public sealed record MaterialArrivalEventDetails() : ExecutionEventDetails(ExecutionEventKind.MaterialArrival)
{
    public string? NodeId { get; init; }

    public string? NodeTitle { get; init; }

    public required string ResourceTypeId { get; init; }

    public required string ResourceName { get; init; }

    public required decimal Quantity { get; init; }

    public string? LocationLabel { get; init; }
}

public sealed record MaterialLossEventDetails() : ExecutionEventDetails(ExecutionEventKind.MaterialLoss)
{
    public string? NodeId { get; init; }

    public string? NodeTitle { get; init; }

    public required string ResourceTypeId { get; init; }

    public required string ResourceName { get; init; }

    public required decimal Quantity { get; init; }

    public string? LocationLabel { get; init; }
}

public sealed record MarketChangeEventDetails() : ExecutionEventDetails(ExecutionEventKind.MarketChange)
{
    public string? ResourceTypeId { get; init; }

    public string? ResourceName { get; init; }

    public long? MarketLocationId { get; init; }

    public required string ChangeSummary { get; init; }

    public string? ImpactHint { get; init; }
}

public sealed record IndustryCostChangeEventDetails() : ExecutionEventDetails(ExecutionEventKind.IndustryCostChange)
{
    public string? NodeId { get; init; }

    public string? NodeTitle { get; init; }

    public string? LocationLabel { get; init; }

    public decimal? PreviousSystemCostIndex { get; init; }

    public required decimal CurrentSystemCostIndex { get; init; }

    public required string ChangeSummary { get; init; }

    public string? ImpactHint { get; init; }
}

public sealed record LocationChangeEventDetails() : ExecutionEventDetails(ExecutionEventKind.LocationChange)
{
    public string? NodeId { get; init; }

    public string? NodeTitle { get; init; }

    public required string ChangeSummary { get; init; }

    public required string CurrentLocationLabel { get; init; }

    public bool IsRecovered { get; init; }
}

public sealed record ManualOverrideEventDetails() : ExecutionEventDetails(ExecutionEventKind.ManualOverride)
{
    public required string OverrideSummary { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> AffectedNodeIds { get; init; } = Array.Empty<string>();
}

public sealed record ManualOverrideResolvedEventDetails() : ExecutionEventDetails(ExecutionEventKind.ManualOverrideResolved)
{
    public required string ResolutionSummary { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> AffectedNodeIds { get; init; } = Array.Empty<string>();
}

public sealed record ExecutionEventRecord
{
    public required string EventId { get; init; }

    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string Summary { get; init; }

    public string? Notes { get; init; }

    public required ExecutionEventDetails Details { get; init; }

    public ExecutionEventKind Kind => Details.Kind;

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public DateTimeOffset OccurredAtUtc { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record ExecutionState
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public IReadOnlyList<string> CompletedNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ExecutionEventRecord> Events { get; init; } = Array.Empty<ExecutionEventRecord>();

    public int PendingTaskCount { get; init; }

    public int ReadyForPublishTaskCount { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public enum ExecutionNodeStatusKind
{
    Completed,
    WaitingOnDependency,
    BlockedByConstraint,
    BlockedByMaterial,
    BlockedByOverride,
    BlockedByResource,
    WaitingOnTaskReview,
    ReadyForTaskCreation,
    ReadyToExecute
}

public sealed record ExecutionNodeStatus
{
    public required string NodeId { get; init; }

    public required string Title { get; init; }

    public required PlanNodeKind NodeKind { get; init; }

    public required ExecutionNodeStatusKind StatusKind { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> DependsOnNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UnmetDependencyNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelatedBlockerIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelatedEventIds { get; init; } = Array.Empty<string>();

    public string? TaskId { get; init; }

    public PendingTaskKind? TaskKind { get; init; }
}

public sealed record ExecutionBlocker
{
    public required string BlockerId { get; init; }

    public required ExecutionBlockerKind Kind { get; init; }

    public required ExecutionBlockerSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? NodeId { get; init; }

    public string? ResourceTypeId { get; init; }

    public IReadOnlyList<string> RelatedEventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelatedTaskIds { get; init; } = Array.Empty<string>();

    public string? SuggestedResolution { get; init; }
}

public sealed record ExecutionBlockerView
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required DateTimeOffset EvaluatedAtUtc { get; init; }

    public required ExecutionState State { get; init; }

    public IReadOnlyList<ExecutionNodeStatus> NodeStatuses { get; init; } = Array.Empty<ExecutionNodeStatus>();

    public IReadOnlyList<ExecutionBlocker> Blockers { get; init; } = Array.Empty<ExecutionBlocker>();

    public int CriticalCount { get; init; }

    public int WarningCount { get; init; }

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record ExecutionNextAction
{
    public required string ActionId { get; init; }

    public required ExecutionActionKind Kind { get; init; }

    public required string Title { get; init; }

    public required string WhyThisAction { get; init; }

    public string? NodeId { get; init; }

    public string? TaskId { get; init; }

    public PendingTaskKind? TaskKind { get; init; }

    public IReadOnlyList<string> DependsOnNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelatedBlockerIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelatedEventIds { get; init; } = Array.Empty<string>();
}

public sealed record ExecutionNextActionsView
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required DateTimeOffset EvaluatedAtUtc { get; init; }

    public required ExecutionState State { get; init; }

    public required PlanFeasibilitySummary Feasibility { get; init; }

    public IReadOnlyList<ExecutionNodeStatus> NodeStatuses { get; init; } = Array.Empty<ExecutionNodeStatus>();

    public IReadOnlyList<ExecutionBlocker> Blockers { get; init; } = Array.Empty<ExecutionBlocker>();

    public IReadOnlyList<ExecutionNextAction> NextActions { get; init; } = Array.Empty<ExecutionNextAction>();

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record ExecutionReplanResult
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string ReplanReason { get; init; }

    public required DateTimeOffset ReplannedAtUtc { get; init; }

    public required ExecutionState State { get; init; }

    public required PlanComputationResult Computation { get; init; }

    public IReadOnlyList<ExecutionNodeStatus> NodeStatuses { get; init; } = Array.Empty<ExecutionNodeStatus>();

    public IReadOnlyList<ExecutionBlocker> Blockers { get; init; } = Array.Empty<ExecutionBlocker>();

    public IReadOnlyList<ExecutionNextAction> NextActions { get; init; } = Array.Empty<ExecutionNextAction>();

    public IReadOnlyList<string> EventExplanations { get; init; } = Array.Empty<string>();
}
