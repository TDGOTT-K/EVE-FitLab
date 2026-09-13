using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Fitting;

public enum ModuleSlotKind
{
    High,
    Mid,
    Low,
    Rig,
    Subsystem,
    Service
}

public enum FittingItemState
{
    Offline,
    Passive,
    Online,
    Active,
    Overload
}

public enum FitTextFormat
{
    Eft
}

public enum WeaponApplicationKind
{
    Direct,
    Turret,
    Missile,
    Drone
}

public enum FitSimulationMode
{
    SingleShip,
    SymmetricCapChain
}

public sealed record DamageProfile
{
    public decimal Em { get; init; }

    public decimal Thermal { get; init; }

    public decimal Kinetic { get; init; }

    public decimal Explosive { get; init; }
}

public sealed record ResistanceProfile
{
    public decimal EmPercent { get; init; }

    public decimal ThermalPercent { get; init; }

    public decimal KineticPercent { get; init; }

    public decimal ExplosivePercent { get; init; }
}

public sealed record DamageTypeProjection
{
    public decimal Em { get; init; }

    public decimal Thermal { get; init; }

    public decimal Kinetic { get; init; }

    public decimal Explosive { get; init; }

    public decimal Omni { get; init; }
}

public sealed record ModuleSlot
{
    public required string SlotId { get; init; }

    public required ModuleSlotKind Kind { get; init; }

    public int Index { get; init; }

    public string? Label { get; init; }
}

public sealed record ShipHull
{
    public required string HullId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public IReadOnlyList<ModuleSlot> Slots { get; init; } = Array.Empty<ModuleSlot>();

    public decimal PowergridOutput { get; init; }

    public decimal CpuOutput { get; init; }

    public decimal ShieldHitpoints { get; init; }

    public ResistanceProfile ShieldResistances { get; init; } = new();

    public decimal ArmorHitpoints { get; init; }

    public ResistanceProfile ArmorResistances { get; init; } = new();

    public decimal StructureHitpoints { get; init; }

    public ResistanceProfile StructureResistances { get; init; } = new();

    public decimal CapacitorCapacity { get; init; }

    public decimal CapacitorRechargeSeconds { get; init; }

    public decimal MaxVelocity { get; init; }

    public decimal SignatureRadius { get; init; }

    public decimal MaxTargetRange { get; init; }

    public decimal TurretDamageMultiplier { get; init; } = 1m;
}

public sealed record Charge
{
    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();
    public required string ChargeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public decimal DamagePerSecondBonus { get; init; }

    public DamageProfile DamageProfileBonus { get; init; } = new();

    public decimal VolleyDamageBonus { get; init; }

    public DamageProfile VolleyDamageProfileBonus { get; init; } = new();

    public decimal TurretDamageMultiplierBonus { get; init; }

    public decimal CapacitorUsagePerSecond { get; init; }

    public decimal OptimalRangeBonusMeters { get; init; }

    public decimal MissileExplosionRadiusMeters { get; init; }

    public decimal MissileExplosionVelocityMetersPerSecond { get; init; }

    public decimal MissileDamageReductionFactor { get; init; }

    public decimal MissileDamageReductionSensitivity { get; init; }
}

public sealed record FittedModule
{
    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public required string SlotId { get; init; }

    public required ModuleSlotKind SlotKind { get; init; }

    public FittingItemState State { get; init; } = FittingItemState.Active;

    public decimal PowergridUsage { get; init; }

    public decimal CpuUsage { get; init; }

    public decimal DamagePerSecond { get; init; }

    public DamageProfile DamageProfilePerSecond { get; init; } = new();

    public decimal VolleyDamage { get; init; }

    public DamageProfile VolleyDamageProfile { get; init; } = new();

    public decimal CycleTimeSeconds { get; init; }

    public decimal ReloadTimeSeconds { get; init; }

    public int MagazineCapacity { get; init; }

    public int ChargeUnitsPerCycle { get; init; } = 1;

    public decimal ShieldHitpointsBonus { get; init; }

    public ResistanceProfile ShieldResistancesBonus { get; init; } = new();

    public decimal ArmorHitpointsBonus { get; init; }

    public ResistanceProfile ArmorResistancesBonus { get; init; } = new();

    public decimal StructureHitpointsBonus { get; init; }

    public ResistanceProfile StructureResistancesBonus { get; init; } = new();

    public decimal CapacitorBonus { get; init; }

    public decimal SpeedBonus { get; init; }

    public decimal TurretDamageMultiplierBonus { get; init; }

