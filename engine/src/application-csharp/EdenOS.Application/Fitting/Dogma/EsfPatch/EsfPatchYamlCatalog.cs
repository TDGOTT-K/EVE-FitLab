using System.Globalization;
using EdenOS.Application.Fitting.Dogma.Sde;

namespace EdenOS.Application.Fitting.Dogma.EsfPatch;

public sealed class EsfPatchYamlCatalog
{
    public IReadOnlyDictionary<string, int> AttributeNameToId { get; }
    public IReadOnlyDictionary<string, int> EffectNameToId { get; }
    internal IReadOnlyList<EsfTypeDogmaPatch> TypeDogmaPatches { get; }
    public string CacheKey { get; }

    private EsfPatchYamlCatalog(
        IReadOnlyDictionary<string, int> attributeNameToId,
        IReadOnlyDictionary<string, int> effectNameToId,
        IReadOnlyList<EsfTypeDogmaPatch> typeDogmaPatches,
        string cacheKey)
    {
        AttributeNameToId = attributeNameToId;
        EffectNameToId = effectNameToId;
        TypeDogmaPatches = typeDogmaPatches;
        CacheKey = cacheKey;
    }

    public static EsfPatchYamlCatalog Empty(string cacheKey = "")
    {
        return new EsfPatchYamlCatalog(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            [],
            cacheKey);
    }

    public static EsfPatchYamlCatalog LoadFromDefaultLocations(string sdeRoot, string? effectsOverlayJsonPath = null)
    {
        var candidates = new List<string>();
        var envRoot = ConfiguredPathResolver.ResolveExistingDirectory(null, "EVEANALYZER_ESF_PATCH_ROOT");
        if (envRoot is not null)
        {
            candidates.Add(envRoot);
        }

        var absSdeRoot = Path.GetFullPath(sdeRoot);
        candidates.Add(Path.Combine(absSdeRoot, "patches"));
        candidates.Add(Path.Combine(absSdeRoot, "convert", "patches"));

        var parent = Directory.GetParent(absSdeRoot);
        if (parent is not null)
        {
            candidates.Add(Path.Combine(parent.FullName, "patches"));
        }

        var normalizedEffectsOverlayJsonPath = ConfiguredPathResolver.NormalizeOptionalPath(effectsOverlayJsonPath);
        if (normalizedEffectsOverlayJsonPath is not null)
        {
            var overlayDirectory = Path.GetDirectoryName(normalizedEffectsOverlayJsonPath);
            if (!string.IsNullOrWhiteSpace(overlayDirectory))
            {
                candidates.Add(Path.GetFullPath(Path.Combine(overlayDirectory, "..", "..", "patches")));
            }
        }

        foreach (var root in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var loaded = LoadFromYamlDirectory(root);
            if (loaded.AttributeNameToId.Count > 0 ||
                loaded.EffectNameToId.Count > 0 ||
                loaded.TypeDogmaPatches.Count > 0)
            {
                return loaded;
            }
        }

        return Empty();
    }

    public static EsfPatchYamlCatalog LoadFromYamlDirectory(string patchRoot)
    {
        if (string.IsNullOrWhiteSpace(patchRoot) || !Directory.Exists(patchRoot))
        {
            return Empty();
        }

        var attrs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var effects = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var typeDogmaPatches = new List<EsfTypeDogmaPatch>();

        var files = Directory
            .EnumerateFiles(patchRoot, "*.y*ml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var file in files)
        {
            ParsePatchFile(file, attrs, effects, typeDogmaPatches);
        }

        return new EsfPatchYamlCatalog(attrs, effects, typeDogmaPatches, Path.GetFullPath(patchRoot));
    }

    public IReadOnlyList<SdeTypeDogmaEffectValue> ResolveInjectedEffects(
        SdeType type,
        SdeGroup? group,
        SdeTypeDogma? typeDogma,
        IReadOnlyDictionary<string, int> typeNameToId,
        IReadOnlyDictionary<string, int> categoryNameToId,
        IReadOnlyDictionary<string, int> attributeNameToId,
        IReadOnlyDictionary<string, int> effectNameToId)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(typeNameToId);
        ArgumentNullException.ThrowIfNull(categoryNameToId);
        ArgumentNullException.ThrowIfNull(attributeNameToId);
        ArgumentNullException.ThrowIfNull(effectNameToId);

        if (TypeDogmaPatches.Count == 0)
        {
            return [];
        }

        var existingAttributeIds = new HashSet<int>(typeDogma?.DogmaAttributes.Select(attribute => attribute.AttributeId) ?? []);
        var existingEffectIds = new HashSet<int>(typeDogma?.DogmaEffects.Select(effect => effect.EffectId) ?? []);
        var injected = new List<SdeTypeDogmaEffectValue>();

        foreach (var patch in TypeDogmaPatches)
        {
            if (!patch.Selectors.Any(selector =>
                    MatchesSelector(
                        selector,
                        type,
                        group,
                        existingAttributeIds,
                        existingEffectIds,
                        typeNameToId,
                        categoryNameToId,
                        attributeNameToId,
                        effectNameToId)))
            {
                continue;
            }

            foreach (var effect in patch.Effects)
            {
                if (!effectNameToId.TryGetValue(effect.EffectName, out var effectId))
                {
                    continue;
                }

                if (existingEffectIds.Add(effectId))
                {
                    injected.Add(new SdeTypeDogmaEffectValue
                    {
                        EffectId = effectId,
                        IsDefault = effect.IsDefault
                    });
                }
            }
        }

        return injected;
    }

