namespace EdenOS.Application.Fitting.Dogma.Models;

public sealed class FittingSummary
{
    public decimal Volley { get; set; }
    public decimal Dps { get; set; }
    public decimal EffectiveHitPoints { get; set; }
    public decimal ShieldHitPoints { get; set; }
    public decimal ArmorHitPoints { get; set; }
    public decimal StructureHitPoints { get; set; }
    public decimal MaxVelocity { get; set; }
    public decimal CapacitorCapacity { get; set; }
    public decimal CapacitorRechargeSeconds { get; set; }
    public decimal CapacitorUsePerSecond { get; set; }
    public bool CapacitorStable { get; set; }
    public decimal WeaponOptimalRange { get; set; }
    public decimal WeaponTracking { get; set; }
    public FittingOffenseSummary Offense { get; set; } = new();
    public FittingDefenseSummary Defense { get; set; } = new();
    public FittingCapacitorSummary Capacitor { get; set; } = new();
    public FittingMobilitySummary Mobility { get; set; } = new();
    public FittingTargetingSummary Targeting { get; set; } = new();
    public FittingFittingSummary Fitting { get; set; } = new();
    public FittingDroneSummary Drones { get; set; } = new();
    public Dictionary<string, EdenOS.Contracts.Fitting.FitAttributeExecutionTrace> AttributeTraces { get; set; } = new();
    public Dictionary<string, decimal> AttributeSnapshot { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FittingOffenseSummary
{
    public decimal Dps { get; set; }
    public decimal DpsWithReload { get; set; }
    public decimal Alpha { get; set; }
    public decimal DroneDps { get; set; }
    public decimal TurretDamageMultiplier { get; set; }
    public FittingDamageProfileSummary DpsProfile { get; set; } = new();
    public FittingDamageProfileSummary DpsWithReloadProfile { get; set; } = new();
    public FittingDamageProfileSummary AlphaProfile { get; set; } = new();
    public FittingDamageProfileSummary DroneDpsProfile { get; set; } = new();
    public FittingDamageProfileSummary TurretDpsProfile { get; set; } = new();
}

public sealed class FittingDamageProfileSummary
{
    public decimal Em { get; set; }
    public decimal Thermal { get; set; }
    public decimal Kinetic { get; set; }
    public decimal Explosive { get; set; }
}

public sealed class FittingDefenseSummary
{
    public decimal EffectiveHitPoints { get; set; }
    public decimal ShieldHitPoints { get; set; }
    public decimal ArmorHitPoints { get; set; }
    public decimal StructureHitPoints { get; set; }
    public decimal ShieldEmResistPct { get; set; }
    public decimal ShieldThermalResistPct { get; set; }
    public decimal ShieldKineticResistPct { get; set; }
    public decimal ShieldExplosiveResistPct { get; set; }
    public decimal ArmorEmResistPct { get; set; }
    public decimal ArmorThermalResistPct { get; set; }
    public decimal ArmorKineticResistPct { get; set; }
    public decimal ArmorExplosiveResistPct { get; set; }
    public decimal HullEmResistPct { get; set; }
    public decimal HullThermalResistPct { get; set; }
    public decimal HullKineticResistPct { get; set; }
    public decimal HullExplosiveResistPct { get; set; }
    public decimal PassiveShieldRechargeRate { get; set; }
    public decimal ShieldBoostRate { get; set; }
    public decimal ArmorRepairRate { get; set; }
    public decimal HullRepairRate { get; set; }
}

public sealed class FittingCapacitorSummary
{
    public bool Stable { get; set; }
    public decimal DepletesInSeconds { get; set; }
    public decimal Capacity { get; set; }
    public decimal RechargeSeconds { get; set; }
    public decimal PeakDelta { get; set; }
    public decimal PeakDeltaPercentage { get; set; }
    public decimal UsePerSecond { get; set; }
    public decimal WeaponUsePerSecond { get; set; }
    public decimal ActiveTankUsePerSecond { get; set; }
}

public sealed class FittingMobilitySummary
{
    public decimal MaxVelocity { get; set; }
    public decimal WarpSpeedMultiplier { get; set; }
    public decimal InertiaModifier { get; set; }
    public decimal AlignTimeSeconds { get; set; }
}

public sealed class FittingTargetingSummary
{
    public decimal MaxTargetRange { get; set; }
    public decimal ScanResolution { get; set; }
    public decimal MaxLockedTargets { get; set; }
    public decimal SignatureRadius { get; set; }
}

public sealed class FittingFittingSummary
{
    public decimal CpuOutput { get; set; }
    public decimal CpuLoad { get; set; }
    public decimal PowerOutput { get; set; }
    public decimal PowerLoad { get; set; }
    public decimal Calibration { get; set; }
    public decimal CalibrationLoad { get; set; }
}

public sealed class FittingDroneSummary
{
    public decimal Bandwidth { get; set; }
    public decimal BandwidthLoad { get; set; }
    public decimal Capacity { get; set; }
    public decimal CapacityLoad { get; set; }
}
