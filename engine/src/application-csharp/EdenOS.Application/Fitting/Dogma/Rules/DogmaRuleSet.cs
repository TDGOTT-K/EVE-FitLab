using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting.Dogma.Rules;

public sealed class DogmaRuleSet
{
    public static DogmaRuleSet Empty { get; } = new();

    public IReadOnlyDictionary<string, int> NamedAttributeIds { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> NamedEffectIds { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public DogmaRuleBucket HighSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket MidSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket LowSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket RigSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket SubsystemSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket ServiceSlot { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket TurretWeapon { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket MissileWeapon { get; init; } = DogmaRuleBucket.Empty;

    public DogmaRuleBucket DroneWeapon { get; init; } = DogmaRuleBucket.Empty;

    public ModuleSlotKind? ResolveSlotKind(IEnumerable<DogmaResolvedEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (HighSlot.Matches(effect))
            {
                return ModuleSlotKind.High;
            }

            if (MidSlot.Matches(effect))
            {
                return ModuleSlotKind.Mid;
            }

            if (LowSlot.Matches(effect))
            {
                return ModuleSlotKind.Low;
            }

            if (RigSlot.Matches(effect))
            {
                return ModuleSlotKind.Rig;
            }

            if (SubsystemSlot.Matches(effect))
            {
                return ModuleSlotKind.Subsystem;
            }

            if (ServiceSlot.Matches(effect))
            {
                return ModuleSlotKind.Service;
            }
        }

        return null;
    }

    public WeaponApplicationKind ResolveWeaponApplicationKind(IEnumerable<DogmaResolvedEffect> effects)
    {
        var matchesMissile = false;
        var matchesTurret = false;
        var matchesDrone = false;

        foreach (var effect in effects)
        {
            if (MissileWeapon.Matches(effect))
            {
                matchesMissile = true;
            }

            if (TurretWeapon.Matches(effect))
            {
                matchesTurret = true;
            }

            if (DroneWeapon.Matches(effect))
            {
                matchesDrone = true;
            }
        }

        if (matchesMissile)
        {
            return WeaponApplicationKind.Missile;
        }

        if (matchesTurret)
        {
            return WeaponApplicationKind.Turret;
        }

        if (matchesDrone)
        {
            return WeaponApplicationKind.Drone;
        }

        return WeaponApplicationKind.Direct;
    }

    public int ResolveNamedAttributeId(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return 0;
        }

        return NamedAttributeIds.TryGetValue(attributeName.Trim(), out var id)
            ? id
            : 0;
    }

    public int ResolveNamedEffectId(string effectName)
    {
        if (string.IsNullOrWhiteSpace(effectName))
        {
            return 0;
        }

        return NamedEffectIds.TryGetValue(effectName.Trim(), out var id)
            ? id
            : 0;
    }
}

public sealed class DogmaRuleBucket
{
    public static DogmaRuleBucket Empty { get; } = new([], []);

    public DogmaRuleBucket(
        IEnumerable<int> effectIds,
        IEnumerable<string> effectNames)
    {
        EffectIds = new HashSet<int>(effectIds.Where(id => id > 0));
        EffectNames = new HashSet<string>(
            effectNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<int> EffectIds { get; }

    public IReadOnlySet<string> EffectNames { get; }

    public bool Matches(DogmaResolvedEffect effect)
    {
        if (EffectIds.Contains(effect.EffectId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(effect.EffectName) && EffectNames.Contains(effect.EffectName);
    }
}

public readonly record struct DogmaResolvedEffect(int EffectId, string? EffectName);
