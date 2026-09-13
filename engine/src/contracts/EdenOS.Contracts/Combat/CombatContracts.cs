using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Combat;

public enum CombatTerminationReason
{
    AttackerDestroyed,
    DefenderDestroyed,
    BothDestroyed,
    MaxTicksReached
}

public sealed record CombatShipStateView
{
    public required string FitId { get; init; }

    public int LaunchedDroneCount { get; init; }

    public int DronesInBayCount { get; init; }

    public int LostDroneCount { get; init; }

    public decimal DroneBandwidthUsed { get; init; }

    public decimal DroneBandwidthAvailable { get; init; }

    public decimal DroneBayUsed { get; init; }

    public decimal DroneBayAvailable { get; init; }

    public decimal ShieldHitpointsRemaining { get; init; }

    public decimal ArmorHitpointsRemaining { get; init; }

    public decimal StructureHitpointsRemaining { get; init; }

    public decimal CapacitorRemaining { get; init; }

    public bool TurretWeaponsSuppressedByCapacitor { get; init; }

    public bool ActiveTankSuppressedByCapacitor { get; init; }

    public bool IsDestroyed { get; init; }
}

public sealed record DamageEvent
{
    public int TickNumber { get; init; }

    public required string SourceFitId { get; init; }

    public required string TargetFitId { get; init; }

    public decimal RawDamage { get; init; }

    public DamageProfile RawDamageProfile { get; init; } = new();

    public decimal ApplicationMultiplier { get; init; } = 1m;

    public decimal TurretApplicationMultiplier { get; init; } = 1m;

    public decimal MissileApplicationMultiplier { get; init; } = 1m;

    public decimal DroneApplicationMultiplier { get; init; } = 1m;

    public decimal ApplicationAdjustedDamage { get; init; }

    public DamageProfile ApplicationAdjustedDamageProfile { get; init; } = new();

    public decimal MitigatedDamage { get; init; }

    public DamageProfile MitigatedDamageProfile { get; init; } = new();

    public decimal AppliedDamage { get; init; }

    public DamageProfile AppliedDamageProfile { get; init; } = new();

    public decimal DamageToShield { get; init; }

    public DamageProfile DamageToShieldProfile { get; init; } = new();

    public decimal DamageToArmor { get; init; }

    public DamageProfile DamageToArmorProfile { get; init; } = new();

    public decimal DamageToStructure { get; init; }

    public DamageProfile DamageToStructureProfile { get; init; } = new();

    public decimal CapacitorSpent { get; init; }

    public int WeaponActivations { get; init; }

    public int WeaponSystemsSuppressedByCapacitorCount { get; init; }

    public int WeaponSystemsReloadingCount { get; init; }

    public int DroneActivations { get; init; }

    public int DroneUnitsAttacking { get; init; }

    public bool TurretWeaponsSuppressedByCapacitor { get; init; }

    public decimal TargetTotalHitpointsRemaining { get; init; }

    public string? Notes { get; init; }
}

public sealed record CombatShipEngagementProfile
{
    public decimal RangeMeters { get; init; }

    public decimal AngularVelocityRadiansPerSecond { get; init; }

    public decimal RelativeVelocityMetersPerSecond { get; init; }

    public decimal? SignatureRadiusOverrideMeters { get; init; }

    public decimal ManualApplicationMultiplier { get; init; } = 1m;
}

public enum DroneEventKind
{
    Recalled,
    Destroyed,
    LostOnShipDestruction
}

public sealed record DroneEvent
{
    public int TickNumber { get; init; }

    public required string FitId { get; init; }

    public required string DroneTypeId { get; init; }

    public required string DroneName { get; init; }

    public required DroneEventKind Kind { get; init; }

    public int QuantityChanged { get; init; }

    public int LaunchedBefore { get; init; }

    public int LaunchedAfter { get; init; }

    public int BayBefore { get; init; }

    public int BayAfter { get; init; }

    public int LostBefore { get; init; }

    public int LostAfter { get; init; }

    public string? Notes { get; init; }
}

public sealed record TankEvent
{
    public int TickNumber { get; init; }

    public required string FitId { get; init; }

    public decimal ShieldRepaired { get; init; }

    public decimal ArmorRepaired { get; init; }

    public decimal StructureRepaired { get; init; }

    public decimal PassiveShieldRepaired { get; init; }

    public decimal ActiveShieldRepaired { get; init; }

    public decimal CapacitorSpent { get; init; }

    public decimal CapacitorRecharged { get; init; }

    public decimal CapacitorBefore { get; init; }

    public decimal CapacitorAfter { get; init; }

    public int ActiveTankActivations { get; init; }

    public int ActiveTankSystemsSuppressedByCapacitorCount { get; init; }

    public bool ActiveTankSuppressedByCapacitor { get; init; }

    public string? Notes { get; init; }
}

public sealed record CombatTick
{
    public int TickNumber { get; init; }

    public decimal ElapsedSeconds { get; init; }

    public IReadOnlyList<DamageEvent> DamageEvents { get; init; } = Array.Empty<DamageEvent>();

    public IReadOnlyList<TankEvent> TankEvents { get; init; } = Array.Empty<TankEvent>();

    public IReadOnlyList<DroneEvent> DroneEvents { get; init; } = Array.Empty<DroneEvent>();

    public required CombatShipStateView AttackerState { get; init; }

    public required CombatShipStateView DefenderState { get; init; }
}

public sealed record CombatScenario
{
    public string? ScenarioId { get; init; }

    public required FitSnapshot AttackerFit { get; init; }

    public required FitSnapshot DefenderFit { get; init; }

    public decimal TickDurationSeconds { get; init; } = 1m;

    public int MaxTicks { get; init; } = 120;

    public CombatShipEngagementProfile AttackerEngagement { get; init; } = new();

    public CombatShipEngagementProfile DefenderEngagement { get; init; } = new();

    public string? Notes { get; init; }
}

public sealed record CombatOutcome
{
    public required CombatScenario Scenario { get; init; }

    public string? WinnerFitId { get; init; }

    public required CombatTerminationReason TerminationReason { get; init; }

    public int TicksElapsed { get; init; }

    public required FitAttributeView AttackerAttributes { get; init; }

    public required FitAttributeView DefenderAttributes { get; init; }

    public required CombatShipStateView AttackerFinalState { get; init; }

    public required CombatShipStateView DefenderFinalState { get; init; }

    public IReadOnlyList<CombatTick> Ticks { get; init; } = Array.Empty<CombatTick>();

    public IReadOnlyList<string> ApproximationNotes { get; init; } = Array.Empty<string>();
}

public sealed record SimulateDuelRequest
{
    public required CombatScenario Scenario { get; init; }
}

public interface ICombatSimulationService
{
    UseCaseResult<CombatOutcome> SimulateDuel(SimulateDuelRequest request);
}