    public decimal ShieldRepairPerSecond { get; init; }

    public decimal ArmorRepairPerSecond { get; init; }

    public decimal StructureRepairPerSecond { get; init; }

    public decimal CapacitorUsagePerSecond { get; init; }

    public decimal CapacitorTransferPerSecond { get; init; }

    public decimal OptimalRangeMeters { get; init; }

    public decimal FalloffRangeMeters { get; init; }

    public decimal TrackingSpeed { get; init; }

    public decimal SignatureResolutionMeters { get; init; }

    public WeaponApplicationKind ApplicationKind { get; init; } = WeaponApplicationKind.Direct;

    public decimal MissileExplosionRadiusMeters { get; init; }

    public decimal MissileExplosionVelocityMetersPerSecond { get; init; }

    public decimal MissileDamageReductionFactor { get; init; }

    public decimal MissileDamageReductionSensitivity { get; init; }

    public Charge? Charge { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

public sealed record Rig
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public required string SlotId { get; init; }

    public decimal PowergridUsage { get; init; }

    public decimal CpuUsage { get; init; }

    public decimal ShieldHitpointsBonus { get; init; }

    public ResistanceProfile ShieldResistancesBonus { get; init; } = new();

    public decimal ArmorHitpointsBonus { get; init; }

    public ResistanceProfile ArmorResistancesBonus { get; init; } = new();

    public decimal StructureHitpointsBonus { get; init; }

    public ResistanceProfile StructureResistancesBonus { get; init; } = new();

    public decimal CapacitorBonus { get; init; }

    public decimal SpeedBonus { get; init; }

    public decimal TurretDamageMultiplierBonus { get; init; }
}

public sealed record DroneStack
{
    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();
    public IReadOnlyDictionary<string, decimal> AttributeSnapshot { get; init; } = new Dictionary<string, decimal>();
    public required string DroneTypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public int Quantity { get; init; }

    public int? BayQuantity { get; init; }

    public FittingItemState State { get; init; } = FittingItemState.Active;

    public decimal DamagePerSecond { get; init; }

    public DamageProfile DamageProfilePerSecond { get; init; } = new();

    public decimal VolleyDamage { get; init; }

    public DamageProfile VolleyDamageProfile { get; init; } = new();

    public decimal CycleTimeSeconds { get; init; }

    public decimal BandwidthPerUnit { get; init; }

    public decimal VolumePerUnit { get; init; }

    public decimal OptimalRangeMeters { get; init; }

    public decimal FalloffRangeMeters { get; init; }

    public decimal TrackingSpeed { get; init; }

    public decimal SignatureResolutionMeters { get; init; }
}

public sealed record DroneBay
{
    public decimal Capacity { get; init; }

    public decimal Bandwidth { get; init; }

    public IReadOnlyList<DroneStack> Drones { get; init; } = Array.Empty<DroneStack>();
}

public sealed record FitCargoEntry
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public int Quantity { get; init; }
}

public sealed record FitSkillLevel
{
    public int SkillTypeId { get; init; }

    public int Level { get; init; }
}

public sealed record FitImplant
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public int? SlotIndex { get; init; }

    public FittingItemState State { get; init; } = FittingItemState.Passive;
}

public sealed record FitBoosterSideEffect
{
    public int EffectId { get; init; }

    public bool Active { get; init; }
}

public sealed record FitBooster
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public int? BoosterSlot { get; init; }

    public FittingItemState State { get; init; } = FittingItemState.Active;

    public IReadOnlyList<FitBoosterSideEffect> SideEffects { get; init; } = Array.Empty<FitBoosterSideEffect>();
}

public sealed record FitSnapshot
{
    public required string FitId { get; init; }

    public string? Name { get; init; }

    public required ShipHull ShipHull { get; init; }

    public IReadOnlyList<FittedModule> Modules { get; init; } = Array.Empty<FittedModule>();

    public IReadOnlyList<Rig> Rigs { get; init; } = Array.Empty<Rig>();

    public DroneBay DroneBay { get; init; } = new();

    public IReadOnlyList<FitCargoEntry> Cargo { get; init; } = Array.Empty<FitCargoEntry>();

    public IReadOnlyList<FitSkillLevel> Skills { get; init; } = Array.Empty<FitSkillLevel>();

    public IReadOnlyList<FitImplant> Implants { get; init; } = Array.Empty<FitImplant>();

