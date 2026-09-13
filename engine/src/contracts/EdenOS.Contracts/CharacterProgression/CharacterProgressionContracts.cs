using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.CharacterProgression;

public enum CharacterAttributeKind
{
    Intelligence,
    Memory,
    Perception,
    Willpower,
    Charisma
}

public enum CharacterProgressionSupplementStrategyKind
{
    PassiveOnly,
    UseUnallocatedFirst,
    UseUnallocatedAndInjectors
}

public enum CharacterProgressionSupplementKind
{
    UnallocatedSkillPoints,
    SkillInjector
}

public sealed record CharacterProgressionSkillPrerequisite
{
    public required string SkillKey { get; init; }

    public int RequiredLevel { get; init; }
}

public sealed record CharacterProgressionSkillDefinition
{
    public required string SkillKey { get; init; }

    public required string DisplayName { get; init; }

    public int Rank { get; init; } = 1;

    public CharacterAttributeKind PrimaryAttribute { get; init; } = CharacterAttributeKind.Intelligence;

    public CharacterAttributeKind SecondaryAttribute { get; init; } = CharacterAttributeKind.Memory;

    public IReadOnlyList<CharacterProgressionSkillPrerequisite> Prerequisites { get; init; } = Array.Empty<CharacterProgressionSkillPrerequisite>();

    public string? Notes { get; init; }
}

public sealed record CharacterProgressionAttributeProfile
{
    public int Intelligence { get; init; } = 20;

    public int Memory { get; init; } = 20;

    public int Perception { get; init; } = 20;

    public int Willpower { get; init; } = 20;

    public int Charisma { get; init; } = 19;
}

public sealed record CharacterProgressionSupplementStrategy
{
    public CharacterProgressionSupplementStrategyKind Kind { get; init; } = CharacterProgressionSupplementStrategyKind.PassiveOnly;

    public long UnallocatedSkillPoints { get; init; }

    public int MaxInjectors { get; init; }
}

public sealed record CharacterProgressionTargetSkillRequest
{
    public required string SkillKey { get; init; }

    public int TargetLevel { get; init; }
}

public sealed record GetCharacterProgressionSkillCatalogRequest;

public sealed record SimulateCharacterProgressionRequest
{
    public string? WorkspaceId { get; init; }

    public string? CharacterId { get; init; }

    public IReadOnlyList<CharacterIndustrySkillLevel> CurrentSkillLevels { get; init; } = Array.Empty<CharacterIndustrySkillLevel>();

    public long? CurrentTotalSkillPoints { get; init; }

    public CharacterProgressionAttributeProfile Attributes { get; init; } = new();

    public CharacterProgressionSupplementStrategy SupplementStrategy { get; init; } = new();

    public IReadOnlyList<CharacterProgressionTargetSkillRequest> Targets { get; init; } = Array.Empty<CharacterProgressionTargetSkillRequest>();
}

public sealed record CharacterProgressionSkillCatalogView
{
    public IReadOnlyList<CharacterProgressionSkillDefinition> Skills { get; init; } = Array.Empty<CharacterProgressionSkillDefinition>();
}

public sealed record CharacterProgressionPrerequisiteEdge
{
    public required string RequiredSkillKey { get; init; }

    public required string DependentSkillKey { get; init; }

    public int RequiredLevel { get; init; }
}

public sealed record CharacterProgressionPlannedSkill
{
    public required string SkillKey { get; init; }

    public required string DisplayName { get; init; }

    public bool IsExplicitTarget { get; init; }

    public int SkillRank { get; init; }

    public int CurrentLevel { get; init; }

    public int TargetLevel { get; init; }

    public long CurrentSkillPoints { get; init; }

    public long TargetSkillPoints { get; init; }

    public long RemainingSkillPoints { get; init; }

    public long SupplementedSkillPoints { get; init; }

    public long PassiveTrainingSkillPoints { get; init; }

    public decimal TrainingRatePerHour { get; init; }

    public decimal PassiveTrainingHours { get; init; }

    public CharacterAttributeKind PrimaryAttribute { get; init; }

    public CharacterAttributeKind SecondaryAttribute { get; init; }

    public IReadOnlyList<CharacterProgressionSkillPrerequisite> DirectPrerequisites { get; init; } = Array.Empty<CharacterProgressionSkillPrerequisite>();

    public IReadOnlyList<string> RequiredBySkillKeys { get; init; } = Array.Empty<string>();
}

public sealed record CharacterProgressionSupplementUsage
{
    public int Sequence { get; init; }

    public CharacterProgressionSupplementKind Kind { get; init; }

    public long GrantedSkillPoints { get; init; }

    public long EffectiveTotalSkillPointsAfterUse { get; init; }

    public long RemainingUnallocatedSkillPointsAfterUse { get; init; }

    public string? Description { get; init; }
}

public sealed record CharacterProgressionSimulationView
{
    public string? WorkspaceId { get; init; }

    public string? CharacterId { get; init; }

    public string? CharacterName { get; init; }

    public CharacterProgressionAttributeProfile Attributes { get; init; } = new();

    public IReadOnlyList<CharacterProgressionTargetSkillRequest> RequestedTargets { get; init; } = Array.Empty<CharacterProgressionTargetSkillRequest>();

    public IReadOnlyList<CharacterProgressionPrerequisiteEdge> PrerequisiteGraph { get; init; } = Array.Empty<CharacterProgressionPrerequisiteEdge>();

    public IReadOnlyList<CharacterProgressionPlannedSkill> PlannedSkills { get; init; } = Array.Empty<CharacterProgressionPlannedSkill>();

    public long StartingKnownSkillPoints { get; init; }

    public long StartingEffectiveTotalSkillPoints { get; init; }

    public long TotalRequiredSkillPoints { get; init; }

    public long SupplementedSkillPoints { get; init; }

    public long PassiveTrainingSkillPoints { get; init; }

    public decimal PassiveTrainingHours { get; init; }

    public DateTimeOffset ProjectedCompletionAtUtc { get; init; }

    public long RemainingUnallocatedSkillPoints { get; init; }

    public int InjectorsConsumed { get; init; }

    public CharacterProgressionSupplementStrategy SupplementStrategy { get; init; } = new();

    public IReadOnlyList<CharacterProgressionSupplementUsage> SupplementUsages { get; init; } = Array.Empty<CharacterProgressionSupplementUsage>();
}

public interface ICharacterProgressionService
{
    UseCaseResult<CharacterProgressionSkillCatalogView> GetSkillCatalog(GetCharacterProgressionSkillCatalogRequest request);

    UseCaseResult<CharacterProgressionSimulationView> Simulate(SimulateCharacterProgressionRequest request);
}
