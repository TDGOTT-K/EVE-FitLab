namespace EdenOS.Contracts.Planning;

public enum PlanEstimateConfidence
{
    High,
    Medium,
    Low
}

public enum PlanAlternativeStrategy
{
    CurrentChain,
    MarketHeavy,
    OutsourceIntermediates,
    InventoryFirst,
    ProfitMaximized
}

public sealed record PlanComputationResult
{
    public required string PlanId { get; init; }

    public required string PlanName { get; init; }

    public required DateTimeOffset ComputedAtUtc { get; init; }

    public required long MarketLocationId { get; init; }

    public string? MarketAccountKey { get; init; }

    public required PlanFeasibilitySummary Feasibility { get; init; }

    public required PlanCostBreakdown CostBreakdown { get; init; }

    public required PlanProfitEstimate ProfitEstimate { get; init; }

    public required PlanTimeEstimate TimeEstimate { get; init; }

    public required PlanConstraintSummary ConstraintSummary { get; init; }

    public IReadOnlyList<PlanAlternativePath> AlternativePaths { get; init; } = Array.Empty<PlanAlternativePath>();

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record PlanFeasibilitySummary
{
    public bool IsFeasible { get; init; }

    public PlanEstimateConfidence Confidence { get; init; } = PlanEstimateConfidence.Low;

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BlockingNodeIds { get; init; } = Array.Empty<string>();
}

public sealed record PlanCostBreakdown
{
    public decimal TotalCostIsk { get; init; }

    public decimal PurchaseCostIsk { get; init; }

    public decimal IndustryCostIsk { get; init; }

    public decimal LogisticsCostIsk { get; init; }

    public decimal OtherCostIsk { get; init; }

    public IReadOnlyList<PlanCostLineItem> LineItems { get; init; } = Array.Empty<PlanCostLineItem>();

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record PlanCostLineItem
{
    public required string Category { get; init; }

    public required string Title { get; init; }

    public decimal AmountIsk { get; init; }

    public string? NodeId { get; init; }

    public string? ResourceTypeId { get; init; }

    public decimal? Quantity { get; init; }

    public bool UsesMarketFacts { get; init; }

    public bool UsesPlaceholderFacts { get; init; }
}

public sealed record PlanProfitEstimate
{
    public decimal EstimatedRevenueIsk { get; init; }

    public decimal EstimatedCostIsk { get; init; }

    public decimal EstimatedProfitIsk { get; init; }

    public decimal MarginPercent { get; init; }

    public PlanEstimateConfidence Confidence { get; init; } = PlanEstimateConfidence.Low;

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record PlanTimeEstimate
{
    public decimal TotalDurationHours { get; init; }

    public decimal CriticalPathHours { get; init; }

    public IReadOnlyList<PlanTimeSegment> Segments { get; init; } = Array.Empty<PlanTimeSegment>();

    public IReadOnlyList<PlanCharacterSchedule> CharacterSchedules { get; init; } = Array.Empty<PlanCharacterSchedule>();

    public IReadOnlyList<PlanOperatorSchedule> OperatorSchedules { get; init; } = Array.Empty<PlanOperatorSchedule>();

    public IReadOnlyList<string> CriticalNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

public sealed record PlanTimeSegment
{
    public required string NodeId { get; init; }

    public required string Title { get; init; }

    public decimal DurationHours { get; init; }

    public decimal ScheduledStartHours { get; init; }

    public decimal ScheduledFinishHours { get; init; }

    public bool OnCriticalPath { get; init; }

    public string? AssignedCharacterId { get; init; }

    public string? AssignedCharacterName { get; init; }

    public string? AssignedOperatorId { get; init; }

    public string? AssignedOperatorName { get; init; }

    public IReadOnlyList<string> AssignedCharacterIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AssignedCharacterNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AssignedOperatorIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AssignedOperatorNames { get; init; } = Array.Empty<string>();

    public int ParallelCharacterCount { get; init; }

    public int ConsumedCharacterSlots { get; init; }

    public decimal EstimatedManualLoadUnits { get; init; }

    public IReadOnlyList<PlanTimeCharacterAllocation> CharacterAllocations { get; init; } = Array.Empty<PlanTimeCharacterAllocation>();

    public IReadOnlyList<string> DependsOnNodeIds { get; init; } = Array.Empty<string>();

    public string? AssignmentRationale { get; init; }
}

public sealed record PlanCharacterSchedule
{
    public required string CharacterId { get; init; }

    public required string CharacterName { get; init; }

    public int AssignedNodeCount { get; init; }

    public decimal ScheduledHours { get; init; }

    public int PeakConsumedSlots { get; init; }

    public IReadOnlyList<string> AssignedNodeIds { get; init; } = Array.Empty<string>();
}

public sealed record PlanOperatorSchedule
{
    public required string OperatorId { get; init; }

    public required string OperatorName { get; init; }

    public int AssignedNodeCount { get; init; }

    public int AssignedCharacterCount { get; init; }

    public decimal ScheduledHours { get; init; }

    public decimal TotalManualLoadUnits { get; init; }

    public decimal? MaxManualLoadUnits { get; init; }

    public bool IsOverloaded { get; init; }

    public IReadOnlyList<string> AssignedNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AssignedCharacterIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AssignedCharacterNames { get; init; } = Array.Empty<string>();
}

public sealed record PlanTimeCharacterAllocation
{
    public required string CharacterId { get; init; }

    public required string CharacterName { get; init; }

    public int ConsumedSlots { get; init; }

    public decimal ScheduledStartHours { get; init; }

    public decimal ScheduledFinishHours { get; init; }
}

public sealed record PlanConstraintSummary
{
    public IReadOnlyList<string> HardConflicts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SoftWarnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PlanResourceReservationSummary> ResourceReservations { get; init; } = Array.Empty<PlanResourceReservationSummary>();

    public IReadOnlyList<PlanNodeConstraintStatus> NodeStatuses { get; init; } = Array.Empty<PlanNodeConstraintStatus>();

    public IReadOnlyList<string> UncertainFacts { get; init; } = Array.Empty<string>();
}

public enum PlanNodeConstraintStatusKind
{
    Clear,
    Warning,
    PotentialResourceContention,
    Blocked
}

public sealed record PlanNodeConstraintStatus
{
    public required string NodeId { get; init; }

    public required string Title { get; init; }

    public required PlanNodeKind NodeKind { get; init; }

    public required PlanNodeConstraintStatusKind StatusKind { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DependsOnNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReservationConnectedNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CandidateCharacterIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CandidateCharacterNames { get; init; } = Array.Empty<string>();

    public string? SuggestedCharacterId { get; init; }

    public string? SuggestedCharacterName { get; init; }

    public string? SuggestedCharacterRationale { get; init; }

    public string? SuggestedOperatorId { get; init; }

    public string? SuggestedOperatorName { get; init; }
}

public sealed record PlanResourceReservationSummary
{
    public required string ResourceKind { get; init; }

    public int DeclaredSlots { get; init; }

    public int ReservedNodeCount { get; init; }

    public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();

    public required string Explanation { get; init; }
}

public sealed record PlanAlternativePath
{
    public required string PathId { get; init; }

    public required PlanAlternativeStrategy Strategy { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public decimal EstimatedCostIsk { get; init; }

    public decimal EstimatedRevenueIsk { get; init; }

    public decimal EstimatedProfitIsk { get; init; }

    public decimal EstimatedDurationHours { get; init; }

    public bool IsPreferred { get; init; }

    public IReadOnlyList<string> Tradeoffs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AffectedNodeIds { get; init; } = Array.Empty<string>();
}