    private enum Section
    {
        None,
        DogmaAttributes,
        DogmaEffects
    }

    private enum TypeDogmaSubSection
    {
        None,
        Selectors,
        Effects
    }

    private static void ParsePatchFile(
        string filePath,
        Dictionary<string, int> attrs,
        Dictionary<string, int> effects,
        List<EsfTypeDogmaPatch> typeDogmaPatches)
    {
        var lines = File.ReadAllLines(filePath);
        ParseTypeDogmaPatches(lines, typeDogmaPatches);

        Section section = Section.None;
        int? currentId = null;
        string? currentName = null;

        void FlushCurrent()
        {
            if (currentId is null || string.IsNullOrWhiteSpace(currentName))
            {
                currentId = null;
                currentName = null;
                return;
            }

            if (section == Section.DogmaAttributes && !attrs.ContainsKey(currentName))
            {
                attrs[currentName] = currentId.Value;
            }

            if (section == Section.DogmaEffects && !effects.ContainsKey(currentName))
            {
                effects[currentName] = currentId.Value;
            }

            currentId = null;
            currentName = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("attributes:", StringComparison.Ordinal) ||
                line.StartsWith("dogmaAttributes:", StringComparison.Ordinal))
            {
                FlushCurrent();
                section = Section.DogmaAttributes;
                continue;
            }

            if (line.StartsWith("effects:", StringComparison.Ordinal) ||
                line.StartsWith("dogmaEffects:", StringComparison.Ordinal))
            {
                FlushCurrent();
                section = Section.DogmaEffects;
                continue;
            }

            if (line.StartsWith("itemPatch:", StringComparison.Ordinal) ||
                line.StartsWith("effectsPatch:", StringComparison.Ordinal) ||
                line.StartsWith("typeDogma:", StringComparison.Ordinal))
            {
                FlushCurrent();
                section = Section.None;
                continue;
            }

            if (section == Section.None)
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushCurrent();
                line = line[2..].Trim();
            }

            if (TryGetScalar(line, "id", out var idText) &&
                int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                currentId = id;
                continue;
            }

