using EdenOS.Contracts.CharacterProgression;

namespace EdenOS.Application.CharacterProgression;

internal sealed class EmbeddedCharacterProgressionSkillCatalogProvider : ICharacterProgressionSkillCatalogProvider
{
    private static readonly CharacterProgressionSkillDefinition[] Skills =
    [
        new()
        {
            SkillKey = "industry",
            DisplayName = "Industry",
            Rank = 1,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory
        },
        new()
        {
            SkillKey = "advanced_industry",
            DisplayName = "Advanced Industry",
            Rank = 3,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "industry",
                    RequiredLevel = 5
                }
            ]
        },
        new()
        {
            SkillKey = "mass_production",
            DisplayName = "Mass Production",
            Rank = 2,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "industry",
                    RequiredLevel = 3
                }
            ]
        },
        new()
        {
            SkillKey = "advanced_mass_production",
            DisplayName = "Advanced Mass Production",
            Rank = 8,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "industry",
                    RequiredLevel = 5
                },
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "mass_production",
                    RequiredLevel = 5
                }
            ]
        },
        new()
        {
            SkillKey = "science",
            DisplayName = "Science",
            Rank = 1,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory
        },
        new()
        {
            SkillKey = "research",
            DisplayName = "Research",
            Rank = 1,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "science",
                    RequiredLevel = 3
                }
            ]
        },
        new()
        {
            SkillKey = "laboratory_operation",
            DisplayName = "Laboratory Operation",
            Rank = 1,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "science",
                    RequiredLevel = 3
                }
            ]
        },
        new()
        {
            SkillKey = "advanced_laboratory_operation",
            DisplayName = "Advanced Laboratory Operation",
            Rank = 8,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "research",
                    RequiredLevel = 5
                },
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "laboratory_operation",
                    RequiredLevel = 5
                }
            ]
        },
        new()
        {
            SkillKey = "reactions",
            DisplayName = "Reactions",
            Rank = 5,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory
        },
        new()
        {
            SkillKey = "mass_reactions",
            DisplayName = "Mass Reactions",
            Rank = 8,
            PrimaryAttribute = CharacterAttributeKind.Intelligence,
            SecondaryAttribute = CharacterAttributeKind.Memory,
            Prerequisites =
            [
                new CharacterProgressionSkillPrerequisite
                {
                    SkillKey = "reactions",
                    RequiredLevel = 5
                }
            ]
        }
    ];

    private static readonly IReadOnlyDictionary<string, CharacterProgressionSkillDefinition> SkillMap =
        Skills.ToDictionary(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CharacterProgressionSkillDefinition> List() => Skills;

    public bool TryGet(string skillKey, out CharacterProgressionSkillDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(skillKey))
        {
            definition = null!;
            return false;
        }

        return SkillMap.TryGetValue(skillKey.Trim(), out definition!);
    }

    public CharacterProgressionSkillDefinition GetRequired(string skillKey)
    {
        if (TryGet(skillKey, out var definition))
        {
            return definition;
        }

        throw new KeyNotFoundException($"Unsupported character progression skill key '{skillKey}'.");
    }
}
