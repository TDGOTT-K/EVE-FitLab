using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.EsfPatch;
using EdenOS.Application.Fitting.Dogma.Rules;
using EdenOS.Application.Fitting.Dogma.Sde;
using EdenOS.Application.Fitting.Dogma.Storage;

namespace EdenOS.Application.Fitting.Dogma.DataSources;

public sealed class JsonlFileDogmaDataSource : IDogmaDataSource
{
    private readonly EsfPatchYamlCatalog patchCatalog;
    private readonly IReadOnlyDictionary<string, int> typeNameToId;
    private readonly IReadOnlyDictionary<string, int> categoryNameToId;
    private readonly IReadOnlyDictionary<string, int> effectNameToId;
    private readonly IReadOnlyDictionary<int, SdeDogmaAttribute> attributeOverrides;
    private readonly IReadOnlyDictionary<int, SdeDogmaEffect> effectOverrides;
    private readonly IReadOnlyDictionary<int, SdeTypeDogma> typeDogmaOverrides;
    private readonly SdeTypeDogmaStore typeDogmaStore;
    private readonly SdeTypeStore typeStore;
    private readonly SdeGroupStore groupStore;
    private readonly SdeDogmaAttributeStore attributeStore;
    private readonly SdeDogmaEffectStore effectStore;
    private readonly Lock patchedTypeDogmaCacheLock = new();
    private readonly Dictionary<int, SdeTypeDogma?> patchedTypeDogmaCache = [];

    public JsonlFileDogmaDataSource(
        string sdeRootPath,
        string dataRootPath,
        string? effectsOverlayJsonPath = null,
        string? ruleSetJsonPath = null)
    {
        var normalizedSdeRoot = Path.GetFullPath(sdeRootPath);
        var normalizedDataRoot = Path.GetFullPath(dataRootPath);
        var buildNumber = ReadBuildNumber(normalizedSdeRoot);
        var idxRoot = Path.Combine(normalizedDataRoot, "idx");
        Directory.CreateDirectory(idxRoot);

        var typeDogmaPath = Path.Combine(normalizedSdeRoot, "typeDogma.jsonl");
        var typesPath = Path.Combine(normalizedSdeRoot, "types.jsonl");
        var groupsPath = Path.Combine(normalizedSdeRoot, "groups.jsonl");
        var attributesPath = Path.Combine(normalizedSdeRoot, "dogmaAttributes.jsonl");
        var effectsPath = Path.Combine(normalizedSdeRoot, "dogmaEffects.jsonl");

        var typeDogmaIndex = LoadOrBuildIndex(typeDogmaPath, idxRoot, "typeDogma.idx", "_key", buildNumber);
        var typesIndex = LoadOrBuildIndex(typesPath, idxRoot, "types.idx", "_key", buildNumber);
        var groupsIndex = LoadOrBuildIndex(groupsPath, idxRoot, "groups.idx", "_key", buildNumber);
        var attributesIndex = LoadOrBuildIndex(attributesPath, idxRoot, "dogmaAttributes.idx", "_key", buildNumber);
        var effectsIndex = LoadOrBuildIndex(effectsPath, idxRoot, "dogmaEffects.idx", "_key", buildNumber);

        var resolvedEffectsOverlayJsonPath = ResolveEffectsOverlayJsonPath(effectsOverlayJsonPath);
        var resolvedRuleSetJsonPath = ResolveRuleSetJsonPath(ruleSetJsonPath, normalizedSdeRoot);
        var resolvedAttributesOverlayJsonPath = ResolveSiblingOverlayJsonPath(resolvedEffectsOverlayJsonPath, "dogmaAttributes.json");
        var resolvedTypeDogmaOverlayJsonPath = ResolveSiblingOverlayJsonPath(resolvedEffectsOverlayJsonPath, "typeDogma.json");
        var hasCompiledEsfOverlay =
            !string.IsNullOrWhiteSpace(resolvedAttributesOverlayJsonPath) &&
            File.Exists(resolvedAttributesOverlayJsonPath) &&
            !string.IsNullOrWhiteSpace(resolvedTypeDogmaOverlayJsonPath) &&
            File.Exists(resolvedTypeDogmaOverlayJsonPath);
        patchCatalog = hasCompiledEsfOverlay
            ? EsfPatchYamlCatalog.Empty("compiled-overlay")
            : EsfPatchYamlCatalog.LoadFromDefaultLocations(normalizedSdeRoot, resolvedEffectsOverlayJsonPath);
        attributeOverrides = DogmaAttributeJsonOverlayStore.Load(resolvedAttributesOverlayJsonPath);
        effectOverrides = DogmaEffectJsonOverlayStore.Load(resolvedEffectsOverlayJsonPath);
        typeDogmaOverrides = TypeDogmaJsonOverlayStore.Load(resolvedTypeDogmaOverlayJsonPath);

        BuildNumber = buildNumber;
        RuleSet = DogmaRuleSetLoader.LoadOrEmpty(resolvedRuleSetJsonPath);
        CacheKey = $"jsonl:{normalizedSdeRoot}|{normalizedDataRoot}|{buildNumber}|effectOverlay:{resolvedEffectsOverlayJsonPath ?? "<none>"}|attributeOverlay:{resolvedAttributesOverlayJsonPath ?? "<none>"}|typeDogmaOverlay:{resolvedTypeDogmaOverlayJsonPath ?? "<none>"}|ruleSet:{resolvedRuleSetJsonPath ?? "<none>"}|patches:{patchCatalog.CacheKey}";
        AttributeNameToId = BuildAttributeNameMap(attributesPath, attributeOverrides, patchCatalog, RuleSet);
        typeNameToId = BuildTypeNameMap(typesPath);
        categoryNameToId = BuildCategoryNameMap(Path.Combine(normalizedSdeRoot, "categories.jsonl"));
        typeDogmaStore = new SdeTypeDogmaStore(typeDogmaPath, typeDogmaIndex);
        typeStore = new SdeTypeStore(typesPath, typesIndex);
        groupStore = new SdeGroupStore(groupsPath, groupsIndex);
        attributeStore = new SdeDogmaAttributeStore(attributesPath, attributesIndex);
        effectStore = new SdeDogmaEffectStore(effectsPath, effectsIndex);
        effectNameToId = BuildEffectNameMap(effectsPath, effectOverrides, patchCatalog, RuleSet);
        EffectNameToId = effectNameToId;
    }