            if (TryGetScalar(line, "name", out var nameText))
            {
                currentName = nameText;
            }
        }

        FlushCurrent();
    }

    private static void ParseTypeDogmaPatches(
        IReadOnlyList<string> lines,
        List<EsfTypeDogmaPatch> typeDogmaPatches)
    {
        var typeDogmaStart = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index].Trim(), "typeDogma:", StringComparison.Ordinal))
            {
                typeDogmaStart = index + 1;
                break;
            }
        }

        if (typeDogmaStart < 0)
        {
            return;
        }

        EsfTypeDogmaPatchBuilder? currentPatch = null;
        EsfTypeDogmaSelectorBuilder? currentSelector = null;
        EsfTypeDogmaEffectRefBuilder? currentEffect = null;
        TypeDogmaSubSection subsection = TypeDogmaSubSection.None;
        string? currentSelectorListKey = null;

        void FlushPatch()
        {
            if (currentPatch is null)
            {
                return;
            }

            var patch = currentPatch.Build();
            if (patch.Selectors.Count > 0 && patch.Effects.Count > 0)
            {
                typeDogmaPatches.Add(patch);
            }

            currentPatch = null;
            currentSelector = null;
            currentEffect = null;
            subsection = TypeDogmaSubSection.None;
            currentSelectorListKey = null;
        }

        for (var index = typeDogmaStart; index < lines.Count; index++)
        {
            var raw = lines[index];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = raw.TakeWhile(char.IsWhiteSpace).Count();
            if (indent == 0 &&
                trimmed.EndsWith(":", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "- patch:", StringComparison.Ordinal))
            {
                break;
            }

            if (indent == 0 && string.Equals(trimmed, "- patch:", StringComparison.Ordinal))
            {
                FlushPatch();
                currentPatch = new EsfTypeDogmaPatchBuilder();
                continue;
            }

            if (currentPatch is null)
            {
                continue;
            }

            if (indent == 2 && string.Equals(trimmed, "dogmaEffects:", StringComparison.Ordinal))
            {
                currentSelector = null;
                currentEffect = null;
                subsection = TypeDogmaSubSection.Effects;
                currentSelectorListKey = null;
                continue;
            }

            if (indent == 2 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentSelectorListKey = null;
                if (subsection != TypeDogmaSubSection.Effects)
                {
                    subsection = TypeDogmaSubSection.Selectors;
                    currentEffect = null;
                    currentSelector = new EsfTypeDogmaSelectorBuilder();
                    currentPatch.Selectors.Add(currentSelector);

                    var selectorHead = trimmed[2..].Trim();
                    if (TryGetScalar(selectorHead, "category", out var categoryName))
                    {
                        currentSelector.CategoryName = categoryName;
                        continue;
                    }

                    if (TryGetScalar(selectorHead, "type", out var typeName))
                    {
                        currentSelector.TypeName = typeName;
                    }

                    continue;
                }

                currentSelector = null;
                currentEffect = new EsfTypeDogmaEffectRefBuilder();
                currentPatch.Effects.Add(currentEffect);

                var effectHead = trimmed[2..].Trim();
                if (TryGetScalar(effectHead, "effect", out var effectName))
                {
                    currentEffect.EffectName = effectName;
                }

                continue;
            }

            if (subsection == TypeDogmaSubSection.Selectors && currentSelector is not null)
            {
                if (indent == 4)
                {
                    if (TryGetScalar(trimmed, "category", out var categoryName))
                    {
                        currentSelector.CategoryName = categoryName;
                        currentSelectorListKey = null;
                        continue;
                    }

                    if (TryGetScalar(trimmed, "type", out var typeName))
                    {
                        currentSelector.TypeName = typeName;
                        currentSelectorListKey = null;
                        continue;
                    }

                    if (string.Equals(trimmed, "hasAllAttributes:", StringComparison.Ordinal))
                    {
                        currentSelectorListKey = "hasAllAttributes";
                        continue;
                    }

                    if (string.Equals(trimmed, "hasAnyAttributes:", StringComparison.Ordinal))
                    {
                        currentSelectorListKey = "hasAnyAttributes";
                        continue;
                    }

                    if (string.Equals(trimmed, "hasAnyEffects:", StringComparison.Ordinal))
                    {
                        currentSelectorListKey = "hasAnyEffects";
                        continue;
                    }
                }

                if (indent >= 6 &&
                    trimmed.StartsWith("- ", StringComparison.Ordinal) &&
                    string.Equals(currentSelectorListKey, "hasAllAttributes", StringComparison.Ordinal) &&
                    TryGetScalar(trimmed[2..].Trim(), "name", out var allAttributeName))
                {
                    currentSelector.HasAllAttributes.Add(allAttributeName);
                    continue;
                }

                if (indent >= 6 &&
                    trimmed.StartsWith("- ", StringComparison.Ordinal) &&
                    string.Equals(currentSelectorListKey, "hasAnyAttributes", StringComparison.Ordinal) &&
                    TryGetScalar(trimmed[2..].Trim(), "name", out var anyAttributeName))
                {
                    currentSelector.HasAnyAttributes.Add(anyAttributeName);
                    continue;
                }

                if (indent >= 6 &&
                    trimmed.StartsWith("- ", StringComparison.Ordinal) &&
                    string.Equals(currentSelectorListKey, "hasAnyEffects", StringComparison.Ordinal) &&
                    TryGetScalar(trimmed[2..].Trim(), "name", out var anyEffectName))
                {
                    currentSelector.HasAnyEffects.Add(anyEffectName);
                    continue;
                }
            }

            if (subsection == TypeDogmaSubSection.Effects && currentEffect is not null)
            {
                if (indent == 4 && TryGetScalar(trimmed, "effect", out var effectName))
                {
                    currentEffect.EffectName = effectName;
                    continue;
                }

                if (indent == 4 &&
                    TryGetScalar(trimmed, "isDefault", out var isDefaultText) &&
                    bool.TryParse(isDefaultText, out var isDefault))
                {
                    currentEffect.IsDefault = isDefault;
                }
            }
        }

        FlushPatch();
    }

    private static bool MatchesSelector(
        EsfTypeDogmaSelector selector,
        SdeType type,
        SdeGroup? group,
        ISet<int> existingAttributeIds,
        ISet<int> existingEffectIds,
        IReadOnlyDictionary<string, int> typeNameToId,
        IReadOnlyDictionary<string, int> categoryNameToId,
        IReadOnlyDictionary<string, int> attributeNameToId,
        IReadOnlyDictionary<string, int> effectNameToId)
    {
        if (!string.IsNullOrWhiteSpace(selector.CategoryName))
        {
            if (group is null ||
                !categoryNameToId.TryGetValue(selector.CategoryName, out var expectedCategoryId) ||
                group.CategoryId != expectedCategoryId)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(selector.TypeName))
        {
            if (!typeNameToId.TryGetValue(selector.TypeName, out var expectedTypeId) || type.TypeId != expectedTypeId)
            {
                return false;
            }
        }

        if (selector.HasAllAttributes.Count > 0)
        {
            foreach (var attributeName in selector.HasAllAttributes)
            {
                if (!attributeNameToId.TryGetValue(attributeName, out var attributeId) || !existingAttributeIds.Contains(attributeId))
                {
                    return false;
                }
            }
        }

        if (selector.HasAnyAttributes.Count > 0)
        {
            var matchedAnyAttribute = selector.HasAnyAttributes
                .Select(attributeName => attributeNameToId.TryGetValue(attributeName, out var attributeId) ? attributeId : 0)
                .Any(attributeId => attributeId > 0 && existingAttributeIds.Contains(attributeId));
            if (!matchedAnyAttribute)
            {
                return false;
            }
        }

        if (selector.HasAnyEffects.Count > 0)
        {
            var matchedAnyEffect = selector.HasAnyEffects
                .Select(effectName => effectNameToId.TryGetValue(effectName, out var effectId) ? effectId : int.MinValue)
                .Any(effectId => effectId != int.MinValue && existingEffectIds.Contains(effectId));
            if (!matchedAnyEffect)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetScalar(string line, string key, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(key + ":", StringComparison.Ordinal))
        {
            return false;
        }

        var raw = line[(key.Length + 1)..].Trim();
        var commentIndex = raw.IndexOf('#');
        if (commentIndex >= 0)
        {
            raw = raw[..commentIndex].Trim();
        }

        raw = raw.Trim();
        if ((raw.StartsWith('"') && raw.EndsWith('"')) || (raw.StartsWith('\'') && raw.EndsWith('\'')))
        {
            raw = raw[1..^1];
        }

        value = raw;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static EsfPatchYamlCatalog Empty() => Empty(string.Empty);

    private sealed class EsfTypeDogmaPatchBuilder
    {
        public List<EsfTypeDogmaSelectorBuilder> Selectors { get; } = [];
        public List<EsfTypeDogmaEffectRefBuilder> Effects { get; } = [];

        public EsfTypeDogmaPatch Build()
        {
            return new EsfTypeDogmaPatch(
                Selectors.Select(selector => selector.Build()).ToArray(),
                Effects.Select(effect => effect.Build()).Where(effect => !string.IsNullOrWhiteSpace(effect.EffectName)).ToArray());
        }
    }

    private sealed class EsfTypeDogmaSelectorBuilder
    {
        public string? CategoryName { get; set; }
        public string? TypeName { get; set; }
        public List<string> HasAllAttributes { get; } = [];
        public List<string> HasAnyAttributes { get; } = [];
        public List<string> HasAnyEffects { get; } = [];

        public EsfTypeDogmaSelector Build()
        {
            return new EsfTypeDogmaSelector(
                CategoryName,
                TypeName,
                HasAllAttributes.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray(),
                HasAnyAttributes.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray(),
                HasAnyEffects.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray());
        }
    }

    private sealed class EsfTypeDogmaEffectRefBuilder
    {
        public string? EffectName { get; set; }
        public bool? IsDefault { get; set; }

        public EsfTypeDogmaEffectRef Build()
        {
            return new EsfTypeDogmaEffectRef(EffectName?.Trim() ?? string.Empty, IsDefault);
        }
    }
}

internal sealed record EsfTypeDogmaPatch(
    IReadOnlyList<EsfTypeDogmaSelector> Selectors,
    IReadOnlyList<EsfTypeDogmaEffectRef> Effects);

internal sealed record EsfTypeDogmaSelector(
    string? CategoryName,
    string? TypeName,
    IReadOnlyList<string> HasAllAttributes,
    IReadOnlyList<string> HasAnyAttributes,
    IReadOnlyList<string> HasAnyEffects);

internal sealed record EsfTypeDogmaEffectRef(
    string EffectName,
    bool? IsDefault);
