using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting.Internal;

internal sealed class FitComputationEngine(TimeProvider timeProvider)
{
    public FitComputationResult Compute(FitSnapshot snapshot)
    {
        var normalizedSnapshot = NormalizeSnapshot(snapshot);
        var pass1 = RunPass1(normalizedSnapshot);
        var pass2 = RunPass2(pass1);
        var pass3 = RunPass3(pass2);
        return RunPass4(pass3);
    }

    public static IReadOnlyList<string> ValidateSnapshotShape(FitSnapshot snapshot)
    {
        var errors = new List<string>();
        if (snapshot.ShipHull is null)
        {
            errors.Add("ship_hull is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(snapshot.ShipHull.HullId))
        {
            errors.Add("ship_hull.hull_id is required.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ShipHull.Name))
        {
            errors.Add("ship_hull.name is required.");
        }

        var isDogmaSnapshot = snapshot.ShipHull.DogmaTypeId is > 0;
        if (!isDogmaSnapshot && snapshot.ShipHull.Slots.Count == 0)
        {
            errors.Add("ship_hull.slots must declare at least one slot.");
        }

        return errors;
    }

    private Pass1State RunPass1(FitSnapshot snapshot)
    {
        var issues = new List<FitValidationIssue>();
        var warnings = new List<string>();
        var slotCatalog = snapshot.ShipHull.Slots
            .GroupBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateGroup in snapshot.ShipHull.Slots
                     .GroupBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "duplicate_hull_slot",
                Category = "slot",
                Message = $"Hull defines duplicate slot id '{duplicateGroup.Key}'.",
                RelatedSlotId = duplicateGroup.Key
            });
        }

        var occupiedSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in snapshot.Modules)
        {
            ValidateOccupiedSlot(
                issues,
                slotCatalog,
                occupiedSlots,
                module.SlotId,
                module.TypeId,
                module.SlotKind,
                $"Module '{module.Name}'");
        }

        foreach (var rig in snapshot.Rigs)
        {
            ValidateOccupiedSlot(
                issues,
                slotCatalog,
                occupiedSlots,
                rig.SlotId,
                rig.TypeId,
                ModuleSlotKind.Rig,
                $"Rig '{rig.Name}'");
        }

        var slotUsage =
            new[]
            {
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.High, snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.High)),
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.Mid, snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Mid)),
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.Low, snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Low)),
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.Rig, snapshot.Rigs.Count),
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.Subsystem, snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Subsystem)),
                BuildSlotUsage(snapshot.ShipHull, ModuleSlotKind.Service, snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Service))
            };

        foreach (var usage in slotUsage.Where(usage => usage.Used > usage.Available))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "slot_capacity_exceeded",
                Category = "slot",
                Message = $"{usage.Kind} slot usage {usage.Used} exceeds hull capacity {usage.Available}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        var powergridUsed = snapshot.Modules.Sum(module => module.PowergridUsage) + snapshot.Rigs.Sum(rig => rig.PowergridUsage);
        var cpuUsed = snapshot.Modules.Sum(module => module.CpuUsage) + snapshot.Rigs.Sum(rig => rig.CpuUsage);

        if (powergridUsed > snapshot.ShipHull.PowergridOutput)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "powergrid_exceeded",
                Category = "resource",
                Message = $"Powergrid usage {powergridUsed:0.##} exceeds available {snapshot.ShipHull.PowergridOutput:0.##}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        if (cpuUsed > snapshot.ShipHull.CpuOutput)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "cpu_exceeded",
                Category = "resource",
                Message = $"CPU usage {cpuUsed:0.##} exceeds available {snapshot.ShipHull.CpuOutput:0.##}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        var usedDroneBandwidth = snapshot.DroneBay.Drones.Sum(drone => drone.BandwidthPerUnit * ResolveLaunchedDroneQuantity(drone));
        var usedDroneCapacity = snapshot.DroneBay.Drones.Sum(drone => drone.VolumePerUnit * ResolveDroneBayQuantity(drone));

        if (snapshot.DroneBay.Bandwidth > 0m && usedDroneBandwidth > snapshot.DroneBay.Bandwidth)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "drone_bandwidth_exceeded",
                Category = "resource",
                Message = $"Drone bandwidth usage {usedDroneBandwidth:0.##} exceeds available {snapshot.DroneBay.Bandwidth:0.##}.",
                RelatedTypeId = snapshot.FitId
            });
        }

        if (snapshot.DroneBay.Capacity > 0m && usedDroneCapacity > snapshot.DroneBay.Capacity)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "drone_capacity_exceeded",
                Category = "resource",
                Message = $"Drone bay usage {usedDroneCapacity:0.##} exceeds available {snapshot.DroneBay.Capacity:0.##}.",
                RelatedTypeId = snapshot.FitId
            });
        }

        if (snapshot.DroneBay.Drones.Any(drone => ResolveLaunchedDroneQuantity(drone) > 0) &&
            snapshot.DroneBay.Drones.All(drone => drone.DamagePerSecond <= 0m && drone.VolleyDamage <= 0m))
        {
            warnings.Add("Launched drones are present, but drone offensive stats are absent; drones do not contribute damage until those fields are populated.");
        }

        return new Pass1State(
            snapshot,
            slotUsage,
            powergridUsed,
            cpuUsed,
            usedDroneBandwidth,
            usedDroneCapacity,
            issues,
            warnings);
    }

    private static Pass2State RunPass2(Pass1State pass1)
    {
        var snapshot = pass1.Snapshot;
        var turretDamageMultiplier = Math.Max(
            0m,
            snapshot.ShipHull.TurretDamageMultiplier
            + snapshot.Modules.Sum(module => module.TurretDamageMultiplierBonus)
            + snapshot.Rigs.Sum(rig => rig.TurretDamageMultiplierBonus)
            + snapshot.Modules.Sum(module => module.Charge?.TurretDamageMultiplierBonus ?? 0m));

        return new Pass2State(
            pass1,
            ShieldHitpoints: snapshot.ShipHull.ShieldHitpoints
                            + snapshot.Modules.Sum(module => module.ShieldHitpointsBonus)
                            + snapshot.Rigs.Sum(rig => rig.ShieldHitpointsBonus),
            ShieldResistances: ResolveResistanceProfile(
                snapshot.ShipHull.ShieldResistances,
                snapshot.Modules.Select(module => module.ShieldResistancesBonus),
                snapshot.Rigs.Select(rig => rig.ShieldResistancesBonus)),
            ArmorHitpoints: snapshot.ShipHull.ArmorHitpoints
                           + snapshot.Modules.Sum(module => module.ArmorHitpointsBonus)
                           + snapshot.Rigs.Sum(rig => rig.ArmorHitpointsBonus),
            ArmorResistances: ResolveResistanceProfile(
                snapshot.ShipHull.ArmorResistances,
                snapshot.Modules.Select(module => module.ArmorResistancesBonus),
                snapshot.Rigs.Select(rig => rig.ArmorResistancesBonus)),
            StructureHitpoints: snapshot.ShipHull.StructureHitpoints
                               + snapshot.Modules.Sum(module => module.StructureHitpointsBonus)
                               + snapshot.Rigs.Sum(rig => rig.StructureHitpointsBonus),
            StructureResistances: ResolveResistanceProfile(
                snapshot.ShipHull.StructureResistances,
                snapshot.Modules.Select(module => module.StructureResistancesBonus),
                snapshot.Rigs.Select(rig => rig.StructureResistancesBonus)),
            CapacitorCapacity: snapshot.ShipHull.CapacitorCapacity
                               + snapshot.Modules.Sum(module => module.CapacitorBonus)
                               + snapshot.Rigs.Sum(rig => rig.CapacitorBonus),
            CapacitorRechargeSeconds: snapshot.ShipHull.CapacitorRechargeSeconds,
            MaxVelocity: snapshot.ShipHull.MaxVelocity
                         + snapshot.Modules.Sum(module => module.SpeedBonus)
                         + snapshot.Rigs.Sum(rig => rig.SpeedBonus),
            TurretDamageMultiplier: turretDamageMultiplier,
            ShieldRepairPerSecond: Math.Max(0m, snapshot.Modules.Sum(module => module.ShieldRepairPerSecond)),
            ArmorRepairPerSecond: Math.Max(0m, snapshot.Modules.Sum(module => module.ArmorRepairPerSecond)),
            StructureRepairPerSecond: Math.Max(0m, snapshot.Modules.Sum(module => module.StructureRepairPerSecond)));
    }

    private static Pass3State RunPass3(Pass2State pass2)
    {
        var snapshot = pass2.Pass1.Snapshot;
        decimal turretDps = 0m;
        decimal turretDpsWithReload = 0m;
        decimal turretVolley = 0m;
        decimal droneDps = 0m;
        decimal droneVolley = 0m;
        decimal capacitorUsagePerSecond = 0m;
        decimal weaponCapacitorUsagePerSecond = 0m;
        decimal activeTankCapacitorUsagePerSecond = 0m;
        decimal weaponOptimalRangeMeters = 0m;
        decimal weaponFalloffRangeMeters = 0m;
        decimal weaponTracking = 0m;
        decimal weaponSignatureResolutionMeters = 0m;
        decimal missileExplosionRadiusMeters = 0m;
        decimal missileExplosionVelocityMetersPerSecond = 0m;
        decimal missileDamageReductionFactor = 0m;
        decimal missileDamageReductionSensitivity = 0m;
        var turretDpsProfile = ZeroDamageProfile();
        var turretDpsWithReloadProfile = ZeroDamageProfile();
        var turretVolleyProfile = ZeroDamageProfile();
        var droneDpsProfile = ZeroDamageProfile();
        var droneVolleyProfile = ZeroDamageProfile();

        foreach (var module in snapshot.Modules)
        {
            var moduleCapacitorUsage = ResolveCapacitorUsagePerSecond(module);
            capacitorUsagePerSecond += moduleCapacitorUsage;

            if (module.ShieldRepairPerSecond > 0m ||
                module.ArmorRepairPerSecond > 0m ||
                module.StructureRepairPerSecond > 0m)
            {
                activeTankCapacitorUsagePerSecond += moduleCapacitorUsage;
            }
        }

        foreach (var module in snapshot.Modules.Where(module => module.SlotKind == ModuleSlotKind.High))
        {
            var resolvedDamageProfilePerSecond = ResolveModuleDamageProfilePerSecond(module);
            var damagePerSecond = TotalDamage(resolvedDamageProfilePerSecond);
            var resolvedVolleyProfile = ResolveModuleVolleyProfile(module, resolvedDamageProfilePerSecond);
            var volleyDamage = TotalDamage(resolvedVolleyProfile);
            var cycleTimeSeconds = ResolveCycleTimeSeconds(module, volleyDamage, damagePerSecond);
            var sustainedWithoutReload = damagePerSecond > 0m
                ? damagePerSecond
                : cycleTimeSeconds > 0m && volleyDamage > 0m
                    ? volleyDamage / cycleTimeSeconds
                    : 0m;
            var sustainedWithReload = ComputeSustainedDamageWithReload(
                sustainedWithoutReload,
                volleyDamage,
                cycleTimeSeconds,
                module.ReloadTimeSeconds,
                module.MagazineCapacity,
                Math.Max(1, module.ChargeUnitsPerCycle));
            var sustainedWithoutReloadProfile = ScaleDamageProfile(
                damagePerSecond > 0m
                    ? resolvedDamageProfilePerSecond
                    : cycleTimeSeconds > 0m
                        ? ScaleDamageProfile(resolvedVolleyProfile, 1m / cycleTimeSeconds)
                        : ZeroDamageProfile(),
                pass2.TurretDamageMultiplier);
            var sustainedWithReloadProfile = damagePerSecond > 0m && sustainedWithoutReload > 0m
                ? ScaleDamageProfile(
                    sustainedWithoutReloadProfile,
                    sustainedWithReload / sustainedWithoutReload)
                : ZeroDamageProfile();

            turretDps += sustainedWithoutReload * pass2.TurretDamageMultiplier;
            turretDpsWithReload += sustainedWithReload * pass2.TurretDamageMultiplier;
            turretVolley += volleyDamage * pass2.TurretDamageMultiplier;
            turretDpsProfile = AddDamageProfiles(turretDpsProfile, sustainedWithoutReloadProfile);
            turretDpsWithReloadProfile = AddDamageProfiles(turretDpsWithReloadProfile, sustainedWithReloadProfile);
            turretVolleyProfile = AddDamageProfiles(
                turretVolleyProfile,
                ScaleDamageProfile(resolvedVolleyProfile, pass2.TurretDamageMultiplier));
            if (sustainedWithoutReload > 0m || volleyDamage > 0m)
            {
                weaponCapacitorUsagePerSecond += ResolveCapacitorUsagePerSecond(module);
            }
            weaponOptimalRangeMeters = Math.Max(
                weaponOptimalRangeMeters,
                Math.Max(0m, module.OptimalRangeMeters + (module.Charge?.OptimalRangeBonusMeters ?? 0m)));
            weaponFalloffRangeMeters = Math.Max(weaponFalloffRangeMeters, Math.Max(0m, module.FalloffRangeMeters));
            weaponTracking = Math.Max(weaponTracking, Math.Max(0m, module.TrackingSpeed));
            weaponSignatureResolutionMeters = Math.Max(weaponSignatureResolutionMeters, Math.Max(0m, module.SignatureResolutionMeters));
            missileExplosionRadiusMeters = Math.Max(
                missileExplosionRadiusMeters,
                Math.Max(0m, module.MissileExplosionRadiusMeters + (module.Charge?.MissileExplosionRadiusMeters ?? 0m)));
            missileExplosionVelocityMetersPerSecond = Math.Max(
                missileExplosionVelocityMetersPerSecond,
                Math.Max(0m, module.MissileExplosionVelocityMetersPerSecond + (module.Charge?.MissileExplosionVelocityMetersPerSecond ?? 0m)));
            missileDamageReductionFactor = Math.Max(
                missileDamageReductionFactor,
                Math.Max(0m, module.MissileDamageReductionFactor + (module.Charge?.MissileDamageReductionFactor ?? 0m)));
            missileDamageReductionSensitivity = Math.Max(
                missileDamageReductionSensitivity,
                Math.Max(0m, module.MissileDamageReductionSensitivity + (module.Charge?.MissileDamageReductionSensitivity ?? 0m)));
        }

        foreach (var drone in snapshot.DroneBay.Drones)
        {
            var launchedQuantity = ResolveLaunchedDroneQuantity(drone);
            if (launchedQuantity <= 0)
            {
                continue;
            }

            var dronePerSecond = ResolveDroneDamagePerSecond(drone);
            var droneAlpha = ResolveDroneVolley(drone);
            var droneDamageProfilePerSecond = ResolveDroneDamageProfilePerSecond(drone);
            var droneVolleyDamageProfile = ResolveDroneVolleyDamageProfile(drone, droneDamageProfilePerSecond);
            droneDps += dronePerSecond * launchedQuantity;
            droneVolley += droneAlpha * launchedQuantity;
            droneDpsProfile = AddDamageProfiles(droneDpsProfile, ScaleDamageProfile(droneDamageProfilePerSecond, launchedQuantity));
            droneVolleyProfile = AddDamageProfiles(droneVolleyProfile, ScaleDamageProfile(droneVolleyDamageProfile, launchedQuantity));
            weaponOptimalRangeMeters = Math.Max(weaponOptimalRangeMeters, Math.Max(0m, drone.OptimalRangeMeters));
            weaponFalloffRangeMeters = Math.Max(weaponFalloffRangeMeters, Math.Max(0m, drone.FalloffRangeMeters));
            weaponTracking = Math.Max(weaponTracking, Math.Max(0m, drone.TrackingSpeed));
            weaponSignatureResolutionMeters = Math.Max(weaponSignatureResolutionMeters, Math.Max(0m, drone.SignatureResolutionMeters));
        }

        var peakCapacitorRechargePerSecond = pass2.CapacitorCapacity > 0m && pass2.CapacitorRechargeSeconds > 0m
            ? pass2.CapacitorCapacity / pass2.CapacitorRechargeSeconds * 2.5m
            : 0m;
        var capacitorStable = capacitorUsagePerSecond <= 0m || peakCapacitorRechargePerSecond >= capacitorUsagePerSecond;
        decimal? capacitorDepletionSeconds = null;
        if (!capacitorStable && pass2.CapacitorCapacity > 0m)
        {
            var netDrain = capacitorUsagePerSecond - peakCapacitorRechargePerSecond;
            if (netDrain > 0m)
            {
                capacitorDepletionSeconds = decimal.Round(pass2.CapacitorCapacity / netDrain, 3);
            }
        }

        return new Pass3State(
            pass2,
            AppliedTurretDamagePerSecond: decimal.Round(turretDps, 3),
            AppliedTurretDamageProfilePerSecond: RoundDamageProfile(turretDpsProfile),
            DamagePerSecondWithReload: decimal.Round(turretDpsWithReload + droneDps, 3),
            AppliedDroneDamagePerSecond: decimal.Round(droneDps, 3),
            AppliedDroneDamageProfilePerSecond: RoundDamageProfile(droneDpsProfile),
            AppliedDamagePerSecond: decimal.Round(turretDpsWithReload + droneDps, 3),
            AppliedDamageProfilePerSecond: RoundDamageProfile(AddDamageProfiles(turretDpsWithReloadProfile, droneDpsProfile)),
            VolleyDamage: decimal.Round(turretVolley + droneVolley, 3),
            VolleyDamageProfile: RoundDamageProfile(AddDamageProfiles(turretVolleyProfile, droneVolleyProfile)),
            CapacitorUsagePerSecond: decimal.Round(capacitorUsagePerSecond, 3),
            WeaponCapacitorUsagePerSecond: decimal.Round(weaponCapacitorUsagePerSecond, 3),
            ActiveTankCapacitorUsagePerSecond: decimal.Round(activeTankCapacitorUsagePerSecond, 3),
            PeakCapacitorRechargePerSecond: decimal.Round(peakCapacitorRechargePerSecond, 3),
            CapacitorStable: capacitorStable,
            CapacitorDepletionSeconds: capacitorDepletionSeconds,
            WeaponOptimalRangeMeters: decimal.Round(weaponOptimalRangeMeters, 3),
            WeaponFalloffRangeMeters: decimal.Round(weaponFalloffRangeMeters, 3),
            WeaponTracking: decimal.Round(weaponTracking, 6),
            WeaponSignatureResolutionMeters: decimal.Round(weaponSignatureResolutionMeters, 3),
            MissileExplosionRadiusMeters: decimal.Round(missileExplosionRadiusMeters, 3),
            MissileExplosionVelocityMetersPerSecond: decimal.Round(missileExplosionVelocityMetersPerSecond, 3),
            MissileDamageReductionFactor: decimal.Round(missileDamageReductionFactor, 6),
            MissileDamageReductionSensitivity: decimal.Round(missileDamageReductionSensitivity, 6));
    }

    private static FitComputationResult RunPass4(Pass3State pass3)
    {
        var pass2 = pass3.Pass2;
        var pass1 = pass2.Pass1;
        var snapshot = pass1.Snapshot;
        var shieldEffectiveHitpointsByDamageType = ComputeEffectiveProjection(pass2.ShieldHitpoints, pass2.ShieldResistances);
        var armorEffectiveHitpointsByDamageType = ComputeEffectiveProjection(pass2.ArmorHitpoints, pass2.ArmorResistances);
        var structureEffectiveHitpointsByDamageType = ComputeEffectiveProjection(pass2.StructureHitpoints, pass2.StructureResistances);
        var totalEffectiveHitpointsByDamageType = AddDamageTypeProjections(
            AddDamageTypeProjections(shieldEffectiveHitpointsByDamageType, armorEffectiveHitpointsByDamageType),
            structureEffectiveHitpointsByDamageType);
        var effectiveRepairPerSecondByDamageType = AddDamageTypeProjections(
            AddDamageTypeProjections(
                ComputeEffectiveProjection(pass2.ShieldRepairPerSecond, pass2.ShieldResistances),
                ComputeEffectiveProjection(pass2.ArmorRepairPerSecond, pass2.ArmorResistances)),
            ComputeEffectiveProjection(pass2.StructureRepairPerSecond, pass2.StructureResistances));
        var attributes = new FitAttributeView
        {
            FitId = snapshot.FitId,
            HullId = snapshot.ShipHull.HullId,
            PowergridUsed = pass1.PowergridUsed,
            PowergridAvailable = snapshot.ShipHull.PowergridOutput,
            CpuUsed = pass1.CpuUsed,
            CpuAvailable = snapshot.ShipHull.CpuOutput,
            DroneBandwidthUsed = pass1.DroneBandwidthUsed,
            DroneBandwidthAvailable = snapshot.DroneBay.Bandwidth,
            DroneBayUsed = pass1.DroneBayUsed,
            DroneBayAvailable = snapshot.DroneBay.Capacity,
            ShieldHitpoints = pass2.ShieldHitpoints,
            ShieldResistances = pass2.ShieldResistances,
            ArmorHitpoints = pass2.ArmorHitpoints,
            ArmorResistances = pass2.ArmorResistances,
            StructureHitpoints = pass2.StructureHitpoints,
            StructureResistances = pass2.StructureResistances,
            TotalHitpoints = pass2.ShieldHitpoints + pass2.ArmorHitpoints + pass2.StructureHitpoints,
            ShieldEffectiveHitpointsOmni = shieldEffectiveHitpointsByDamageType.Omni,
            ShieldEffectiveHitpointsByDamageType = shieldEffectiveHitpointsByDamageType,
            ArmorEffectiveHitpointsOmni = armorEffectiveHitpointsByDamageType.Omni,
            ArmorEffectiveHitpointsByDamageType = armorEffectiveHitpointsByDamageType,
            StructureEffectiveHitpointsOmni = structureEffectiveHitpointsByDamageType.Omni,
            StructureEffectiveHitpointsByDamageType = structureEffectiveHitpointsByDamageType,
            TotalEffectiveHitpointsOmni = totalEffectiveHitpointsByDamageType.Omni,
            TotalEffectiveHitpointsByDamageType = totalEffectiveHitpointsByDamageType,
            CapacitorCapacity = pass2.CapacitorCapacity,
            CapacitorRechargeSeconds = pass2.CapacitorRechargeSeconds,
            CapacitorUsagePerSecond = pass3.CapacitorUsagePerSecond,
            WeaponCapacitorUsagePerSecond = pass3.WeaponCapacitorUsagePerSecond,
            ActiveTankCapacitorUsagePerSecond = pass3.ActiveTankCapacitorUsagePerSecond,
            PeakCapacitorRechargePerSecond = pass3.PeakCapacitorRechargePerSecond,
            CapacitorStable = pass3.CapacitorStable,
            CapacitorDepletionSeconds = pass3.CapacitorDepletionSeconds,
            MaxVelocity = pass2.MaxVelocity,
            SignatureRadius = snapshot.ShipHull.SignatureRadius,
            MaxTargetRange = snapshot.ShipHull.MaxTargetRange,
            TurretDamageMultiplier = pass2.TurretDamageMultiplier,
            AppliedDamagePerSecond = pass3.AppliedDamagePerSecond,
            AppliedDamageProfilePerSecond = pass3.AppliedDamageProfilePerSecond,
            AppliedTurretDamagePerSecond = pass3.AppliedTurretDamagePerSecond,
            AppliedTurretDamageProfilePerSecond = pass3.AppliedTurretDamageProfilePerSecond,
            AppliedDroneDamagePerSecond = pass3.AppliedDroneDamagePerSecond,
            AppliedDroneDamageProfilePerSecond = pass3.AppliedDroneDamageProfilePerSecond,
            DamagePerSecondWithReload = pass3.DamagePerSecondWithReload,
            VolleyDamage = pass3.VolleyDamage,
            VolleyDamageProfile = pass3.VolleyDamageProfile,
            WeaponOptimalRangeMeters = pass3.WeaponOptimalRangeMeters,
            WeaponFalloffRangeMeters = pass3.WeaponFalloffRangeMeters,
            WeaponTracking = pass3.WeaponTracking,
            WeaponSignatureResolutionMeters = pass3.WeaponSignatureResolutionMeters,
            MissileExplosionRadiusMeters = pass3.MissileExplosionRadiusMeters,
            MissileExplosionVelocityMetersPerSecond = pass3.MissileExplosionVelocityMetersPerSecond,
            MissileDamageReductionFactor = pass3.MissileDamageReductionFactor,
            MissileDamageReductionSensitivity = pass3.MissileDamageReductionSensitivity,
            PassiveShieldRechargePerSecond = 0m,
            ShieldRepairPerSecond = pass2.ShieldRepairPerSecond,
            ArmorRepairPerSecond = pass2.ArmorRepairPerSecond,
            StructureRepairPerSecond = pass2.StructureRepairPerSecond,
            EffectiveRepairPerSecondByDamageType = effectiveRepairPerSecondByDamageType,
            SlotUsage = pass1.SlotUsage,
            AttributeSnapshot = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["droneBandwidth"] = decimal.Round(snapshot.DroneBay.Bandwidth, 3),
                ["droneBandwidthLoad"] = decimal.Round(pass1.DroneBandwidthUsed, 3),
                ["droneCapacity"] = decimal.Round(snapshot.DroneBay.Capacity, 3),
                ["droneCapacityLoad"] = decimal.Round(pass1.DroneBayUsed, 3),
                ["signatureRadius"] = decimal.Round(snapshot.ShipHull.SignatureRadius, 3),
                ["maxTargetRange"] = decimal.Round(snapshot.ShipHull.MaxTargetRange, 3),
                ["weaponOptimalRange"] = pass3.WeaponOptimalRangeMeters,
                ["weaponFalloffRange"] = pass3.WeaponFalloffRangeMeters,
                ["weaponTracking"] = pass3.WeaponTracking,
                ["weaponSignatureResolution"] = pass3.WeaponSignatureResolutionMeters,
                ["missileExplosionRadius"] = pass3.MissileExplosionRadiusMeters,
                ["missileExplosionVelocity"] = pass3.MissileExplosionVelocityMetersPerSecond,
                ["missileDamageReductionFactor"] = pass3.MissileDamageReductionFactor,
                ["missileDamageReductionSensitivity"] = pass3.MissileDamageReductionSensitivity
            },
            ApproximationNotes =
            [
                "This MVP now runs through a simplified four-pass fitting pipeline: normalize and validate input, aggregate hull and fit bonuses, derive offense and capacitor behavior, then project the public attribute view.",
                "Weapon DPS remains an approximation driven entirely by snapshot fields. Reload is modeled only when cycle time, reload time, and magazine values are present; target-dependent turret and missile application is resolved by the combat kernel, not by fit attributes.",
                "Damage types and layer resistances are projected, and resistance bonuses now use a classic descending-strength stacking-penalty approximation rather than raw percentage-point addition.",
                "Drone bandwidth and bay volume are modeled directly from snapshot DroneBay values and listed drone stacks; launch state, control range, and squad behavior are still out of scope.",
                "Effective hitpoints and effective repair are projected both per damage type and as an omni average; the omni figures remain average-resistance approximations rather than exact EHP against a real incoming weapon mix.",
                "Capacitor stability uses a peak-recharge approximation. Weapon and active-tank capacitor usage are surfaced separately for combat, but fit-level calculation still does not run a per-module cycle simulator."
            ]
        };

        RemoveZeroAttributeSnapshotEntries(attributes.AttributeSnapshot as IDictionary<string, decimal>);

        return new FitComputationResult(snapshot, attributes, pass1.Issues, pass1.Warnings);
    }

    private FitSnapshot NormalizeSnapshot(FitSnapshot snapshot)
    {
        var now = timeProvider.GetUtcNow();
        return snapshot with
        {
            FitId = string.IsNullOrWhiteSpace(snapshot.FitId) ? $"fit-{Guid.NewGuid():N}" : snapshot.FitId.Trim(),
            Name = NormalizeOptional(snapshot.Name),
            Notes = NormalizeOptional(snapshot.Notes),
            ShipHull = snapshot.ShipHull with
            {
                HullId = snapshot.ShipHull.HullId.Trim(),
                Name = snapshot.ShipHull.Name.Trim(),
                Slots = snapshot.ShipHull.Slots
                    .Select(slot => slot with
                    {
                        SlotId = slot.SlotId.Trim(),
                        Label = NormalizeOptional(slot.Label)
                    })
                    .OrderBy(slot => slot.Kind)
                    .ThenBy(slot => slot.Index)
                    .ToArray()
            },
            Modules = snapshot.Modules
                .Select(module => module with
                {
                    TypeId = module.TypeId.Trim(),
                    Name = module.Name.Trim(),
                    SlotId = module.SlotId.Trim(),
                    Charge = module.Charge is null
                        ? null
                        : module.Charge with
                        {
                            ChargeId = module.Charge.ChargeId.Trim(),
                            Name = module.Charge.Name.Trim()
                        },
                    Tags = module.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToArray()
                })
                .ToArray(),
            Rigs = snapshot.Rigs
                .Select(rig => rig with
                {
                    TypeId = rig.TypeId.Trim(),
                    Name = rig.Name.Trim(),
                    SlotId = rig.SlotId.Trim()
                })
                .ToArray(),
            DroneBay = snapshot.DroneBay with
            {
                Drones = snapshot.DroneBay.Drones
                    .Select(drone => drone with
                    {
                        DroneTypeId = drone.DroneTypeId.Trim(),
                        Name = drone.Name.Trim()
                    })
                    .ToArray()
            },
            CreatedAtUtc = snapshot.CreatedAtUtc == default ? now : snapshot.CreatedAtUtc,
            UpdatedAtUtc = now
        };
    }

    private static decimal ResolveVolleyDamage(FittedModule module, decimal damagePerSecond)
    {
        if (module.VolleyDamage > 0m || module.Charge?.VolleyDamageBonus > 0m)
        {
            return Math.Max(0m, module.VolleyDamage + (module.Charge?.VolleyDamageBonus ?? 0m));
        }

        if (module.CycleTimeSeconds > 0m && damagePerSecond > 0m)
        {
            return damagePerSecond * module.CycleTimeSeconds;
        }

        return 0m;
    }

    private static DamageProfile ResolveModuleDamageProfilePerSecond(FittedModule module)
    {
        var scalarDamagePerSecond = Math.Max(0m, module.DamagePerSecond + (module.Charge?.DamagePerSecondBonus ?? 0m));
        var typedProfile = AddDamageProfiles(module.DamageProfilePerSecond, module.Charge?.DamageProfileBonus ?? ZeroDamageProfile());
        return NormalizeDamageProfileTotal(typedProfile, scalarDamagePerSecond);
    }

    private static DamageProfile ResolveModuleVolleyProfile(FittedModule module, DamageProfile resolvedDamageProfilePerSecond)
    {
        var scalarVolleyDamage = ResolveVolleyDamage(module, TotalDamage(resolvedDamageProfilePerSecond));
        var typedVolleyProfile = AddDamageProfiles(module.VolleyDamageProfile, module.Charge?.VolleyDamageProfileBonus ?? ZeroDamageProfile());
        if (TotalDamage(typedVolleyProfile) > 0m || scalarVolleyDamage > 0m)
        {
            return NormalizeDamageProfileTotal(typedVolleyProfile, scalarVolleyDamage);
        }

        if (module.CycleTimeSeconds > 0m)
        {
            return ScaleDamageProfile(resolvedDamageProfilePerSecond, module.CycleTimeSeconds);
        }

        return ZeroDamageProfile();
    }

    private static decimal ResolveCycleTimeSeconds(FittedModule module, decimal volleyDamage, decimal damagePerSecond)
    {
        if (module.CycleTimeSeconds > 0m)
        {
            return module.CycleTimeSeconds;
        }

        if (volleyDamage > 0m && damagePerSecond > 0m)
        {
            return volleyDamage / damagePerSecond;
        }

        return 0m;
    }

    private static decimal ResolveCapacitorUsagePerSecond(FittedModule module) =>
        Math.Max(0m, module.CapacitorUsagePerSecond + (module.Charge?.CapacitorUsagePerSecond ?? 0m));

    private static decimal ComputeSustainedDamageWithReload(
        decimal damagePerSecond,
        decimal volleyDamage,
        decimal cycleTimeSeconds,
        decimal reloadTimeSeconds,
        int magazineCapacity,
        int chargeUnitsPerCycle)
    {
        if (damagePerSecond <= 0m || cycleTimeSeconds <= 0m || reloadTimeSeconds <= 0m || magazineCapacity <= 0)
        {
            return damagePerSecond;
        }

        var shotsPerMagazine = magazineCapacity / Math.Max(1, chargeUnitsPerCycle);
        if (shotsPerMagazine <= 0)
        {
            return damagePerSecond;
        }

        var resolvedVolley = volleyDamage > 0m ? volleyDamage : damagePerSecond * cycleTimeSeconds;
        var magazineDamage = resolvedVolley * shotsPerMagazine;
        var activeDuration = cycleTimeSeconds * shotsPerMagazine;
        var sustainedDuration = activeDuration + reloadTimeSeconds;
        return sustainedDuration > 0m ? magazineDamage / sustainedDuration : damagePerSecond;
    }

    private static decimal ResolveDroneDamagePerSecond(DroneStack drone)
    {
        var typedDamage = TotalDamage(drone.DamageProfilePerSecond);
        if (typedDamage > 0m)
        {
            return typedDamage;
        }

        if (drone.DamagePerSecond > 0m)
        {
            return drone.DamagePerSecond;
        }

        var typedVolley = TotalDamage(drone.VolleyDamageProfile);
        if (typedVolley > 0m && drone.CycleTimeSeconds > 0m)
        {
            return typedVolley / drone.CycleTimeSeconds;
        }

        if (drone.VolleyDamage > 0m && drone.CycleTimeSeconds > 0m)
        {
            return drone.VolleyDamage / drone.CycleTimeSeconds;
        }

        return 0m;
    }

    private static decimal ResolveDroneVolley(DroneStack drone)
    {
        var typedVolley = TotalDamage(drone.VolleyDamageProfile);
        if (typedVolley > 0m)
        {
            return typedVolley;
        }

        if (drone.VolleyDamage > 0m)
        {
            return drone.VolleyDamage;
        }

        var typedDamage = TotalDamage(drone.DamageProfilePerSecond);
        if (typedDamage > 0m && drone.CycleTimeSeconds > 0m)
        {
            return typedDamage * drone.CycleTimeSeconds;
        }

        if (drone.DamagePerSecond > 0m && drone.CycleTimeSeconds > 0m)
        {
            return drone.DamagePerSecond * drone.CycleTimeSeconds;
        }

        return 0m;
    }

    private static DamageProfile ResolveDroneDamageProfilePerSecond(DroneStack drone)
    {
        var scalarDamagePerSecond = ResolveDroneDamagePerSecond(drone);
        return NormalizeDamageProfileTotal(drone.DamageProfilePerSecond, scalarDamagePerSecond);
    }

    private static DamageProfile ResolveDroneVolleyDamageProfile(DroneStack drone, DamageProfile damageProfilePerSecond)
    {
        var scalarVolleyDamage = ResolveDroneVolley(drone);
        if (TotalDamage(drone.VolleyDamageProfile) > 0m || scalarVolleyDamage > 0m)
        {
            return NormalizeDamageProfileTotal(drone.VolleyDamageProfile, scalarVolleyDamage);
        }

        if (drone.CycleTimeSeconds > 0m)
        {
            return ScaleDamageProfile(damageProfilePerSecond, drone.CycleTimeSeconds);
        }

        return ZeroDamageProfile();
    }

    private static DamageProfile NormalizeDamageProfileTotal(DamageProfile profile, decimal scalarTotal)
    {
        var normalizedScalarTotal = Math.Max(0m, scalarTotal);
        var typedTotal = TotalDamage(profile);
        if (typedTotal <= 0m)
        {
            return normalizedScalarTotal <= 0m ? ZeroDamageProfile() : SplitDamageEvenly(normalizedScalarTotal);
        }

        if (normalizedScalarTotal <= 0m || typedTotal == normalizedScalarTotal)
        {
            return profile;
        }

        return ScaleDamageProfile(profile, normalizedScalarTotal / typedTotal);
    }

    private static ResistanceProfile ResolveResistanceProfile(
        ResistanceProfile baseProfile,
        IEnumerable<ResistanceProfile> moduleBonuses,
        IEnumerable<ResistanceProfile> rigBonuses)
    {
        var bonuses = moduleBonuses.Concat(rigBonuses).ToArray();
        return new ResistanceProfile
        {
            EmPercent = ResolveStackedResistance(baseProfile.EmPercent, bonuses.Select(bonus => bonus.EmPercent)),
            ThermalPercent = ResolveStackedResistance(baseProfile.ThermalPercent, bonuses.Select(bonus => bonus.ThermalPercent)),
            KineticPercent = ResolveStackedResistance(baseProfile.KineticPercent, bonuses.Select(bonus => bonus.KineticPercent)),
            ExplosivePercent = ResolveStackedResistance(baseProfile.ExplosivePercent, bonuses.Select(bonus => bonus.ExplosivePercent))
        };
    }

    private static decimal ResolveStackedResistance(decimal baseResistance, IEnumerable<decimal> bonuses)
    {
        var vulnerability = 1m - ClampResistance(baseResistance);
        foreach (var bonus in bonuses
                     .Where(bonus => bonus != 0m)
                     .OrderByDescending(bonus => Math.Abs(bonus))
                     .Select((bonus, index) => ApplyStackingPenalty(bonus, index)))
        {
            vulnerability *= 1m - bonus;
        }

        return ClampResistance(1m - vulnerability);
    }

    private static decimal ApplyStackingPenalty(decimal bonus, int index)
    {
        var multipliers = StackingPenaltyMultipliers;
        var multiplier = index < multipliers.Length ? multipliers[index] : 0m;
        return bonus * multiplier;
    }

    private static decimal ComputeOmniEffectiveHitpoints(decimal hitpoints, ResistanceProfile resistances)
    {
        var averageResistance = (
            ClampResistance(resistances.EmPercent)
            + ClampResistance(resistances.ThermalPercent)
            + ClampResistance(resistances.KineticPercent)
            + ClampResistance(resistances.ExplosivePercent)) / 4m;
        var vulnerability = Math.Max(0.05m, 1m - averageResistance);
        return decimal.Round(hitpoints / vulnerability, 3);
    }

    private static DamageTypeProjection ComputeEffectiveProjection(decimal rawValue, ResistanceProfile resistances) =>
        new()
        {
            Em = ComputeEffectiveValue(rawValue, resistances.EmPercent),
            Thermal = ComputeEffectiveValue(rawValue, resistances.ThermalPercent),
            Kinetic = ComputeEffectiveValue(rawValue, resistances.KineticPercent),
            Explosive = ComputeEffectiveValue(rawValue, resistances.ExplosivePercent),
            Omni = ComputeOmniEffectiveHitpoints(rawValue, resistances)
        };

    private static decimal ComputeEffectiveValue(decimal rawValue, decimal resistance)
    {
        if (rawValue <= 0m)
        {
            return 0m;
        }

        var vulnerability = Math.Max(0.05m, 1m - ClampResistance(resistance));
        return decimal.Round(rawValue / vulnerability, 3);
    }

    private static DamageTypeProjection AddDamageTypeProjections(DamageTypeProjection left, DamageTypeProjection right) =>
        new()
        {
            Em = decimal.Round(left.Em + right.Em, 3),
            Thermal = decimal.Round(left.Thermal + right.Thermal, 3),
            Kinetic = decimal.Round(left.Kinetic + right.Kinetic, 3),
            Explosive = decimal.Round(left.Explosive + right.Explosive, 3),
            Omni = decimal.Round(left.Omni + right.Omni, 3)
        };

    private static readonly decimal[] StackingPenaltyMultipliers =
    [
        1.000000m,
        0.869120m,
        0.570583m,
        0.282955m,
        0.105993m,
        0.029994m,
        0.006404m,
        0.001000m
    ];

    private static decimal ClampResistance(decimal value) => Math.Clamp(value, 0m, 0.95m);

    private static DamageProfile ZeroDamageProfile() => new();

    private static decimal TotalDamage(DamageProfile profile) =>
        Math.Max(0m, profile.Em)
        + Math.Max(0m, profile.Thermal)
        + Math.Max(0m, profile.Kinetic)
        + Math.Max(0m, profile.Explosive);

    private static DamageProfile AddDamageProfiles(DamageProfile left, DamageProfile right) =>
        new()
        {
            Em = left.Em + right.Em,
            Thermal = left.Thermal + right.Thermal,
            Kinetic = left.Kinetic + right.Kinetic,
            Explosive = left.Explosive + right.Explosive
        };

    private static DamageProfile ScaleDamageProfile(DamageProfile profile, decimal factor)
    {
        if (factor <= 0m)
        {
            return ZeroDamageProfile();
        }

        return new DamageProfile
        {
            Em = profile.Em * factor,
            Thermal = profile.Thermal * factor,
            Kinetic = profile.Kinetic * factor,
            Explosive = profile.Explosive * factor
        };
    }

    private static DamageProfile SplitDamageEvenly(decimal totalDamage)
    {
        if (totalDamage <= 0m)
        {
            return ZeroDamageProfile();
        }

        var quadrant = totalDamage / 4m;
        return new DamageProfile
        {
            Em = quadrant,
            Thermal = quadrant,
            Kinetic = quadrant,
            Explosive = quadrant
        };
    }

    private static DamageProfile RoundDamageProfile(DamageProfile profile) =>
        new()
        {
            Em = decimal.Round(profile.Em, 3),
            Thermal = decimal.Round(profile.Thermal, 3),
            Kinetic = decimal.Round(profile.Kinetic, 3),
            Explosive = decimal.Round(profile.Explosive, 3)
        };

    private static void ValidateOccupiedSlot(
        ICollection<FitValidationIssue> issues,
        IReadOnlyDictionary<string, ModuleSlot> slotCatalog,
        IDictionary<string, string> occupiedSlots,
        string slotId,
        string typeId,
        ModuleSlotKind expectedKind,
        string entityLabel)
    {
        if (!slotCatalog.TryGetValue(slotId, out var slot))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "unknown_slot",
                Category = "slot",
                Message = $"{entityLabel} targets unknown slot '{slotId}'.",
                RelatedSlotId = slotId,
                RelatedTypeId = typeId
            });
            return;
        }

        if (slot.Kind != expectedKind)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "slot_kind_mismatch",
                Category = "slot",
                Message = $"{entityLabel} requires a {expectedKind} slot but '{slotId}' is {slot.Kind}.",
                RelatedSlotId = slotId,
                RelatedTypeId = typeId
            });
        }

        if (occupiedSlots.TryGetValue(slotId, out var occupyingTypeId))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "slot_conflict",
                Category = "slot",
                Message = $"Slot '{slotId}' is already occupied by '{occupyingTypeId}'.",
                RelatedSlotId = slotId,
                RelatedTypeId = typeId
            });
            return;
        }

        occupiedSlots[slotId] = typeId;
    }

    private static FitSlotUsageView BuildSlotUsage(ShipHull hull, ModuleSlotKind kind, int used) =>
        new()
        {
            Kind = kind,
            Used = used,
            Available = hull.Slots.Count(slot => slot.Kind == kind)
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ResolveLaunchedDroneQuantity(DroneStack drone)
    {
        if (drone.State is not (FittingItemState.Active or FittingItemState.Overload))
        {
            return 0;
        }

        return Math.Max(0, drone.Quantity);
    }

    private static int ResolveDroneBayQuantity(DroneStack drone)
    {
        return Math.Max(0, drone.BayQuantity ?? drone.Quantity);
    }

    private static void RemoveZeroAttributeSnapshotEntries(IDictionary<string, decimal>? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        foreach (var key in snapshot.Where(entry => entry.Value == 0m).Select(entry => entry.Key).ToArray())
        {
            snapshot.Remove(key);
        }
    }

    internal sealed record FitComputationResult(
        FitSnapshot Snapshot,
        FitAttributeView Attributes,
        IReadOnlyList<FitValidationIssue> Issues,
        IReadOnlyList<string> Warnings);

    private sealed record Pass1State(
        FitSnapshot Snapshot,
        IReadOnlyList<FitSlotUsageView> SlotUsage,
        decimal PowergridUsed,
        decimal CpuUsed,
        decimal DroneBandwidthUsed,
        decimal DroneBayUsed,
        IReadOnlyList<FitValidationIssue> Issues,
        IReadOnlyList<string> Warnings);

    private sealed record Pass2State(
        Pass1State Pass1,
        decimal ShieldHitpoints,
        ResistanceProfile ShieldResistances,
        decimal ArmorHitpoints,
        ResistanceProfile ArmorResistances,
        decimal StructureHitpoints,
        ResistanceProfile StructureResistances,
        decimal CapacitorCapacity,
        decimal CapacitorRechargeSeconds,
        decimal MaxVelocity,
        decimal TurretDamageMultiplier,
        decimal ShieldRepairPerSecond,
        decimal ArmorRepairPerSecond,
        decimal StructureRepairPerSecond);

    private sealed record Pass3State(
        Pass2State Pass2,
        decimal AppliedTurretDamagePerSecond,
        DamageProfile AppliedTurretDamageProfilePerSecond,
        decimal DamagePerSecondWithReload,
        decimal AppliedDroneDamagePerSecond,
        DamageProfile AppliedDroneDamageProfilePerSecond,
        decimal AppliedDamagePerSecond,
        DamageProfile AppliedDamageProfilePerSecond,
        decimal VolleyDamage,
        DamageProfile VolleyDamageProfile,
        decimal CapacitorUsagePerSecond,
        decimal WeaponCapacitorUsagePerSecond,
        decimal ActiveTankCapacitorUsagePerSecond,
        decimal PeakCapacitorRechargePerSecond,
        bool CapacitorStable,
        decimal? CapacitorDepletionSeconds,
        decimal WeaponOptimalRangeMeters,
        decimal WeaponFalloffRangeMeters,
        decimal WeaponTracking,
        decimal WeaponSignatureResolutionMeters,
        decimal MissileExplosionRadiusMeters,
        decimal MissileExplosionVelocityMetersPerSecond,
        decimal MissileDamageReductionFactor,
        decimal MissileDamageReductionSensitivity);
}
