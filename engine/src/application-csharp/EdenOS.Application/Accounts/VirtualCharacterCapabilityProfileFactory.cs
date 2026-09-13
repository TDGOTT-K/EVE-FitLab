using EdenOS.Contracts.CharacterPool;

namespace EdenOS.Application.Accounts;

public static class VirtualCharacterCapabilityProfileFactory
{
    public static CharacterCapabilityProfile BuildIndustrialCapabilityProfile(
        string skillProfile,
        IReadOnlyList<CharacterIndustrySkillLevel> skillLevels,
        bool canParticipateInIndustryPlanning = true,
        bool canCoverLogisticsTasks = false,
        bool isUnlimitedSkillSimulation = false,
        IReadOnlyList<string>? specialtyTags = null,
        IReadOnlyList<string>? facilityAccessTags = null,
        decimal inventionSuccessBonusPercent = 0m)
    {
        var normalizedSkillProfile = string.IsNullOrWhiteSpace(skillProfile)
            ? throw new ArgumentException("Skill profile is required.", nameof(skillProfile))
            : skillProfile.Trim();

        var industryProfile = IndustrySkillCapabilityProjector.Project(
            new CharacterIndustryCapabilityProfile
            {
                InventionSuccessBonusPercent = Math.Max(0m, inventionSuccessBonusPercent),
                SpecialtyTags = NormalizeTags(specialtyTags),
                FacilityAccessTags = NormalizeTags(facilityAccessTags)
            },
            skillLevels,
            $"Projected from simulated skill profile '{normalizedSkillProfile}'.");

        return new CharacterCapabilityProfile
        {
            SkillProfile = normalizedSkillProfile,
            CanParticipateInIndustryPlanning = canParticipateInIndustryPlanning,
            CanProvideAuthenticatedMarketAccess = false,
            CanCoverLogisticsTasks = canCoverLogisticsTasks,
            IsUnlimitedSkillSimulation = isUnlimitedSkillSimulation,
            Industry = industryProfile
        };
    }

    public static CharacterCapabilityProfile BuildIndustrialCapabilityProfileFromTemplate(
        string templateKey,
        string skillProfile,
        bool? canParticipateInIndustryPlanning = null,
        bool? canCoverLogisticsTasks = null,
        bool? isUnlimitedSkillSimulation = null,
        IReadOnlyList<CharacterIndustrySkillLevel>? skillLevelOverrides = null,
        IReadOnlyList<string>? specialtyTags = null,
        IReadOnlyList<string>? facilityAccessTags = null,
        decimal? inventionSuccessBonusPercent = null)
    {
        var template = VirtualCharacterTemplateCatalog.GetRequired(templateKey);
        return BuildIndustrialCapabilityProfile(
            skillProfile,
            MergeSkillLevels(template.SkillLevels, skillLevelOverrides),
            canParticipateInIndustryPlanning: canParticipateInIndustryPlanning ?? template.CanParticipateInIndustryPlanning,
            canCoverLogisticsTasks: canCoverLogisticsTasks ?? template.CanCoverLogisticsTasks,
            isUnlimitedSkillSimulation: isUnlimitedSkillSimulation ?? template.IsUnlimitedSkillSimulation,
            specialtyTags: MergeTags(template.SpecialtyTags, specialtyTags),
            facilityAccessTags: MergeTags(template.FacilityAccessTags, facilityAccessTags),
            inventionSuccessBonusPercent: inventionSuccessBonusPercent ?? template.InventionSuccessBonusPercent);
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        return tags is null
            ? Array.Empty<string>()
            : tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static IReadOnlyList<CharacterIndustrySkillLevel> MergeSkillLevels(
        IReadOnlyList<CharacterIndustrySkillLevel> baseSkillLevels,
        IReadOnlyList<CharacterIndustrySkillLevel>? overrideSkillLevels)
    {
        var merged = baseSkillLevels
            .Where(skill => !string.IsNullOrWhiteSpace(skill.SkillKey))
            .ToDictionary(
                skill => skill.SkillKey.Trim(),
                skill => Math.Clamp(skill.Level, 0, 5),
                StringComparer.OrdinalIgnoreCase);

        if (overrideSkillLevels is not null)
        {
            foreach (var overrideSkill in overrideSkillLevels)
            {
                if (string.IsNullOrWhiteSpace(overrideSkill.SkillKey))
                {
                    continue;
                }

                merged[overrideSkill.SkillKey.Trim()] = Math.Clamp(overrideSkill.Level, 0, 5);
            }
        }

        return merged
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new CharacterIndustrySkillLevel
            {
                SkillKey = entry.Key,
                Level = entry.Value
            })
            .ToArray();
    }

    private static IReadOnlyList<string> MergeTags(
        IReadOnlyList<string> baseTags,
        IReadOnlyList<string>? overrideTags)
    {
        return NormalizeTags(baseTags.Concat(overrideTags ?? Array.Empty<string>()).ToArray());
    }
}
