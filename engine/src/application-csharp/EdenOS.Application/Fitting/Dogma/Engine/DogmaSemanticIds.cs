namespace EdenOS.Application.Fitting.Dogma.Engine;

public sealed record DogmaSemanticIds
{
    public int DamagePerSecondWithoutReload { get; init; }

    public int DamagePerSecondWithReload { get; init; }

    public int DamageAlpha { get; init; }

    public int DroneDamagePerSecond { get; init; }

    public int ShieldEhp { get; init; }

    public int ArmorEhp { get; init; }

    public int HullEhp { get; init; }

    public int Ehp { get; init; }

    public int CapacitorUsePerSecond { get; init; }

    public int CapacitorPeakDelta { get; init; }

    public int CapacitorPeakDeltaPercentage { get; init; }

    public int CapacitorDepletesIn { get; init; }

    public int PassiveShieldRechargeRate { get; init; }

    public int ShieldBoostRate { get; init; }

    public int ArmorRepairRate { get; init; }

    public int HullRepairRate { get; init; }

    public int CycleTime { get; init; }

    public int ChargeAmount { get; init; }

    public int CpuFree { get; init; }

    public int PowerFree { get; init; }

    public int AlignTime { get; init; }

    public int MissileDamageEffect { get; init; }

    public int VelocityBoostEffect { get; init; }

    public int ModuleBonusAfterburnerEffect { get; init; }

    public int ModuleBonusMicrowarpdriveEffect { get; init; }

    public static DogmaSemanticIds FromMaps(
        IReadOnlyDictionary<string, int> attributeNameToId,
        IReadOnlyDictionary<string, int> effectNameToId)
    {
        return new DogmaSemanticIds
        {
            DamagePerSecondWithoutReload = Resolve(attributeNameToId, "damagePerSecondWithoutReload"),
            DamagePerSecondWithReload = Resolve(attributeNameToId, "damagePerSecondWithReload"),
            DamageAlpha = Resolve(attributeNameToId, "damageAlpha"),
            DroneDamagePerSecond = Resolve(attributeNameToId, "droneDamagePerSecond"),
            ShieldEhp = Resolve(attributeNameToId, "shieldEhp"),
            ArmorEhp = Resolve(attributeNameToId, "armorEhp"),
            HullEhp = Resolve(attributeNameToId, "hullEhp"),
            Ehp = Resolve(attributeNameToId, "ehp"),
            CapacitorUsePerSecond = Resolve(attributeNameToId, "capacitorUsePerSecond"),
            CapacitorPeakDelta = Resolve(attributeNameToId, "capacitorPeakDelta"),
            CapacitorPeakDeltaPercentage = Resolve(attributeNameToId, "capacitorPeakDeltaPercentage"),
            CapacitorDepletesIn = Resolve(attributeNameToId, "capacitorDepletesIn"),
            PassiveShieldRechargeRate = Resolve(attributeNameToId, "passiveShieldRechargeRate"),
            ShieldBoostRate = Resolve(attributeNameToId, "shieldBoostRate"),
            ArmorRepairRate = Resolve(attributeNameToId, "armorRepairRate"),
            HullRepairRate = Resolve(attributeNameToId, "hullRepairRate"),
            CycleTime = Resolve(attributeNameToId, "cycleTime"),
            ChargeAmount = Resolve(attributeNameToId, "chargeAmount"),
            CpuFree = Resolve(attributeNameToId, "cpuFree"),
            PowerFree = Resolve(attributeNameToId, "powerFree"),
            AlignTime = Resolve(attributeNameToId, "alignTime"),
            MissileDamageEffect = Resolve(effectNameToId, "missileDamage"),
            VelocityBoostEffect = Resolve(effectNameToId, "velocityBoost"),
            ModuleBonusAfterburnerEffect = Resolve(effectNameToId, "moduleBonusAfterburner"),
            ModuleBonusMicrowarpdriveEffect = Resolve(effectNameToId, "moduleBonusMicrowarpdrive")
        };
    }

    private static int Resolve(IReadOnlyDictionary<string, int> map, string name) =>
        map.TryGetValue(name, out var id) ? id : 0;
}
