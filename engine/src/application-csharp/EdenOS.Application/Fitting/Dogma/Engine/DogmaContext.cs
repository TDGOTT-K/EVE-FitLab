using System.Collections.Concurrent;
using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.Fitting.Dogma.Rules;
using EdenOS.Application.Fitting.Dogma.Sde;

namespace EdenOS.Application.Fitting.Dogma.Engine;

public sealed class DogmaContext
{
    private static readonly ConcurrentDictionary<string, DogmaContext> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDogmaDataSource dataSource;
    private readonly ConcurrentDictionary<int, IReadOnlyDictionary<int, double>> typeAttributeCache = new();
    private readonly ConcurrentDictionary<int, SdeDogmaAttribute?> attributeCache = new();
    private readonly ConcurrentDictionary<int, SdeType?> typeCache = new();
    private readonly ConcurrentDictionary<int, SdeGroup?> groupCache = new();
    private readonly ConcurrentDictionary<int, SdeTypeDogma?> typeDogmaCache = new();
    private readonly IReadOnlyDictionary<string, int> attributeNameToId;
    private readonly IReadOnlyDictionary<string, int> effectNameToId;

    private DogmaContext(IDogmaDataSource dataSource)
    {
        this.dataSource = dataSource;
        attributeNameToId = dataSource.AttributeNameToId;
        effectNameToId = dataSource.EffectNameToId;
        SemanticIds = DogmaSemanticIds.FromMaps(attributeNameToId, effectNameToId);
    }

    public static DogmaContext GetOrCreate(string sdeRoot, string dataRoot) =>
        GetOrCreate(new JsonlFileDogmaDataSource(sdeRoot, dataRoot));

    public static DogmaContext GetOrCreate(IDogmaDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        return Cache.GetOrAdd(dataSource.CacheKey, _ => new DogmaContext(dataSource));
    }

    public double GetTypeAttributeValue(int typeId, string attributeName, double fallback = 0d)
    {
        var type = typeCache.GetOrAdd(typeId, id => dataSource.GetType(id));
        if (type is not null)
        {
            if (string.Equals(attributeName, "mass", StringComparison.OrdinalIgnoreCase) && type.Mass is double massValue)
            {
                return massValue;
            }

            if (string.Equals(attributeName, "capacity", StringComparison.OrdinalIgnoreCase) && type.Capacity is double capacityValue)
            {
                return capacityValue;
            }

            if (string.Equals(attributeName, "volume", StringComparison.OrdinalIgnoreCase) && type.Volume is double volumeValue)
            {
                return volumeValue;
            }

            if (string.Equals(attributeName, "radius", StringComparison.OrdinalIgnoreCase) && type.Radius is double radiusValue)
            {
                return radiusValue;
            }
        }

        if (!attributeNameToId.TryGetValue(attributeName, out var attributeId))
        {
            return fallback;
        }

        return GetTypeAttributeValue(typeId, attributeId, fallback);
    }

    public double GetTypeAttributeValue(int typeId, int attributeId, double fallback = 0d)
    {
        var type = typeCache.GetOrAdd(typeId, id => dataSource.GetType(id));
        var attribute = attributeCache.GetOrAdd(attributeId, id => dataSource.GetAttribute(id));
        if (type is not null && attribute?.Name is { Length: > 0 } attributeName)
        {
            if (string.Equals(attributeName, "mass", StringComparison.OrdinalIgnoreCase) && type.Mass is double massValue)
            {
                return massValue;
            }

            if (string.Equals(attributeName, "capacity", StringComparison.OrdinalIgnoreCase) && type.Capacity is double capacityValue)
            {
                return capacityValue;
            }

            if (string.Equals(attributeName, "volume", StringComparison.OrdinalIgnoreCase) && type.Volume is double volumeValue)
            {
                return volumeValue;
            }

            if (string.Equals(attributeName, "radius", StringComparison.OrdinalIgnoreCase) && type.Radius is double radiusValue)
            {
                return radiusValue;
            }
        }

        var values = typeAttributeCache.GetOrAdd(typeId, BuildTypeAttributeMap);
        if (values.TryGetValue(attributeId, out var value))
        {
            return value;
        }

        if (attribute?.DefaultValue is not null)
        {
            return attribute.DefaultValue.Value;
        }

        return fallback;
    }

    public SdeDogmaEffect? GetEffect(int effectId) => dataSource.GetEffect(effectId);

    public SdeDogmaAttribute? GetAttribute(int attributeId) => attributeCache.GetOrAdd(attributeId, id => dataSource.GetAttribute(id));

    public DogmaRuleSet RuleSet => dataSource.RuleSet;

    public DogmaSemanticIds SemanticIds { get; }

    public int TryGetAttributeId(string attributeName) =>
        attributeNameToId.TryGetValue(attributeName, out var id) ? id : 0;

    public int TryGetEffectId(string effectName) =>
        effectNameToId.TryGetValue(effectName, out var id) ? id : 0;

    public SdeTypeDogma? GetTypeDogma(int typeId) => typeDogmaCache.GetOrAdd(typeId, id => dataSource.GetTypeDogma(id));

    public int GetTypeGroupId(int typeId)
    {
        var type = typeCache.GetOrAdd(typeId, id => dataSource.GetType(id));
        return type?.GroupId ?? 0;
    }

    public string? GetGroupName(int groupId)
    {
        if (groupId <= 0)
        {
            return null;
        }

        var group = groupCache.GetOrAdd(groupId, id => dataSource.GetGroup(id));
        if (group?.Name is null || group.Name.Count == 0)
        {
            return null;
        }

        if (group.Name.TryGetValue("en", out var englishName) && !string.IsNullOrWhiteSpace(englishName))
        {
            return englishName;
        }

        return group.Name.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public string? GetTypeGroupName(int typeId) => GetGroupName(GetTypeGroupId(typeId));

    public double GetTypeMass(int typeId)
    {
        var type = typeCache.GetOrAdd(typeId, id => dataSource.GetType(id));
        return type?.Mass ?? 0d;
    }

    public int GetTypeCategoryId(int typeId)
    {
        var groupId = GetTypeGroupId(typeId);
        if (groupId <= 0)
        {
            return 0;
        }

        var group = groupCache.GetOrAdd(groupId, id => dataSource.GetGroup(id));
        return group?.CategoryId ?? 0;
    }

    private IReadOnlyDictionary<int, double> BuildTypeAttributeMap(int typeId)
    {
        var typeDogma = dataSource.GetTypeDogma(typeId);
        if (typeDogma?.DogmaAttributes is null || typeDogma.DogmaAttributes.Count == 0)
        {
            return new Dictionary<int, double>();
        }

        return typeDogma.DogmaAttributes
            .GroupBy(x => x.AttributeId)
            .ToDictionary(group => group.Key, group => group.Last().Value);
    }
}
