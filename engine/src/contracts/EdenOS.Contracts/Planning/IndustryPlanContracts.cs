using System.Text.Json.Serialization;

namespace EdenOS.Contracts.Planning;

public enum PlanStatus
{
    Draft,
    Active,
    Archived
}

public enum PlanningPriority
{
    Balanced,
    TimeFirst,
    ProfitFirst,
    CapitalFirst
}

public enum PlanLogisticsTolerance
{
    Flexible,
    Balanced,
    Minimal
}

public enum PlanNodeKind
{
    Production,
    Reaction,
    CopyOrInvention,
    InventoryPool,
    Transport,
    Trade
}

public enum PlanLinkKind
{
    MaterialFlow,
    LogisticsFlow,
    TradeFlow,
    Reservation,
    Dependency
}

public enum ProductionActivityKind
{
    Generic,
    Manufacturing,
    Reprocessing,
    PlanetaryIndustry,
    Mining,
    SalvageReprocessing
}

public enum CopyOrInventionActivity
{
    Copy,
    Invention
}

public enum PlanTradeMode
{
    Purchase,
    Sale,
    PrivateExchange,
    OutsourceInput
}

public enum ResourceSlotKind
{
    Character,
    Blueprint,
    Bpc,
    Job,
    Facility,
    Other
}

public sealed record PlanGoal
{
    public required string TargetTypeId { get; init; }

    public required string TargetName { get; init; }

    public required decimal Quantity { get; init; }

    public DateTimeOffset? DeliveryWindowStartUtc { get; init; }

    public DateTimeOffset? DeliveryDeadlineUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record PlanPreferences
{
    public PlanningPriority PrimaryPriority { get; init; } = PlanningPriority.Balanced;

    public bool AllowMarketPurchases { get; init; } = true;

    public bool AllowIntermediateBuying { get; init; } = true;

    public bool AllowOutsourcing { get; init; } = true;

    public bool AllowMultiLocationExecution { get; init; } = true;

    public bool PreferExistingInventory { get; init; } = true;

    public bool PreferSimplerChains { get; init; }

    public bool PreferLowerCharacterLoad { get; init; }

    public int? MaxDailyManualOperations { get; init; }

    public int? MaxAcceptedTransportLegs { get; init; }

    public PlanLogisticsTolerance LogisticsTolerance { get; init; } = PlanLogisticsTolerance.Balanced;

    public bool RequireOwnedReactionFacility { get; init; }

    public bool RequireOwnedCapitalProductionFacility { get; init; }

    public string? Notes { get; init; }
}

public sealed record ResourceSlotSpec
{
    public required string SlotId { get; init; }

    public required ResourceSlotKind Kind { get; init; }

    public required string Label { get; init; }

    public bool Exclusive { get; init; } = true;

    public string? Notes { get; init; }
}

public sealed record PlanNodeResourceProfile
{
    public int CharacterSlots { get; init; }

    public int BlueprintSlots { get; init; }

    public int BpcSlots { get; init; }

    public int JobSlots { get; init; }

    public IReadOnlyList<ResourceSlotSpec> AdditionalSlots { get; init; } = Array.Empty<ResourceSlotSpec>();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ProductionNodeDetails), "production")]
[JsonDerivedType(typeof(ReactionNodeDetails), "reaction")]
[JsonDerivedType(typeof(CopyOrInventionNodeDetails), "copy_or_invention")]
[JsonDerivedType(typeof(InventoryPoolNodeDetails), "inventory_pool")]
[JsonDerivedType(typeof(TransportNodeDetails), "transport")]
[JsonDerivedType(typeof(TradeNodeDetails), "trade")]
public abstract record PlanNodeDetails(PlanNodeKind Kind);

public sealed record ProductionNodeDetails() : PlanNodeDetails(PlanNodeKind.Production)
{
    public ProductionActivityKind Activity { get; init; } = ProductionActivityKind.Generic;

    public string? RecipeTypeId { get; init; }

    public string? RecipeName { get; init; }

    public bool CanUseExistingInventory { get; init; } = true;
}

public sealed record ReactionNodeDetails() : PlanNodeDetails(PlanNodeKind.Reaction)
{
    public string? ReactionTypeId { get; init; }

    public string? ReactionName { get; init; }

    public bool UsesFormulaBasedInputs { get; init; } = true;
}

public sealed record CopyOrInventionNodeDetails() : PlanNodeDetails(PlanNodeKind.CopyOrInvention)
{
    public required CopyOrInventionActivity Activity { get; init; }

    public required string BlueprintTypeId { get; init; }

    public required string BlueprintName { get; init; }

    public int? CopyRuns { get; init; }

    public int? MaterialEfficiency { get; init; }

    public int? TimeEfficiency { get; init; }

    public bool RequiresPerBpcTracking { get; init; } = true;
}

public sealed record InventoryPoolNodeDetails() : PlanNodeDetails(PlanNodeKind.InventoryPool)
{
    public string? PoolPurpose { get; init; }

    public bool HideUpstreamDetailsByDefault { get; init; } = true;

    public bool PreserveSourceTraceability { get; init; } = true;
}

public sealed record TransportNodeDetails() : PlanNodeDetails(PlanNodeKind.Transport)
{
    public required string SourceLocation { get; init; }

    public required string DestinationLocation { get; init; }

    public bool AllowPartialCompletion { get; init; } = true;

    public string? RouteGroupId { get; init; }
}

public sealed record TradeNodeDetails() : PlanNodeDetails(PlanNodeKind.Trade)
{
    public required PlanTradeMode TradeMode { get; init; }

    public string? MarketScope { get; init; }

    public decimal? UnitPricePreference { get; init; }

    public bool UsesMarketFacts { get; init; } = true;
}

public sealed record PlanNode
{
    public required string NodeId { get; init; }

    public required string Title { get; init; }

    public string? Notes { get; init; }

    public string? LocationLabel { get; init; }

    public required PlanNodeDetails Details { get; init; }

    public PlanNodeKind Kind => Details.Kind;

    public PlanNodeResourceProfile ResourceProfile { get; init; } = new();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record PlanLink
{
    public required string LinkId { get; init; }

    public required string FromNodeId { get; init; }

    public required string ToNodeId { get; init; }

    public required PlanLinkKind Kind { get; init; }

    public string? Label { get; init; }

    public string? ResourceTypeId { get; init; }

    public decimal? Quantity { get; init; }

    public bool IsOptional { get; init; }
}

public sealed record IndustryPlan
{
    public string WorkspaceId { get; init; } = string.Empty;

    public required string PlanId { get; init; }

    public required string Name { get; init; }

    public string? Notes { get; init; }

    public PlanStatus Status { get; init; } = PlanStatus.Draft;

    public PlanGoal? Goal { get; init; }

    public PlanPreferences Preferences { get; init; } = new();

    public IReadOnlyList<PlanNode> Nodes { get; init; } = Array.Empty<PlanNode>();

    public IReadOnlyList<PlanLink> Links { get; init; } = Array.Empty<PlanLink>();

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record PlanSummary
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string Name { get; init; }

    public required PlanStatus Status { get; init; }

    public string? GoalTargetName { get; init; }
    public string? GoalTargetTypeId { get; init; }

    public decimal? GoalQuantity { get; init; }

    public int NodeCount { get; init; }

    public int LinkCount { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