    public IReadOnlyList<FitBooster> Boosters { get; init; } = Array.Empty<FitBooster>();

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record FitSlotUsageView
{
    public required ModuleSlotKind Kind { get; init; }

    public int Used { get; init; }

    public int Available { get; init; }
}

public sealed record FitAmmoOption
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public int? DogmaTypeId { get; init; }

    public int CargoQuantity { get; init; }
}

public sealed record FitAmmoSelectionPrompt
{
    public required string SlotId { get; init; }

    public required string ModuleTypeId { get; init; }

    public required string ModuleName { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<FitAmmoOption> Options { get; init; } = Array.Empty<FitAmmoOption>();
}

public sealed record FitAttributeView
{
    public required string FitId { get; init; }

    public required string HullId { get; init; }

    public decimal PowergridUsed { get; init; }

    public decimal PowergridAvailable { get; init; }

    public decimal CpuUsed { get; init; }

    public decimal? CalibrationUsed { get; init; }

    public decimal? CalibrationAvailable { get; init; }

    public int? TurretHardpointsUsed { get; init; }
    public decimal? TurretHardpointsAvailable { get; init; }
    public int? LauncherHardpointsUsed { get; init; }
    public decimal? LauncherHardpointsAvailable { get; init; }

    public decimal CpuAvailable { get; init; }

    public decimal DroneBandwidthUsed { get; init; }

    public decimal DroneBandwidthAvailable { get; init; }

    public decimal DroneBayUsed { get; init; }

    public decimal DroneBayAvailable { get; init; }

    public decimal ShieldHitpoints { get; init; }

    public ResistanceProfile ShieldResistances { get; init; } = new();

    public decimal ArmorHitpoints { get; init; }

    public ResistanceProfile ArmorResistances { get; init; } = new();

    public decimal StructureHitpoints { get; init; }

    public ResistanceProfile StructureResistances { get; init; } = new();

    public decimal TotalHitpoints { get; init; }

    public decimal ShieldEffectiveHitpointsOmni { get; init; }

    public DamageTypeProjection ShieldEffectiveHitpointsByDamageType { get; init; } = new();

    public decimal ArmorEffectiveHitpointsOmni { get; init; }

    public DamageTypeProjection ArmorEffectiveHitpointsByDamageType { get; init; } = new();

    public decimal StructureEffectiveHitpointsOmni { get; init; }

    public DamageTypeProjection StructureEffectiveHitpointsByDamageType { get; init; } = new();

    public decimal TotalEffectiveHitpointsOmni { get; init; }

    public DamageTypeProjection TotalEffectiveHitpointsByDamageType { get; init; } = new();

    public decimal CapacitorCapacity { get; init; }

    public decimal CapacitorRechargeSeconds { get; init; }

    public decimal CapacitorUsagePerSecond { get; init; }

    public decimal WeaponCapacitorUsagePerSecond { get; init; }

    public decimal ActiveTankCapacitorUsagePerSecond { get; init; }

    public decimal PeakCapacitorRechargePerSecond { get; init; }

    public bool CapacitorStable { get; init; }

    public decimal? CapacitorDepletionSeconds { get; init; }

    public decimal MaxVelocity { get; init; }

    public decimal SignatureRadius { get; init; }

    public decimal MaxTargetRange { get; init; }

    public decimal TurretDamageMultiplier { get; init; }

    public decimal AppliedDamagePerSecond { get; init; }

    public DamageProfile AppliedDamageProfilePerSecond { get; init; } = new();

    public decimal AppliedTurretDamagePerSecond { get; init; }

    public DamageProfile AppliedTurretDamageProfilePerSecond { get; init; } = new();

    public decimal AppliedDroneDamagePerSecond { get; init; }

    public DamageProfile AppliedDroneDamageProfilePerSecond { get; init; } = new();

    public decimal DamagePerSecondWithReload { get; init; }

    public decimal VolleyDamage { get; init; }

    public DamageProfile VolleyDamageProfile { get; init; } = new();

    public decimal WeaponOptimalRangeMeters { get; init; }

    public decimal WeaponFalloffRangeMeters { get; init; }

    public decimal WeaponTracking { get; init; }

    public decimal WeaponSignatureResolutionMeters { get; init; }

    public decimal MissileExplosionRadiusMeters { get; init; }

    public decimal MissileExplosionVelocityMetersPerSecond { get; init; }

    public decimal MissileDamageReductionFactor { get; init; }

    public decimal MissileDamageReductionSensitivity { get; init; }

    public decimal PassiveShieldRechargePerSecond { get; init; }

    public decimal ShieldRepairPerSecond { get; init; }

    public decimal ArmorRepairPerSecond { get; init; }

