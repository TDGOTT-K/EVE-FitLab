using EdenOS.Contracts.CharacterPool;

namespace EdenOS.Application.Accounts;

public sealed record VirtualCharacterTemplateDefinition
{
    public required string TemplateKey { get; init; }

    public required string DisplayName { get; init; }

    public required string Summary { get; init; }

    public VirtualCharacterKind VirtualKind { get; init; } = VirtualCharacterKind.Manual;

    public bool CanParticipateInIndustryPlanning { get; init; } = true;

    public bool CanCoverLogisticsTasks { get; init; }

    public bool IsUnlimitedSkillSimulation { get; init; }

    public IReadOnlyList<CharacterIndustrySkillLevel> SkillLevels { get; init; } = Array.Empty<CharacterIndustrySkillLevel>();

    public IReadOnlyList<string> SpecialtyTags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FacilityAccessTags { get; init; } = Array.Empty<string>();

    public decimal InventionSuccessBonusPercent { get; init; }
}

public static class VirtualCharacterTemplateCatalog
{
    private static readonly IReadOnlyList<VirtualCharacterTemplateDefinition> OrderedTemplates =
    [
        new()
        {
            TemplateKey = "manufacturing-baseline",
            DisplayName = "制造基础号",
            Summary = "适合普通制造和日常补线的基础模板。",
            SkillLevels =
            [
                Skill("industry", 4),
                Skill("advanced_industry", 3),
                Skill("mass_production", 4),
                Skill("advanced_mass_production", 2)
            ],
            SpecialtyTags = ["t1_manufacturing"]
        },
        new()
        {
            TemplateKey = "manufacturing-max",
            DisplayName = "满技能制造号",
            Summary = "高并发普通制造模板，适合做标准制造主力号。",
            SkillLevels =
            [
                Skill("industry", 5),
                Skill("advanced_industry", 5),
                Skill("mass_production", 5),
                Skill("advanced_mass_production", 5)
            ],
            SpecialtyTags = ["t1_manufacturing", "t2_component_manufacturing"]
        },
        new()
        {
            TemplateKey = "capital-manufacturer",
            DisplayName = "资本制造号",
            Summary = "给资本组件和大型产线预留的制造模板。",
            SkillLevels =
            [
                Skill("industry", 5),
                Skill("advanced_industry", 5),
                Skill("mass_production", 5),
                Skill("advanced_mass_production", 5),
                Skill("laboratory_operation", 4)
            ],
            SpecialtyTags = ["capital_components", "capital_hulls"],
            FacilityAccessTags = ["capital_yard"]
        },
        new()
        {
            TemplateKey = "reaction-specialist",
            DisplayName = "反应号",
            Summary = "专门跑反应链的模板，默认不承担物流。",
            SkillLevels =
            [
                Skill("reactions", 5),
                Skill("mass_reactions", 5),
                Skill("advanced_mass_reactions", 4)
            ],
            SpecialtyTags = ["reactions"],
            FacilityAccessTags = ["reaction_structure"]
        },
        new()
        {
            TemplateKey = "invention-specialist",
            DisplayName = "发明号",
            Summary = "适合复制和发明链，带基础发明成功率加成占位。",
            SkillLevels =
            [
                Skill("industry", 5),
                Skill("advanced_industry", 4),
                Skill("laboratory_operation", 5),
                Skill("advanced_laboratory_operation", 5)
            ],
            SpecialtyTags = ["t2_invention"],
            InventionSuccessBonusPercent = 10m
        },
        new()
        {
            TemplateKey = "full-skill-industrialist",
            DisplayName = "全技能工业模拟号",
            Summary = "用于极限方案测试的全技能工业模板。",
            VirtualKind = VirtualCharacterKind.FullSkillSimulation,
            CanCoverLogisticsTasks = true,
            IsUnlimitedSkillSimulation = true,
            SkillLevels =
            [
                Skill("industry", 5),
                Skill("advanced_industry", 5),
                Skill("mass_production", 5),
                Skill("advanced_mass_production", 5),
                Skill("laboratory_operation", 5),
                Skill("advanced_laboratory_operation", 5),
                Skill("reactions", 5),
                Skill("mass_reactions", 5),
                Skill("advanced_mass_reactions", 5),
                Skill("reprocessing", 5)
            ],
            SpecialtyTags = ["capital_components", "capital_hulls", "reactions", "t2_invention"],
            FacilityAccessTags = ["capital_yard", "reaction_structure"],
            InventionSuccessBonusPercent = 15m
        }
    ];

    private static readonly IReadOnlyDictionary<string, VirtualCharacterTemplateDefinition> TemplatesByKey =
        OrderedTemplates.ToDictionary(template => template.TemplateKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<VirtualCharacterTemplateDefinition> List()
    {
        return OrderedTemplates;
    }

    public static bool TryGet(string templateKey, out VirtualCharacterTemplateDefinition template)
    {
        return TemplatesByKey.TryGetValue(templateKey, out template!);
    }

    public static VirtualCharacterTemplateDefinition GetRequired(string templateKey)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            throw new ArgumentException("Template key is required.", nameof(templateKey));
        }

        if (TemplatesByKey.TryGetValue(templateKey.Trim(), out var template))
        {
            return template;
        }

        throw new ArgumentOutOfRangeException(
            nameof(templateKey),
            templateKey,
            $"Unknown virtual character template '{templateKey}'.");
    }

    private static CharacterIndustrySkillLevel Skill(string skillKey, int level)
    {
        return new CharacterIndustrySkillLevel
        {
            SkillKey = skillKey,
            Level = level
        };
    }
}
