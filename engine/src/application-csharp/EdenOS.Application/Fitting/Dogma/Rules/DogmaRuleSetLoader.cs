using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Rules;

public static class DogmaRuleSetLoader
{
    public static DogmaRuleSet LoadOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return DogmaRuleSet.Empty;
        }

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<DogmaRuleSetDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto is null)
        {
            return DogmaRuleSet.Empty;
        }

        return new DogmaRuleSet
        {
            NamedAttributeIds = ToNameIdMap(dto.NamedAttributeIds),
            NamedEffectIds = ToNameIdMap(dto.NamedEffectIds),
            HighSlot = ToBucket(dto.SlotKinds?.High),
            MidSlot = ToBucket(dto.SlotKinds?.Mid),
            LowSlot = ToBucket(dto.SlotKinds?.Low),
            RigSlot = ToBucket(dto.SlotKinds?.Rig),
            SubsystemSlot = ToBucket(dto.SlotKinds?.Subsystem),
            ServiceSlot = ToBucket(dto.SlotKinds?.Service),
            TurretWeapon = ToBucket(dto.WeaponApplicationKinds?.Turret),
            MissileWeapon = ToBucket(dto.WeaponApplicationKinds?.Missile),
            DroneWeapon = ToBucket(dto.WeaponApplicationKinds?.Drone)
        };
    }

    private static DogmaRuleBucket ToBucket(DogmaRuleBucketDto? dto) =>
        dto is null
            ? DogmaRuleBucket.Empty
            : new DogmaRuleBucket(dto.EffectIds ?? [], dto.EffectNames ?? []);

    private static IReadOnlyDictionary<string, int> ToNameIdMap(Dictionary<string, int>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return source
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value != 0)
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DogmaRuleSetDto
    {
        [JsonPropertyName("named_attribute_ids")]
        public Dictionary<string, int>? NamedAttributeIds { get; init; }

        [JsonPropertyName("named_effect_ids")]
        public Dictionary<string, int>? NamedEffectIds { get; init; }

        [JsonPropertyName("slot_kinds")]
        public DogmaSlotKindsDto? SlotKinds { get; init; }

        [JsonPropertyName("weapon_application_kinds")]
        public DogmaWeaponApplicationKindsDto? WeaponApplicationKinds { get; init; }
    }

    private sealed record DogmaSlotKindsDto
    {
        public DogmaRuleBucketDto? High { get; init; }

        public DogmaRuleBucketDto? Mid { get; init; }

        public DogmaRuleBucketDto? Low { get; init; }

        public DogmaRuleBucketDto? Rig { get; init; }

        public DogmaRuleBucketDto? Subsystem { get; init; }

        public DogmaRuleBucketDto? Service { get; init; }
    }

    private sealed record DogmaWeaponApplicationKindsDto
    {
        public DogmaRuleBucketDto? Turret { get; init; }

        public DogmaRuleBucketDto? Missile { get; init; }

        public DogmaRuleBucketDto? Drone { get; init; }
    }

    private sealed record DogmaRuleBucketDto
    {
        [JsonPropertyName("effect_ids")]
        public IReadOnlyList<int>? EffectIds { get; init; }

        [JsonPropertyName("effect_names")]
        public IReadOnlyList<string>? EffectNames { get; init; }
    }
}