    public decimal StructureRepairPerSecond { get; init; }

    public decimal RemoteShieldRepairPerSecond { get; init; }

    public decimal RemoteArmorRepairPerSecond { get; init; }

    public decimal RemoteStructureRepairPerSecond { get; init; }

    public decimal RemoteCapacitorTransferPerSecond { get; init; }

    public DamageTypeProjection EffectiveRepairPerSecondByDamageType { get; init; } = new();

    public IReadOnlyList<FitSlotUsageView> SlotUsage { get; init; } = Array.Empty<FitSlotUsageView>();

    public IReadOnlyList<FitAmmoSelectionPrompt> AmmoSelectionPrompts { get; init; } = Array.Empty<FitAmmoSelectionPrompt>();

    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();

    public IReadOnlyDictionary<string, decimal> AttributeSnapshot { get; init; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ApproximationNotes { get; init; } = Array.Empty<string>();
}

public sealed record FitValidationIssue
{
    public required string Code { get; init; }

    public required string Category { get; init; }

    public required string Message { get; init; }

    public string? RelatedSlotId { get; init; }

    public string? RelatedTypeId { get; init; }
}

public sealed record FitValidationResult
{
    public required string FitId { get; init; }

    public bool IsValid { get; init; }

    public required FitSnapshot Snapshot { get; init; }

    public required FitAttributeView Attributes { get; init; }

    public IReadOnlyList<FitValidationIssue> Issues { get; init; } = Array.Empty<FitValidationIssue>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record CreateOrUpdateFitRequest
{
    public required FitSnapshot Snapshot { get; init; }

    public FitSimulationMode SimulationMode { get; init; } = FitSimulationMode.SingleShip;
}

public sealed record ValidateFitRequest
{
    public required FitSnapshot Snapshot { get; init; }

    public FitSimulationMode SimulationMode { get; init; } = FitSimulationMode.SingleShip;
}

public sealed record GetFitAttributesRequest
{
    public required FitSnapshot Snapshot { get; init; }

    public FitSimulationMode SimulationMode { get; init; } = FitSimulationMode.SingleShip;
}

public sealed record ImportFitTextRequest
{
    public required string Text { get; init; }

    public FitTextFormat Format { get; init; } = FitTextFormat.Eft;

    public string? FitId { get; init; }

    public string? Locale { get; init; }
}

public sealed record ImportedFitTextView
{
    public required FitTextFormat Format { get; init; }

    public required FitSnapshot Snapshot { get; init; }

    public string? Locale { get; init; }

    public IReadOnlyList<FitAmmoSelectionPrompt> AmmoSelectionPrompts { get; init; } = Array.Empty<FitAmmoSelectionPrompt>();
}

public sealed record ExportFitTextRequest
{
    public required FitSnapshot Snapshot { get; init; }

    public FitTextFormat Format { get; init; } = FitTextFormat.Eft;

    public bool IncludeEmptySlots { get; init; } = true;

    public string? Locale { get; init; }
}

public sealed record ExportedFitTextView
{
    public required string FitId { get; init; }

    public required FitTextFormat Format { get; init; }

    public required string Text { get; init; }

    public string? Locale { get; init; }
}

public sealed record SelectFitAmmoRequest
{
    public required FitSnapshot Snapshot { get; init; }

    public int? ChargeDogmaTypeId { get; init; }

    public string? ChargeTypeId { get; init; }

    public string? ChargeName { get; init; }

    public IReadOnlyList<string> SlotIds { get; init; } = Array.Empty<string>();

    public bool ApplyToAllCompatibleSlots { get; init; } = true;

    public string? Locale { get; init; }
}

public sealed record FitAmmoSelectionResult
{
    public required FitSnapshot Snapshot { get; init; }

    public required string SelectedChargeTypeId { get; init; }

    public required string SelectedChargeName { get; init; }

    public int? SelectedChargeDogmaTypeId { get; init; }

    public IReadOnlyList<string> UpdatedSlotIds { get; init; } = Array.Empty<string>();

    public string? Locale { get; init; }

    public IReadOnlyList<FitAmmoSelectionPrompt> RemainingAmmoSelectionPrompts { get; init; } = Array.Empty<FitAmmoSelectionPrompt>();
}

public sealed record FitEnvironmentProfile
{
    public required string ProfileId { get; init; }

    public required string Name { get; init; }

    public string? WorkspaceId { get; init; }

    public string? CharacterId { get; init; }

    public string? CharacterName { get; init; }

