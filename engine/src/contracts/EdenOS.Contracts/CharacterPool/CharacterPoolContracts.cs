using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.CharacterPool;

public enum CharacterSourceKind
{
    EsiBound,
    Virtual
}

public enum VirtualCharacterKind
{
    Manual,
    FullSkillSimulation
}

public sealed record CharacterIndustrySkillLevel
{
    public required string SkillKey { get; init; }

    public int Level { get; init; }
}

public sealed record CharacterIndustryCapabilityProfile
{
    public bool CanRunManufacturingJobs { get; init; } = true;

    public bool CanRunReactionJobs { get; init; } = true;

    public bool CanRunCopyJobs { get; init; } = true;

    public bool CanRunInventionJobs { get; init; } = true;

    public bool CanRunReprocessingJobs { get; init; } = true;

    public int ManufacturingJobSlotCapacity { get; init; } = 1;

    public int ReactionJobSlotCapacity { get; init; } = 1;

    public int CopyJobSlotCapacity { get; init; } = 1;

    public int InventionJobSlotCapacity { get; init; } = 1;

    public int ReprocessingJobSlotCapacity { get; init; } = 1;

    public decimal ManufacturingTimeEfficiencyPercent { get; init; }

    public decimal ReactionTimeEfficiencyPercent { get; init; }

    public decimal InventionSuccessBonusPercent { get; init; }

    public decimal ReprocessingYieldPercent { get; init; }

    public IReadOnlyList<CharacterIndustrySkillLevel> SkillLevels { get; init; } = Array.Empty<CharacterIndustrySkillLevel>();

    public IReadOnlyList<string> SpecialtyTags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FacilityAccessTags { get; init; } = Array.Empty<string>();

    public string? Notes { get; init; }
}

public sealed record CharacterCapabilityProfile
{
    public required string SkillProfile { get; init; }

    public bool CanParticipateInIndustryPlanning { get; init; } = true;

    public bool CanProvideAuthenticatedMarketAccess { get; init; }

    public bool CanCoverLogisticsTasks { get; init; }

    public bool IsUnlimitedSkillSimulation { get; init; }

    public CharacterIndustryCapabilityProfile Industry { get; init; } = new();
}

public sealed record CharacterPoolEntry
{
    public required string CharacterId { get; init; }

    public required string CharacterPoolId { get; init; }

    public required string DisplayName { get; init; }

    public required CharacterSourceKind SourceKind { get; init; }

    public string? EsiCharacterId { get; init; }

    public VirtualCharacterKind? VirtualKind { get; init; }

    public string? OperatorId { get; init; }

    public required CharacterCapabilityProfile CapabilityProfile { get; init; }

    public bool IsPrimaryAccountCharacter { get; init; }

    public bool IsSelectableForIndustryPlanning { get; init; } = true;

    public bool IsSelectableForMarketAccess { get; init; }

    public string? Notes { get; init; }
}

public sealed record CharacterPoolView
{
    public required string WorkspaceId { get; init; }

    public required string CharacterPoolId { get; init; }

    public IReadOnlyList<CharacterPoolEntry> Characters { get; init; } = Array.Empty<CharacterPoolEntry>();
}

public sealed record OperatorPoolEntry
{
    public required string WorkspaceId { get; init; }

    public required string OperatorId { get; init; }

    public required string DisplayName { get; init; }

    public bool CanCoordinateIndustryPlanning { get; init; } = true;

    public int? MaxDailyManualOperations { get; init; }

    public IReadOnlyList<string> ResponsibilityTags { get; init; } = Array.Empty<string>();

    public string? Notes { get; init; }
}

public sealed record OperatorPoolView
{
    public required string WorkspaceId { get; init; }

    public IReadOnlyList<OperatorPoolEntry> Operators { get; init; } = Array.Empty<OperatorPoolEntry>();
}

public sealed record ListCharacterPoolRequest
{
    public required string WorkspaceId { get; init; }
}

public sealed record ListOperatorPoolRequest
{
    public required string WorkspaceId { get; init; }
}

public sealed record AddOperatorRequest
{
    public required string WorkspaceId { get; init; }

    public required string DisplayName { get; init; }

    public bool CanCoordinateIndustryPlanning { get; init; } = true;

    public int? MaxDailyManualOperations { get; init; }

    public IReadOnlyList<string> ResponsibilityTags { get; init; } = Array.Empty<string>();

    public string? Notes { get; init; }
}

public sealed record UpdateOperatorRequest
{
    public required string WorkspaceId { get; init; }

    public required string OperatorId { get; init; }

    public string? DisplayName { get; init; }

    public bool? CanCoordinateIndustryPlanning { get; init; }

    public int? MaxDailyManualOperations { get; init; }

    public IReadOnlyList<string>? ResponsibilityTags { get; init; }

    public string? Notes { get; init; }
}

public sealed record AssignCharacterOperatorRequest
{
    public required string WorkspaceId { get; init; }

    public required string CharacterId { get; init; }

    public string? OperatorId { get; init; }
}

public sealed record AddVirtualCharacterRequest
{
    public required string WorkspaceId { get; init; }

    public required string DisplayName { get; init; }

    public required VirtualCharacterKind VirtualKind { get; init; }

    public string? OperatorId { get; init; }

    public required CharacterCapabilityProfile CapabilityProfile { get; init; }

    public bool IsSelectableForIndustryPlanning { get; init; } = true;

    public string? Notes { get; init; }
}

public sealed record UpdateVirtualCharacterRequest
{
    public required string WorkspaceId { get; init; }

    public required string CharacterId { get; init; }

    public string? DisplayName { get; init; }

    public string? OperatorId { get; init; }

    public CharacterCapabilityProfile? CapabilityProfile { get; init; }

    public bool? IsSelectableForIndustryPlanning { get; init; }

    public string? Notes { get; init; }
}

public sealed record AttachEsiCharacterRequest
{
    public required string WorkspaceId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public string? OperatorId { get; init; }

    public required CharacterCapabilityProfile CapabilityProfile { get; init; }

    public bool IsSelectableForIndustryPlanning { get; init; } = true;

    public bool IsSelectableForMarketAccess { get; init; } = true;

    public string? Notes { get; init; }
}

public sealed record UpdateEsiCharacterCapabilityProfileRequest
{
    public required string WorkspaceId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required CharacterCapabilityProfile CapabilityProfile { get; init; }

    public string? Notes { get; init; }
}

public interface ICharacterPoolService
{
    UseCaseResult<CharacterPoolView> List(ListCharacterPoolRequest request);

    UseCaseResult<CharacterPoolEntry> AddVirtualCharacter(AddVirtualCharacterRequest request);

    UseCaseResult<CharacterPoolEntry> UpdateVirtualCharacter(UpdateVirtualCharacterRequest request);

    UseCaseResult<CharacterPoolEntry> AttachEsiCharacter(AttachEsiCharacterRequest request);

    UseCaseResult<CharacterPoolEntry> UpdateEsiCharacterCapabilityProfile(UpdateEsiCharacterCapabilityProfileRequest request);
}

public interface IOperatorPoolService
{
    UseCaseResult<OperatorPoolView> ListOperators(ListOperatorPoolRequest request);

    UseCaseResult<OperatorPoolEntry> AddOperator(AddOperatorRequest request);

    UseCaseResult<OperatorPoolEntry> UpdateOperator(UpdateOperatorRequest request);

    UseCaseResult<CharacterPoolEntry> AssignCharacterOperator(AssignCharacterOperatorRequest request);
}