    public string CacheKey { get; }

    public int BuildNumber { get; }

    public IReadOnlyDictionary<string, int> AttributeNameToId { get; }

    public IReadOnlyDictionary<string, int> EffectNameToId { get; }

    public DogmaRuleSet RuleSet { get; }

    public SdeTypeDogma? GetTypeDogma(int typeId)
    {
        lock (patchedTypeDogmaCacheLock)
        {
            if (patchedTypeDogmaCache.TryGetValue(typeId, out var cached))
            {
                return cached;
            }

            typeDogmaOverrides.TryGetValue(typeId, out var overlayTypeDogma);
            var baseTypeDogma = MergeTypeDogma(typeDogmaStore.GetById(typeId), overlayTypeDogma, typeId);
            var type = typeStore.GetById(typeId);
            if (type is null)
            {
                patchedTypeDogmaCache[typeId] = baseTypeDogma;
                return baseTypeDogma;
            }

            var group = groupStore.GetById(type.GroupId);
            var injectedEffects = patchCatalog.ResolveInjectedEffects(
                type,
                group,
                baseTypeDogma,
                typeNameToId,
                categoryNameToId,
                AttributeNameToId,
                effectNameToId);
            if (injectedEffects.Count == 0)
            {
                patchedTypeDogmaCache[typeId] = baseTypeDogma;
                return baseTypeDogma;
            }

            var merged = CloneTypeDogma(baseTypeDogma, typeId);

            var existingEffects = merged.DogmaEffects
                .GroupBy(effect => effect.EffectId)
                .ToDictionary(group => group.Key, group => group.Last());
            foreach (var injectedEffect in injectedEffects)
            {
                if (existingEffects.TryGetValue(injectedEffect.EffectId, out var existing))
                {
                    if (injectedEffect.IsDefault is not null)
                    {
                        existing.IsDefault = injectedEffect.IsDefault;
                    }

                    continue;
                }

                merged.DogmaEffects.Add(new SdeTypeDogmaEffectValue
                {
                    EffectId = injectedEffect.EffectId,
                    IsDefault = injectedEffect.IsDefault
                });
                existingEffects[injectedEffect.EffectId] = merged.DogmaEffects[^1];
            }

            patchedTypeDogmaCache[typeId] = merged;
            return merged;
        }
    }

    public SdeType? GetType(int typeId) => typeStore.GetById(typeId);

