using System.Text;
using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Sde;
using EdenOS.Contracts.CharacterProgression;

namespace EdenOS.Application.CharacterProgression;

internal interface ICharacterProgressionSkillCatalogProvider
{
    IReadOnlyList<CharacterProgressionSkillDefinition> List();

    bool TryGet(string skillKey, out CharacterProgressionSkillDefinition definition);

    CharacterProgressionSkillDefinition GetRequired(string skillKey);
}

internal sealed class CharacterProgressionSkillCatalogProvider : ICharacterProgressionSkillCatalogProvider
{
    private const int SkillCategoryId = 16;
    private const int PrimaryAttributeAttributeId = 180;
    private const int SecondaryAttributeAttributeId = 181;
    private const int SkillTimeConstantAttributeId = 275;

    private static readonly (int SkillAttributeId, int LevelAttributeId)[] PrerequisiteAttributePairs =
    [
        (182, 277),
        (183, 278),
        (184, 279),
        (1285, 1286),
        (1289, 1287),
        (1290, 1288)
    ];

    private static readonly IReadOnlyDictionary<int, CharacterAttributeKind> CharacterAttributeKindsByDogmaAttributeId =
        new Dictionary<int, CharacterAttributeKind>
        {
            [164] = CharacterAttributeKind.Charisma,
            [165] = CharacterAttributeKind.Intelligence,
            [166] = CharacterAttributeKind.Memory,
            [167] = CharacterAttributeKind.Perception,
            [168] = CharacterAttributeKind.Willpower
        };

    private readonly IReadOnlyList<CharacterProgressionSkillDefinition> orderedSkills;
    private readonly IReadOnlyDictionary<string, CharacterProgressionSkillDefinition> skillsByKey;

