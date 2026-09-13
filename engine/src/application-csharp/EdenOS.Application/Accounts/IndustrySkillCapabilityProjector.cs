using EdenOS.Contracts.CharacterPool;

namespace EdenOS.Application.Accounts;

internal static class IndustrySkillCapabilityProjector
{
    public static CharacterIndustryCapabilityProfile Project(
        CharacterIndustryCapabilityProfile currentProfile,
        IReadOnlyList<CharacterIndustrySkillLevel> skillLevels,
        string? notes)
    {
        var normalizedSkillLevels = NormalizeSkillLevels(skillLevels);
        var skillLevelsByKey = normalizedSkillLevels
            .ToDictionary(
                skill => skill.SkillKey,
                skill => skill.Level,
                StringComparer.OrdinalIgnoreCase);

        var industryLevel = GetLevel(skillLevelsByKey, "industry");
        var advancedIndustryLevel = GetLevel(skillLevelsByKey, "advanced_industry");
        var reprocessingLevel = GetLevel(skillLevelsByKey, "reprocessing");
        var laboratoryOperationLevel = GetLevel(skillLevelsByKey, "laboratory_operation");
        var advancedLaboratoryOperationLevel = GetLevel(skillLevelsByKey, "advanced_laboratory_operation");
        var massProductionLevel = GetLevel(skillLevelsByKey, "mass_production");
        var advancedMassProductionLevel = GetLevel(skillLevelsByKey, "advanced_mass_production");
        var reactionsLevel = GetLevel(skillLevelsByKey, "reactions");
        var massReactionsLevel = GetLevel(skillLevelsByKey, "mass_reactions");
        var advancedMassReactionsLevel = GetLevel(skillLevelsByKey, "advanced_mass_reactions");

        var canRunManufacturingJobs = HasAnyLevel(industryLevel, advancedIndustryLevel, massProductionLevel, advancedMassProductionLevel);
        var canRunReactionJobs = HasAnyLevel(reactionsLevel, massReactionsLevel, advancedMassReactionsLevel);
        var canRunCopyJobs = HasAnyLevel(laboratoryOperationLevel, advancedLaboratoryOperationLevel);
        var canRunInventionJobs = HasAnyLevel(laboratoryOperationLevel, advancedLaboratoryOperationLevel);
        var canRunReprocessingJobs = reprocessingLevel > 0;

        return currentProfile with
        {
            CanRunManufacturingJobs = canRunManufacturingJobs,
            CanRunReactionJobs = canRunReactionJobs,
            CanRunCopyJobs = canRunCopyJobs,
            CanRunInventionJobs = canRunInventionJobs,
            CanRunReprocessingJobs = canRunReprocessingJobs,
            ManufacturingJobSlotCapacity = canRunManufacturingJobs
                ? ResolveConcurrentCapacity(massProductionLevel, advancedMassProductionLevel)
                : 0,
            ReactionJobSlotCapacity = canRunReactionJobs
                ? ResolveConcurrentCapacity(massReactionsLevel, advancedMassReactionsLevel)
                : 0,
            CopyJobSlotCapacity = canRunCopyJobs
                ? ResolveConcurrentCapacity(laboratoryOperationLevel, advancedLaboratoryOperationLevel)
                : 0,
            InventionJobSlotCapacity = canRunInventionJobs
                ? ResolveConcurrentCapacity(laboratoryOperationLevel, advancedLaboratoryOperationLevel)
                : 0,
            ReprocessingJobSlotCapacity = canRunReprocessingJobs ? 1 : 0,
            ManufacturingTimeEfficiencyPercent = (industryLevel * 4m) + (advancedIndustryLevel * 3m),
            ReactionTimeEfficiencyPercent = reactionsLevel * 4m,
            ReprocessingYieldPercent = reprocessingLevel * 3m,
            SkillLevels = normalizedSkillLevels,
            SpecialtyTags = NormalizeTags(currentProfile.SpecialtyTags),
            FacilityAccessTags = NormalizeTags(currentProfile.FacilityAccessTags),
            Notes = notes ?? currentProfile.Notes
        };
    }

    private static IReadOnlyList<CharacterIndustrySkillLevel> NormalizeSkillLevels(
        IReadOnlyList<CharacterIndustrySkillLevel> skillLevels)
    {
        return skillLevels
            .Where(skill => !string.IsNullOrWhiteSpace(skill.SkillKey))
            .Select(skill => new CharacterIndustrySkillLevel
            {
                SkillKey = skill.SkillKey.Trim(),
                Level = Math.Clamp(skill.Level, 0, 5)
            })
            .GroupBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CharacterIndustrySkillLevel
            {
                SkillKey = group.Key,
                Level = group.Max(item => item.Level)
            })
            .OrderBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tags)
    {
        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasAnyLevel(params int[] levels)
    {
        return levels.Any(level => level > 0);
    }

    private static int GetLevel(
        IReadOnlyDictionary<string, int> skillLevelsByKey,
        string skillKey)
    {
        return skillLevelsByKey.GetValueOrDefault(skillKey, 0);
    }

    private static int ResolveConcurrentCapacity(int basicLevel, int advancedLevel)
    {
        return 1 + Math.Max(0, basicLevel) + Math.Max(0, advancedLevel);
    }
}