    public int? TryResolveTypeIdByName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return typeNameToId.TryGetValue(typeName.Trim(), out var typeId)
            ? typeId
            : null;
    }

    public SdeGroup? GetGroup(int groupId) => groupStore.GetById(groupId);

    public SdeDogmaAttribute? GetAttribute(int attributeId)
    {
        attributeOverrides.TryGetValue(attributeId, out var overlay);
        return DogmaAttributeJsonOverlayStore.Merge(attributeStore.GetById(attributeId), overlay);
    }

    public SdeDogmaEffect? GetEffect(int effectId)
    {
        effectOverrides.TryGetValue(effectId, out var overlay);
        return DogmaEffectJsonOverlayStore.Merge(effectStore.GetById(effectId), overlay);
    }

    private static JsonlOffsetIndex LoadOrBuildIndex(
        string sourceFilePath,
        string idxRoot,
        string indexFileName,
        string idFieldName,
        int buildNumber)
    {
        var indexPath = Path.Combine(idxRoot, indexFileName);
        var index = JsonlOffsetIndex.Load(indexPath, buildNumber, sourceFilePath);
        if (index is not null)
        {
            return index;
        }

        index = JsonlOffsetIndex.BuildIndex(sourceFilePath, idFieldName, buildNumber);
        index.Save(indexPath);
        return index;
    }

    private static Dictionary<string, int> BuildAttributeNameMap(
        string attributesPath,
        IReadOnlyDictionary<int, SdeDogmaAttribute> attributeOverrides,
        EsfPatchYamlCatalog patchCatalog,
        DogmaRuleSet ruleSet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var line in File.ReadLines(attributesPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SdeDogmaAttribute? attribute;
            try
            {
                attribute = JsonSerializer.Deserialize<SdeDogmaAttribute>(line, options);
            }
            catch
            {
                continue;
            }

            if (attribute is null || attribute.AttributeId == 0 || string.IsNullOrWhiteSpace(attribute.Name))
            {
                continue;
            }

            if (!map.ContainsKey(attribute.Name))
            {
                map[attribute.Name] = attribute.AttributeId;
            }
        }

        foreach (var attribute in attributeOverrides.Values.Where(attribute => attribute.AttributeId != 0 && !string.IsNullOrWhiteSpace(attribute.Name)))
        {
            map[attribute.Name] = attribute.AttributeId;
        }

        MergeEsfPatchAliases(map, patchCatalog.AttributeNameToId);
        MergeEsfPatchAliases(map, ruleSet.NamedAttributeIds);
        return map;
    }

    private static Dictionary<string, int> BuildCategoryNameMap(string categoriesPath)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(categoriesPath))
        {
            return map;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var line in File.ReadLines(categoriesPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SdeCategory? category;
            try
            {
                category = JsonSerializer.Deserialize<SdeCategory>(line, options);
            }
            catch
            {
                continue;
            }

            if (category is null || category.CategoryId <= 0 || category.Name is null || category.Name.Count == 0)
            {
                continue;
            }

            if (category.Name.TryGetValue("en", out var englishName) &&
                !string.IsNullOrWhiteSpace(englishName) &&
                !map.ContainsKey(englishName))
            {
                map[englishName] = category.CategoryId;
            }

            foreach (var localizedName in category.Name.Values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!map.ContainsKey(localizedName))
                {
                    map[localizedName] = category.CategoryId;
                }
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildEffectNameMap(
        string effectsPath,
        IReadOnlyDictionary<int, SdeDogmaEffect> effectOverrides,
        EsfPatchYamlCatalog patchCatalog,
        DogmaRuleSet ruleSet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var line in File.ReadLines(effectsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SdeDogmaEffect? effect;
            try
            {
                effect = JsonSerializer.Deserialize<SdeDogmaEffect>(line, options);
            }
            catch
            {
                continue;
            }

            if (effect is null || effect.EffectId == 0 || string.IsNullOrWhiteSpace(effect.Name))
            {
                continue;
            }

            if (!map.ContainsKey(effect.Name))
            {
                map[effect.Name] = effect.EffectId;
            }
        }

        foreach (var effect in effectOverrides.Values.Where(effect => effect.EffectId != 0 && !string.IsNullOrWhiteSpace(effect.Name)))
        {
            map[effect.Name] = effect.EffectId;
        }

        MergeEsfPatchAliases(map, patchCatalog.EffectNameToId);
        MergeEsfPatchAliases(map, ruleSet.NamedEffectIds);
        return map;
    }

    private static Dictionary<string, int> BuildTypeNameMap(string typesPath)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var line in File.ReadLines(typesPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SdeType? type;
            try
            {
                type = JsonSerializer.Deserialize<SdeType>(line, options);
            }
            catch
            {
                continue;
            }

            if (type is null || type.TypeId <= 0 || type.Name is null || type.Name.Count == 0)
            {
                continue;
            }

            if (type.Name.TryGetValue("en", out var englishName) && !string.IsNullOrWhiteSpace(englishName) && !map.ContainsKey(englishName))
            {
                map[englishName] = type.TypeId;
            }

            foreach (var localizedName in type.Name.Values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!map.ContainsKey(localizedName))
                {
                    map[localizedName] = type.TypeId;
                }
            }
        }

        return map;
    }

    private static void MergeEsfPatchAliases(Dictionary<string, int> map, IReadOnlyDictionary<string, int> aliases)
    {
        foreach (var (name, id) in aliases)
        {
            if (id == 0 || string.IsNullOrWhiteSpace(name) || map.ContainsKey(name))
            {
                continue;
            }

            map[name] = id;
        }
    }

    private static SdeTypeDogma? MergeTypeDogma(SdeTypeDogma? baseTypeDogma, SdeTypeDogma? overlayTypeDogma, int typeId)
    {
        if (overlayTypeDogma is null)
        {
            return baseTypeDogma;
        }

        var merged = CloneTypeDogma(baseTypeDogma, typeId);

        var existingAttributes = merged.DogmaAttributes
            .GroupBy(attribute => attribute.AttributeId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (var overlayAttribute in overlayTypeDogma.DogmaAttributes)
        {
            if (existingAttributes.TryGetValue(overlayAttribute.AttributeId, out var existingAttribute))
            {
                existingAttribute.Value = overlayAttribute.Value;
                continue;
            }

            merged.DogmaAttributes.Add(new SdeTypeDogmaAttributeValue
            {
                AttributeId = overlayAttribute.AttributeId,
                Value = overlayAttribute.Value
            });
        }

        var existingEffects = merged.DogmaEffects
            .GroupBy(effect => effect.EffectId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (var overlayEffect in overlayTypeDogma.DogmaEffects)
        {
            if (existingEffects.TryGetValue(overlayEffect.EffectId, out var existingEffect))
            {
                if (overlayEffect.IsDefault is not null)
                {
                    existingEffect.IsDefault = overlayEffect.IsDefault;
                }

                continue;
            }

            merged.DogmaEffects.Add(new SdeTypeDogmaEffectValue
            {
                EffectId = overlayEffect.EffectId,
                IsDefault = overlayEffect.IsDefault
            });
        }

        return merged;
    }

    private static SdeTypeDogma CloneTypeDogma(SdeTypeDogma? source, int typeId)
    {
        return new SdeTypeDogma
        {
            TypeId = source?.TypeId > 0 ? source.TypeId : typeId,
            DogmaAttributes = source?.DogmaAttributes
                .GroupBy(attribute => attribute.AttributeId)
                .Select(group => group.Last())
                .Select(attribute => new SdeTypeDogmaAttributeValue
            {
                AttributeId = attribute.AttributeId,
                Value = attribute.Value
            }).ToList() ?? [],
            DogmaEffects = source?.DogmaEffects
                .GroupBy(effect => effect.EffectId)
                .Select(group => group.Last())
                .Select(effect => new SdeTypeDogmaEffectValue
            {
                EffectId = effect.EffectId,
                IsDefault = effect.IsDefault
            }).ToList() ?? []
        };
    }

    private static int ReadBuildNumber(string sdeRoot)
    {
        var metaPath = Path.Combine(sdeRoot, "_sde.jsonl");
        if (!File.Exists(metaPath))
        {
            return 0;
        }

        using var stream = File.OpenRead(metaPath);
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.TryGetProperty("buildNumber", out var prop) && prop.TryGetInt32(out var buildNumber)
            ? buildNumber
            : 0;
    }

    private static string? ResolveEffectsOverlayJsonPath(string? configuredPath)
    {
        return ConfiguredPathResolver.ResolveOptionalPath(configuredPath, "EDENOS_DOGMA_EFFECTS_OVERLAY_JSON");
    }

    private static string? ResolveRuleSetJsonPath(string? configuredPath, string sdeRoot)
    {
        var resolvedConfiguredPath = ConfiguredPathResolver.ResolveExistingFile(configuredPath, "EDENOS_DOGMA_RULES_JSON");
        if (resolvedConfiguredPath is not null)
        {
            return resolvedConfiguredPath;
        }

        var sdeLocalPath = Path.Combine(sdeRoot, "dogma-rules.json");
        if (File.Exists(sdeLocalPath))
        {
            return sdeLocalPath;
        }

        var repoRoot = RepositoryPathResolver.TryResolveRepositoryRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var repoDefaultPath = Path.Combine(repoRoot, "data", "static-data", "fitting-combat", "dogma-rules.json");
        return File.Exists(repoDefaultPath)
            ? repoDefaultPath
            : null;
    }

    private static string? ResolveSiblingOverlayJsonPath(string? baseOverlayPath, string fileName)
    {
        var normalizedBaseOverlayPath = ConfiguredPathResolver.NormalizeOptionalPath(baseOverlayPath);
        if (normalizedBaseOverlayPath is null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(normalizedBaseOverlayPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate)
            ? candidate
            : null;
    }
}