    public IReadOnlyList<FitSkillLevel> Skills { get; init; } = Array.Empty<FitSkillLevel>();

    public IReadOnlyList<FitImplant> Implants { get; init; } = Array.Empty<FitImplant>();

    public IReadOnlyList<FitBooster> Boosters { get; init; } = Array.Empty<FitBooster>();

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record FitEnvironmentProfileSummary
{
    public required string ProfileId { get; init; }

    public required string Name { get; init; }

    public string? WorkspaceId { get; init; }

    public string? CharacterId { get; init; }

    public string? CharacterName { get; init; }

    public int SkillCount { get; init; }

    public int ImplantCount { get; init; }

    public int BoosterCount { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record FitEnvironmentProfileCatalogView
{
    public IReadOnlyList<FitEnvironmentProfileSummary> Profiles { get; init; } = Array.Empty<FitEnvironmentProfileSummary>();
}

public sealed record SaveFitEnvironmentProfileRequest
{
    public required FitEnvironmentProfile Profile { get; init; }

    public bool Overwrite { get; init; } = true;
}

public sealed record GetFitEnvironmentProfileRequest
{
    public required string ProfileId { get; init; }
}

public sealed record ListFitEnvironmentProfilesRequest
{
    public string? WorkspaceId { get; init; }

    public string? CharacterId { get; init; }
}

public sealed record DeleteFitEnvironmentProfileRequest
{
    public required string ProfileId { get; init; }
}

public sealed record FitEnvironmentProfileDeleteResult
{
    public required string ProfileId { get; init; }

    public bool Deleted { get; init; }
}

public sealed record ApplyFitEnvironmentProfileRequest
{
    public required string ProfileId { get; init; }

    public required FitSnapshot Snapshot { get; init; }

    public bool ReplaceImplants { get; init; } = true;

    public bool ReplaceBoosters { get; init; } = true;

    public bool ReplaceSkills { get; init; } = true;
}

public sealed record FitEnvironmentProfileApplyResult
{
    public required string ProfileId { get; init; }

    public required FitEnvironmentProfile Profile { get; init; }

    public required FitSnapshot Snapshot { get; init; }

    public int AppliedImplantCount { get; init; }

    public int AppliedBoosterCount { get; init; }

    public int AppliedSkillCount { get; init; }
}

public interface IFitService
{
    UseCaseResult<FitSnapshot> CreateOrUpdate(CreateOrUpdateFitRequest request);

    UseCaseResult<FitValidationResult> Validate(ValidateFitRequest request);

    UseCaseResult<FitAttributeView> GetAttributes(GetFitAttributesRequest request);

    UseCaseResult<ImportedFitTextView> ImportText(ImportFitTextRequest request);

    UseCaseResult<ExportedFitTextView> ExportText(ExportFitTextRequest request);

    UseCaseResult<FitAmmoSelectionResult> SelectAmmo(SelectFitAmmoRequest request);
}

public interface IFitEnvironmentProfileService
{
    UseCaseResult<FitEnvironmentProfile> SaveProfile(SaveFitEnvironmentProfileRequest request);

    UseCaseResult<FitEnvironmentProfile> GetProfile(GetFitEnvironmentProfileRequest request);

    UseCaseResult<FitEnvironmentProfileCatalogView> ListProfiles(ListFitEnvironmentProfilesRequest request);

    UseCaseResult<FitEnvironmentProfileDeleteResult> DeleteProfile(DeleteFitEnvironmentProfileRequest request);

    UseCaseResult<FitEnvironmentProfileApplyResult> ApplyProfile(ApplyFitEnvironmentProfileRequest request);
}


public sealed record FitAttributeExecutionTrace
{
    public string Attribute { get; init; } = "";
    public double BaseValue { get; init; }
    public double FinalValue { get; set; }
    public bool IsComplete { get; set; } = true;
    public List<FitModifierExecutionStep> Steps { get; init; } = new();
}
public sealed record FitModifierExecutionStep
{
    public int Order { get; init; }
    public int SourceTypeId { get; init; }
    public string SourceKind { get; init; } = "";
    public int SourceIndex { get; init; }
    public string SourceState { get; init; } = "";
    public int SkillLevel { get; init; }
    public int EffectId { get; init; }
    public int ModifyingAttributeId { get; init; }
    public double SourceBaseValue { get; init; }
    public double SourceValue { get; init; }
    public string Operation { get; init; } = "";
    public double PenaltyMultiplier { get; init; } = 1;
    public double AppliedValue { get; init; }
    public double Before { get; init; }
    public double After { get; init; }
}