    public CharacterProgressionSkillCatalogProvider(string sdeRootPath)
    {
        if (string.IsNullOrWhiteSpace(sdeRootPath))
        {
            throw new ArgumentException("SDE root path is required.", nameof(sdeRootPath));
        }

        var normalizedSdeRootPath = Path.GetFullPath(sdeRootPath.Trim());
        if (!Directory.Exists(normalizedSdeRootPath))
        {
            throw new DirectoryNotFoundException($"Character progression SDE root path was not found: '{normalizedSdeRootPath}'.");
        }

        orderedSkills = LoadCatalog(normalizedSdeRootPath);
        if (orderedSkills.Count == 0)
        {
            throw new InvalidOperationException($"Character progression SDE catalog at '{normalizedSdeRootPath}' did not produce any skill definitions.");
        }

        skillsByKey = orderedSkills.ToDictionary(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CharacterProgressionSkillDefinition> List() => orderedSkills;

    public bool TryGet(string skillKey, out CharacterProgressionSkillDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(skillKey))
        {
            definition = null!;
            return false;
        }

        return skillsByKey.TryGetValue(skillKey.Trim(), out definition!);
    }

    public CharacterProgressionSkillDefinition GetRequired(string skillKey)
    {
        if (TryGet(skillKey, out var definition))
        {
            return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(skillKey), skillKey, $"Unknown progression skill '{skillKey}'.");
    }

    private static IReadOnlyList<CharacterProgressionSkillDefinition> LoadCatalog(string sdeRootPath)
    {
        var groupsPath = Path.Combine(sdeRootPath, "groups.jsonl");
        var typesPath = Path.Combine(sdeRootPath, "types.jsonl");
        var typeDogmaPath = Path.Combine(sdeRootPath, "typeDogma.jsonl");

        EnsureFileExists(groupsPath);
        EnsureFileExists(typesPath);
        EnsureFileExists(typeDogmaPath);

        var groups = LoadGroups(groupsPath);
        var skillTypes = LoadSkillTypes(typesPath, groups);
        var typeDogmaByTypeId = LoadTypeDogma(typeDogmaPath);

        var definitions = new List<CharacterProgressionSkillDefinition>(skillTypes.Count);
        foreach (var skillType in skillTypes.Values.OrderBy(type => ResolveEnglishName(type.Name), StringComparer.OrdinalIgnoreCase))
        {
            if (!typeDogmaByTypeId.TryGetValue(skillType.TypeId, out var dogma))
            {
                continue;
            }

            var attributes = dogma.DogmaAttributes
                .GroupBy(attribute => attribute.AttributeId)
                .ToDictionary(group => group.Key, group => group.Last().Value);

            if (!TryGetRank(attributes, out var rank) ||
                !TryMapCharacterAttribute(attributes, PrimaryAttributeAttributeId, out var primaryAttribute) ||
                !TryMapCharacterAttribute(attributes, SecondaryAttributeAttributeId, out var secondaryAttribute))
            {
                continue;
            }

            var prerequisites = BuildPrerequisites(attributes, skillTypes)
                .OrderBy(prerequisite => prerequisite.SkillKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var displayName = ResolveEnglishName(skillType.Name);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var skillKey = CreateSkillKey(displayName);
            if (definitions.Any(existing => string.Equals(existing.SkillKey, skillKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Character progression SDE catalog produced duplicate skill key '{skillKey}'.");
            }

            definitions.Add(new CharacterProgressionSkillDefinition
            {
                SkillKey = skillKey,
                DisplayName = displayName,
                Rank = rank,
                PrimaryAttribute = primaryAttribute,
                SecondaryAttribute = secondaryAttribute,
                Prerequisites = prerequisites,
                Notes = $"Derived from SDE typeID {skillType.TypeId}."
            });
        }

        return definitions;
    }

    private static IReadOnlyDictionary<int, SdeGroup> LoadGroups(string groupsPath)
    {
        var groups = new Dictionary<int, SdeGroup>();
        foreach (var group in ReadJsonl<SdeGroup>(groupsPath))
        {
            if (group.GroupId <= 0)
            {
                continue;
            }

            groups[group.GroupId] = group;
        }

        return groups;
    }

    private static IReadOnlyDictionary<int, SdeType> LoadSkillTypes(string typesPath, IReadOnlyDictionary<int, SdeGroup> groups)
    {
        var types = new Dictionary<int, SdeType>();
        foreach (var type in ReadJsonl<SdeType>(typesPath))
        {
            if (type.TypeId <= 0 || !type.Published)
            {
                continue;
            }

            if (!groups.TryGetValue(type.GroupId, out var group) || group.CategoryId != SkillCategoryId)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ResolveEnglishName(type.Name)))
            {
                continue;
            }

            types[type.TypeId] = type;
        }

        return types;
    }

    private static IReadOnlyDictionary<int, SdeTypeDogma> LoadTypeDogma(string typeDogmaPath)
    {
        var result = new Dictionary<int, SdeTypeDogma>();
        foreach (var typeDogma in ReadJsonl<SdeTypeDogma>(typeDogmaPath))
        {
            if (typeDogma.TypeId <= 0)
            {
                continue;
            }

            result[typeDogma.TypeId] = typeDogma;
        }

        return result;
    }

    private static IReadOnlyList<CharacterProgressionSkillPrerequisite> BuildPrerequisites(
        IReadOnlyDictionary<int, double> attributes,
        IReadOnlyDictionary<int, SdeType> skillTypes)
    {
        var prerequisites = new List<CharacterProgressionSkillPrerequisite>();
        foreach (var (skillAttributeId, levelAttributeId) in PrerequisiteAttributePairs)
        {
            if (!attributes.TryGetValue(skillAttributeId, out var skillTypeIdValue))
            {
                continue;
            }

            var requiredTypeId = Convert.ToInt32(Math.Round(skillTypeIdValue, MidpointRounding.AwayFromZero));
            if (requiredTypeId <= 0 || !skillTypes.TryGetValue(requiredTypeId, out var requiredSkillType))
            {
                continue;
            }

            var requiredLevel = attributes.TryGetValue(levelAttributeId, out var requiredLevelValue)
                ? Math.Clamp(Convert.ToInt32(Math.Round(requiredLevelValue, MidpointRounding.AwayFromZero)), 0, 5)
                : 0;
            if (requiredLevel <= 0)
            {
                continue;
            }

            var requiredSkillName = ResolveEnglishName(requiredSkillType.Name);
            if (string.IsNullOrWhiteSpace(requiredSkillName))
            {
                continue;
            }

            prerequisites.Add(new CharacterProgressionSkillPrerequisite
            {
                SkillKey = CreateSkillKey(requiredSkillName),
                RequiredLevel = requiredLevel
            });
        }

        return prerequisites;
    }

    private static bool TryGetRank(IReadOnlyDictionary<int, double> attributes, out int rank)
    {
        if (attributes.TryGetValue(SkillTimeConstantAttributeId, out var rankValue))
        {
            rank = Convert.ToInt32(Math.Round(rankValue, MidpointRounding.AwayFromZero));
            if (rank > 0)
            {
                return true;
            }
        }

        rank = 0;
        return false;
    }

    private static bool TryMapCharacterAttribute(
        IReadOnlyDictionary<int, double> attributes,
        int trainingAttributeId,
        out CharacterAttributeKind kind)
    {
        if (!attributes.TryGetValue(trainingAttributeId, out var characterAttributeIdValue))
        {
            kind = default;
            return false;
        }

        var characterAttributeId = Convert.ToInt32(Math.Round(characterAttributeIdValue, MidpointRounding.AwayFromZero));
        return CharacterAttributeKindsByDogmaAttributeId.TryGetValue(characterAttributeId, out kind);
    }

    private static string ResolveEnglishName(Dictionary<string, string>? localizedNames)
    {
        if (localizedNames is null || localizedNames.Count == 0)
        {
            return string.Empty;
        }

        if (localizedNames.TryGetValue("en", out var englishName) && !string.IsNullOrWhiteSpace(englishName))
        {
            return englishName.Trim();
        }

        return localizedNames.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string CreateSkillKey(string displayName)
    {
        var buffer = new StringBuilder(displayName.Length);
        var previousWasSeparator = true;

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                buffer.Append('_');
                previousWasSeparator = true;
            }
        }

        var key = buffer.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Unable to derive stable skill key from display name '{displayName}'.");
        }

        return key;
    }

    private static IEnumerable<T> ReadJsonl<T>(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            T? item;
            try
            {
                item = JsonSerializer.Deserialize<T>(line, options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Character progression SDE file was not found: '{path}'.", path);
        }
    }
}

internal static class CharacterProgressionSdePathResolver
{
    private static readonly string[] CandidateRelativeSdeRoots =
    [
        Path.Combine("tests", "fixtures", "character-progression", "minimal-sde"),
        Path.Combine("tests", "fixtures", "fitting-dogma", "minimal-sde")
    ];

    public static string? ResolveSdeRootPath(string? configuredSdeRootPath)
    {
        var resolvedConfiguredPath = ConfiguredPathResolver.ResolveExistingDirectory(
            configuredSdeRootPath,
            "EDENOS_CHARACTER_PROGRESSION_SDE_ROOT");
        if (resolvedConfiguredPath is not null)
        {
            return resolvedConfiguredPath;
        }

        var repoRoot = RepositoryPathResolver.TryResolveRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return null;
        }

        return CandidateRelativeSdeRoots
            .Select(relativePath => Path.Combine(repoRoot, relativePath))
            .FirstOrDefault(Directory.Exists);
    }

}
