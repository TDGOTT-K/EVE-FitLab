using EdenOS.Contracts.Combat;
using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Combat;

public sealed class InMemoryCombatSimulationService(IFitService fitService) : ICombatSimulationService
{
    public UseCaseResult<CombatOutcome> SimulateDuel(SimulateDuelRequest request)
    {
        var traceId = CreateTraceId("combat.simulate_duel");
        var scenarioErrors = ValidateScenario(request.Scenario);
        if (scenarioErrors.Count > 0)
        {
            return UseCaseResult<CombatOutcome>.Failure(
                UseCaseStatus.InvalidInput,
                "Combat scenario is invalid.",
                traceId,
                scenarioErrors);
        }

        var attackerValidation = fitService.Validate(new ValidateFitRequest { Snapshot = request.Scenario.AttackerFit });
        var defenderValidation = fitService.Validate(new ValidateFitRequest { Snapshot = request.Scenario.DefenderFit });

        if (!attackerValidation.IsSuccess || attackerValidation.Data is null)
        {
            return UseCaseResult<CombatOutcome>.Failure(
                UseCaseStatus.InvalidInput,
                "Attacker fit could not be validated.",
                traceId,
                attackerValidation.Errors);
        }

        if (!defenderValidation.IsSuccess || defenderValidation.Data is null)
        {
            return UseCaseResult<CombatOutcome>.Failure(
                UseCaseStatus.InvalidInput,
                "Defender fit could not be validated.",
                traceId,
                defenderValidation.Errors);
        }

        if (!attackerValidation.Data.IsValid || !defenderValidation.Data.IsValid)
        {
            var fitErrors = attackerValidation.Data.Issues
                .Select(issue => $"attacker: {issue.Message}")
                .Concat(defenderValidation.Data.Issues.Select(issue => $"defender: {issue.Message}"))
                .ToArray();

            return UseCaseResult<CombatOutcome>.Failure(
                UseCaseStatus.InvalidInput,
                "Combat simulation requires both fits to pass validation.",
                traceId,
                fitErrors,
                attackerValidation.Warnings.Concat(defenderValidation.Warnings).ToArray());
        }

        var scenario = request.Scenario with
        {
            AttackerFit = attackerValidation.Data.Snapshot,
            DefenderFit = defenderValidation.Data.Snapshot
        };
        var attackerAttributes = attackerValidation.Data.Attributes;
        var defenderAttributes = defenderValidation.Data.Attributes;
        var attackerState = MutableShipState.From(scenario.AttackerFit, attackerAttributes);
        var defenderState = MutableShipState.From(scenario.DefenderFit, defenderAttributes);
        var ticks = new List<CombatTick>(capacity: scenario.MaxTicks);
        CombatTerminationReason terminationReason = CombatTerminationReason.MaxTicksReached;
        string? winnerFitId = null;

        for (var tickNumber = 1; tickNumber <= scenario.MaxTicks; tickNumber++)
        {
            var damageEvents = new List<DamageEvent>(capacity: 2);
            var tankEvents = new List<TankEvent>(capacity: 2);
            var droneEvents = new List<DroneEvent>(capacity: 8);
            var elapsedSeconds = tickNumber * scenario.TickDurationSeconds;
            var tickStartSeconds = elapsedSeconds - scenario.TickDurationSeconds;
            var attackerCapacitorRecharged = RechargeCapacitor(attackerState, attackerAttributes, scenario.TickDurationSeconds);
            var defenderCapacitorRecharged = RechargeCapacitor(defenderState, defenderAttributes, scenario.TickDurationSeconds);

            if (!attackerState.IsDestroyed)
            {
                var damageEvent = ApplyDamage(
                    tickNumber,
                    tickStartSeconds,
                    scenario.TickDurationSeconds,
                    attackerAttributes,
                    attackerState,
                    defenderAttributes,
                    defenderState,
                    scenario.AttackerEngagement);
                damageEvents.Add(damageEvent);
                droneEvents.AddRange(ResolveDroneStateAfterIncomingDamage(tickNumber, defenderState, defenderAttributes, damageEvent));
            }

            if (!defenderState.IsDestroyed)
            {
                var damageEvent = ApplyDamage(
                    tickNumber,
                    tickStartSeconds,
                    scenario.TickDurationSeconds,
                    defenderAttributes,
                    defenderState,
                    attackerAttributes,
                    attackerState,
                    scenario.DefenderEngagement);
                damageEvents.Add(damageEvent);
                droneEvents.AddRange(ResolveDroneStateAfterIncomingDamage(tickNumber, attackerState, attackerAttributes, damageEvent));
            }

            if (!attackerState.IsDestroyed)
            {
                tankEvents.Add(ApplyTank(
                    tickNumber,
                    tickStartSeconds,
                    scenario.TickDurationSeconds,
                    attackerAttributes,
                    attackerState,
                    attackerCapacitorRecharged));
            }

            if (!defenderState.IsDestroyed)
            {
                tankEvents.Add(ApplyTank(
                    tickNumber,
                    tickStartSeconds,
                    scenario.TickDurationSeconds,
                    defenderAttributes,
                    defenderState,
                    defenderCapacitorRecharged));
            }

            ticks.Add(new CombatTick
            {
                TickNumber = tickNumber,
                ElapsedSeconds = elapsedSeconds,
                DamageEvents = damageEvents,
                TankEvents = tankEvents,
                DroneEvents = droneEvents,
                AttackerState = attackerState.ToView(),
                DefenderState = defenderState.ToView()
            });

            if (attackerState.IsDestroyed && defenderState.IsDestroyed)
            {
                terminationReason = CombatTerminationReason.BothDestroyed;
                break;
            }

            if (attackerState.IsDestroyed)
            {
                terminationReason = CombatTerminationReason.AttackerDestroyed;
                winnerFitId = scenario.DefenderFit.FitId;
                break;
            }

            if (defenderState.IsDestroyed)
            {
                terminationReason = CombatTerminationReason.DefenderDestroyed;
                winnerFitId = scenario.AttackerFit.FitId;
                break;
            }
        }

        var outcome = new CombatOutcome
        {
            Scenario = scenario,
            WinnerFitId = winnerFitId,
            TerminationReason = terminationReason,
            TicksElapsed = ticks.Count,
            AttackerAttributes = attackerAttributes,
            DefenderAttributes = defenderAttributes,
            AttackerFinalState = attackerState.ToView(),
            DefenderFinalState = defenderState.ToView(),
            Ticks = ticks,
            ApproximationNotes =
            [
                "Legacy fit snapshots now drive per-module weapon and repair cycles when the snapshot includes cycle, reload, magazine, and typed damage or repair data. Modules without enough timing data still fall back to continuous per-second application.",
                "Dogma-backed fits now project per-module timing, reload, magazine, capacitor, and typed damage data into FitSnapshot.Modules. Target application still uses fit-level turret and missile envelopes instead of fully independent per-module target solutions.",
                "Damage is applied as deterministic four-damage-type pressure against shield, armor, then structure. Turret tracking/range/signature and missile explosion-radius/velocity application use deterministic approximations; random hit quality, wrecking shots, missile flight time, and manual piloting are not modeled.",
                "Layer resistances are resolved from FitAttributeView and applied using the simplified stacking-penalty and effective-layer approximation already used by fitting.",
                "Capacitor now gates module activations on a per-tick or per-cycle basis, but capacitor warfare, overheating side effects, ancillary charges, and partial activation timing inside a tick remain outside this MVP.",
                "Only launched drones contribute damage. Drone loss and recall remain simplified pressure-based approximations rather than full drone AI, target switching, travel time, or relaunch management."
            ]
        };

        return UseCaseResult<CombatOutcome>.Success(
            outcome,
            winnerFitId is null
                ? "Combat simulation reached the configured tick limit."
                : $"Combat simulation completed with winner '{winnerFitId}'.",
            traceId,
            attackerValidation.Warnings.Concat(defenderValidation.Warnings).ToArray());
    }

    private static DamageEvent ApplyDamage(
        int tickNumber,
        decimal tickStartSeconds,
        decimal tickDurationSeconds,
        FitAttributeView attackerAttributes,
        MutableShipState attackerState,
        FitAttributeView defenderAttributes,
        MutableShipState defenderState,
        CombatShipEngagementProfile engagement)
    {
        var moduleWeaponResolution = attackerState.WeaponSystems.Count > 0
            ? ResolveModuleWeaponDamage(attackerState, tickStartSeconds, tickDurationSeconds)
            : ResolveAggregatedTurretDamage(attackerAttributes, attackerState, tickDurationSeconds);
        var droneWeaponResolution = attackerState.DroneAttackSystems.Count > 0
            ? ResolveDroneWeaponDamage(attackerState, tickStartSeconds, tickDurationSeconds)
            : ResolveAggregatedDroneDamage(attackerAttributes, attackerState, tickDurationSeconds);
        var rawDamageProfile = AddDamageProfiles(moduleWeaponResolution.DamageProfile, droneWeaponResolution.DamageProfile);
        var rawDamage = TotalDamage(rawDamageProfile);
        var application = ResolveDamageApplication(
            moduleWeaponResolution,
            droneWeaponResolution,
            attackerAttributes,
            defenderAttributes,
            engagement);
        var applicationAdjustedDamageProfile = application.AppliedDamageProfile;
        var applicationAdjustedDamage = TotalDamage(applicationAdjustedDamageProfile);
        var remainingDamageProfile = applicationAdjustedDamageProfile;
        var mitigatedDamageProfile = ZeroDamageProfile();
        var damageToShieldProfile = ZeroDamageProfile();
        var damageToArmorProfile = ZeroDamageProfile();
        var damageToStructureProfile = ZeroDamageProfile();

        var shieldResolution = ApplyDamageToLayer(
            ref defenderState.ShieldHitpointsRemaining,
            remainingDamageProfile,
            defenderAttributes.ShieldResistances);
        remainingDamageProfile = shieldResolution.OverflowDamageProfile;
        mitigatedDamageProfile = AddDamageProfiles(mitigatedDamageProfile, shieldResolution.MitigatedDamageProfile);
        damageToShieldProfile = shieldResolution.AppliedDamageProfile;

        var armorResolution = ApplyDamageToLayer(
            ref defenderState.ArmorHitpointsRemaining,
            remainingDamageProfile,
            defenderAttributes.ArmorResistances);
        remainingDamageProfile = armorResolution.OverflowDamageProfile;
        mitigatedDamageProfile = AddDamageProfiles(mitigatedDamageProfile, armorResolution.MitigatedDamageProfile);
        damageToArmorProfile = armorResolution.AppliedDamageProfile;

        var structureResolution = ApplyDamageToLayer(
            ref defenderState.StructureHitpointsRemaining,
            remainingDamageProfile,
            defenderAttributes.StructureResistances);
        mitigatedDamageProfile = AddDamageProfiles(mitigatedDamageProfile, structureResolution.MitigatedDamageProfile);
        damageToStructureProfile = structureResolution.AppliedDamageProfile;

        var appliedDamageProfile = AddDamageProfiles(
            AddDamageProfiles(damageToShieldProfile, damageToArmorProfile),
            damageToStructureProfile);

        if (defenderState.StructureHitpointsRemaining <= 0m)
        {
            defenderState.StructureHitpointsRemaining = 0m;
            defenderState.IsDestroyed = true;
        }

        var capacitorSpent = moduleWeaponResolution.CapacitorSpent;
        var weaponActivations = moduleWeaponResolution.Activations;
        var weaponSystemsSuppressed = moduleWeaponResolution.SuppressedCount;
        var weaponSystemsReloading = moduleWeaponResolution.ReloadingCount;
        var droneActivations = droneWeaponResolution.Activations;
        var droneUnitsAttacking = droneWeaponResolution.EngagedUnits;
        var turretSuppressedByCapacitor = moduleWeaponResolution.HasCapacitorDependentOffense && weaponSystemsSuppressed > 0;
        attackerState.TurretWeaponsSuppressedByCapacitor = turretSuppressedByCapacitor;

        return new DamageEvent
        {
            TickNumber = tickNumber,
            SourceFitId = attackerState.FitId,
            TargetFitId = defenderState.FitId,
            RawDamage = decimal.Round(rawDamage, 3),
            RawDamageProfile = RoundDamageProfile(rawDamageProfile),
            ApplicationMultiplier = decimal.Round(application.OverallMultiplier, 6),
            TurretApplicationMultiplier = decimal.Round(application.TurretMultiplier, 6),
            MissileApplicationMultiplier = decimal.Round(application.MissileMultiplier, 6),
            DroneApplicationMultiplier = decimal.Round(application.DroneMultiplier, 6),
            ApplicationAdjustedDamage = decimal.Round(applicationAdjustedDamage, 3),
            ApplicationAdjustedDamageProfile = RoundDamageProfile(applicationAdjustedDamageProfile),
            MitigatedDamage = decimal.Round(TotalDamage(mitigatedDamageProfile), 3),
            MitigatedDamageProfile = RoundDamageProfile(mitigatedDamageProfile),
            AppliedDamage = decimal.Round(TotalDamage(appliedDamageProfile), 3),
            AppliedDamageProfile = RoundDamageProfile(appliedDamageProfile),
            DamageToShield = decimal.Round(TotalDamage(damageToShieldProfile), 3),
            DamageToShieldProfile = RoundDamageProfile(damageToShieldProfile),
            DamageToArmor = decimal.Round(TotalDamage(damageToArmorProfile), 3),
            DamageToArmorProfile = RoundDamageProfile(damageToArmorProfile),
            DamageToStructure = decimal.Round(TotalDamage(damageToStructureProfile), 3),
            DamageToStructureProfile = RoundDamageProfile(damageToStructureProfile),
            CapacitorSpent = decimal.Round(capacitorSpent, 3),
            WeaponActivations = weaponActivations,
            WeaponSystemsSuppressedByCapacitorCount = weaponSystemsSuppressed,
            WeaponSystemsReloadingCount = weaponSystemsReloading,
            DroneActivations = droneActivations,
            DroneUnitsAttacking = droneUnitsAttacking,
            TurretWeaponsSuppressedByCapacitor = turretSuppressedByCapacitor,
            TargetTotalHitpointsRemaining = decimal.Round(defenderState.TotalHitpointsRemaining, 3),
            Notes = BuildDamageNotes(
                rawDamage,
                weaponActivations,
                weaponSystemsSuppressed,
                weaponSystemsReloading,
                droneActivations,
                droneUnitsAttacking,
                TotalDamage(mitigatedDamageProfile),
                application)
        };
    }

    private static TankEvent ApplyTank(
        int tickNumber,
        decimal tickStartSeconds,
        decimal tickDurationSeconds,
        FitAttributeView attributes,
        MutableShipState state,
        decimal capacitorRecharged)
    {
        var capacitorBefore = state.CapacitorRemaining;
        var passiveShieldRepairBudget = Math.Max(0m, attributes.PassiveShieldRechargePerSecond * tickDurationSeconds);
        var passiveShieldRepaired = RepairLayer(ref state.ShieldHitpointsRemaining, state.MaxShieldHitpoints, passiveShieldRepairBudget);
        var activeTankResolution = state.RepairSystems.Count > 0
            ? ResolveModuleRepair(state, tickStartSeconds, tickDurationSeconds)
            : ResolveAggregatedActiveTank(attributes, state, tickDurationSeconds);
        var activeShieldRepaired = RepairLayer(
            ref state.ShieldHitpointsRemaining,
            state.MaxShieldHitpoints,
            activeTankResolution.ShieldRepairAmount);
        var armorRepaired = RepairLayer(
            ref state.ArmorHitpointsRemaining,
            state.MaxArmorHitpoints,
            activeTankResolution.ArmorRepairAmount);
        var structureRepaired = RepairLayer(
            ref state.StructureHitpointsRemaining,
            state.MaxStructureHitpoints,
            activeTankResolution.StructureRepairAmount);
        var activeTankSuppressedByCapacitor = activeTankResolution.HasCapacitorDependentTank && activeTankResolution.SuppressedCount > 0;

        state.ActiveTankSuppressedByCapacitor = activeTankSuppressedByCapacitor;

        return new TankEvent
        {
            TickNumber = tickNumber,
            FitId = state.FitId,
            ShieldRepaired = decimal.Round(passiveShieldRepaired + activeShieldRepaired, 3),
            ArmorRepaired = decimal.Round(armorRepaired, 3),
            StructureRepaired = decimal.Round(structureRepaired, 3),
            PassiveShieldRepaired = decimal.Round(passiveShieldRepaired, 3),
            ActiveShieldRepaired = decimal.Round(activeShieldRepaired, 3),
            CapacitorSpent = decimal.Round(activeTankResolution.CapacitorSpent, 3),
            CapacitorRecharged = decimal.Round(capacitorRecharged, 3),
            CapacitorBefore = decimal.Round(capacitorBefore, 3),
            CapacitorAfter = decimal.Round(state.CapacitorRemaining, 3),
            ActiveTankActivations = activeTankResolution.Activations,
            ActiveTankSystemsSuppressedByCapacitorCount = activeTankResolution.SuppressedCount,
            ActiveTankSuppressedByCapacitor = activeTankSuppressedByCapacitor,
            Notes = BuildTankNotes(
                passiveShieldRepaired,
                activeShieldRepaired,
                armorRepaired,
                structureRepaired,
                activeTankResolution.Activations,
                activeTankResolution.SuppressedCount)
        };
    }

    private static WeaponResolution ResolveModuleWeaponDamage(
        MutableShipState attackerState,
        decimal tickStartSeconds,
        decimal tickDurationSeconds)
    {
        var tickEndSeconds = tickStartSeconds + tickDurationSeconds;
        var damageProfile = ZeroDamageProfile();
        var turretDamageProfile = ZeroDamageProfile();
        var missileDamageProfile = ZeroDamageProfile();
        var directDamageProfile = ZeroDamageProfile();
        var capacitorSpent = 0m;
        var activations = 0;
        var suppressedCount = 0;
        var reloadingCount = 0;
        var hasCapacitorDependentOffense = false;

        foreach (var system in attackerState.WeaponSystems)
        {
            if (system.CapacitorCostPerActivation > 0m || system.CapacitorUsagePerSecond > 0m)
            {
                hasCapacitorDependentOffense = true;
            }

            if (IsReloadingDuringTick(system, tickStartSeconds, tickEndSeconds))
            {
                reloadingCount++;
            }

            if (system.IsContinuous)
            {
                if (TotalDamage(system.DamageProfilePerSecond) <= 0m)
                {
                    continue;
                }

                var capacitorNeed = Math.Max(0m, system.CapacitorUsagePerSecond * tickDurationSeconds);
                if (capacitorNeed > 0m && attackerState.CapacitorRemaining < capacitorNeed)
                {
                    suppressedCount++;
                    continue;
                }

                var contribution = ScaleDamageProfile(system.DamageProfilePerSecond, tickDurationSeconds);
                damageProfile = AddDamageProfiles(damageProfile, contribution);
                AddWeaponContributionByKind(system.ApplicationKind, contribution, ref turretDamageProfile, ref missileDamageProfile, ref directDamageProfile);
                activations++;
                if (capacitorNeed > 0m)
                {
                    var spent = Math.Min(attackerState.CapacitorRemaining, capacitorNeed);
                    attackerState.CapacitorRemaining = Math.Max(0m, attackerState.CapacitorRemaining - spent);
                    capacitorSpent += spent;
                }

                continue;
            }

            while (system.NextActivationTimeSeconds > 0m && system.NextActivationTimeSeconds <= tickEndSeconds)
            {
                if (system.CapacitorCostPerActivation > 0m && attackerState.CapacitorRemaining < system.CapacitorCostPerActivation)
                {
                    suppressedCount++;
                    system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
                    continue;
                }

                damageProfile = AddDamageProfiles(damageProfile, system.VolleyDamageProfile);
                AddWeaponContributionByKind(system.ApplicationKind, system.VolleyDamageProfile, ref turretDamageProfile, ref missileDamageProfile, ref directDamageProfile);
                activations++;
                if (system.CapacitorCostPerActivation > 0m)
                {
                    var spent = Math.Min(attackerState.CapacitorRemaining, system.CapacitorCostPerActivation);
                    attackerState.CapacitorRemaining = Math.Max(0m, attackerState.CapacitorRemaining - spent);
                    capacitorSpent += spent;
                }

                AdvanceWeaponSystemAfterActivation(system);
            }
        }

        return new WeaponResolution(
            RoundDamageProfile(damageProfile),
            RoundDamageProfile(turretDamageProfile),
            RoundDamageProfile(missileDamageProfile),
            RoundDamageProfile(directDamageProfile),
            decimal.Round(capacitorSpent, 6),
            activations,
            suppressedCount,
            reloadingCount,
            0,
            hasCapacitorDependentOffense);
    }

    private static WeaponResolution ResolveAggregatedTurretDamage(
        FitAttributeView attackerAttributes,
        MutableShipState attackerState,
        decimal tickDurationSeconds)
    {
        var turretCapacitorNeed = Math.Max(0m, attackerAttributes.WeaponCapacitorUsagePerSecond * tickDurationSeconds);
        var turretSuppressedByCapacitor = turretCapacitorNeed > 0m && attackerState.CapacitorRemaining < turretCapacitorNeed;
        if (turretSuppressedByCapacitor)
        {
            return new WeaponResolution(
                ZeroDamageProfile(),
                ZeroDamageProfile(),
                ZeroDamageProfile(),
                ZeroDamageProfile(),
                0m,
                0,
                1,
                0,
                0,
                turretCapacitorNeed > 0m);
        }

        var damageProfile = ScaleDamageProfile(
            SubtractDamageProfiles(
                attackerAttributes.AppliedDamageProfilePerSecond,
                attackerAttributes.AppliedDroneDamageProfilePerSecond),
            Math.Max(0m, tickDurationSeconds));
        var missileDamageProfile = attackerAttributes.MissileExplosionRadiusMeters > 0m ||
                                   attackerAttributes.MissileExplosionVelocityMetersPerSecond > 0m
            ? damageProfile
            : ZeroDamageProfile();
        var turretDamageProfile = TotalDamage(missileDamageProfile) > 0m
            ? ZeroDamageProfile()
            : damageProfile;
        var capacitorSpent = 0m;
        if (turretCapacitorNeed > 0m)
        {
            capacitorSpent = Math.Min(attackerState.CapacitorRemaining, turretCapacitorNeed);
            attackerState.CapacitorRemaining = Math.Max(0m, attackerState.CapacitorRemaining - capacitorSpent);
        }

        return new WeaponResolution(
            RoundDamageProfile(damageProfile),
            RoundDamageProfile(turretDamageProfile),
            RoundDamageProfile(missileDamageProfile),
            ZeroDamageProfile(),
            decimal.Round(capacitorSpent, 6),
            TotalDamage(damageProfile) > 0m ? 1 : 0,
            0,
            0,
            0,
            turretCapacitorNeed > 0m);
    }

    private static WeaponResolution ResolveDroneWeaponDamage(
        MutableShipState attackerState,
        decimal tickStartSeconds,
        decimal tickDurationSeconds)
    {
        var tickEndSeconds = tickStartSeconds + tickDurationSeconds;
        var damageProfile = ZeroDamageProfile();
        var activations = 0;
        var engagedUnits = 0;
        var engagedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var system in attackerState.DroneAttackSystems)
        {
            if (system.Wing.LaunchedCount <= 0)
            {
                continue;
            }

            if (system.IsContinuous)
            {
                var contribution = ScaleDamageProfile(system.DamageProfilePerSecond, tickDurationSeconds * system.Wing.LaunchedCount);
                if (TotalDamage(contribution) <= 0m)
                {
                    continue;
                }

                damageProfile = AddDamageProfiles(damageProfile, contribution);
                activations++;
                if (engagedSystems.Add(system.SystemId))
                {
                    engagedUnits += system.Wing.LaunchedCount;
                }

                continue;
            }

            while (system.NextActivationTimeSeconds > 0m && system.NextActivationTimeSeconds <= tickEndSeconds)
            {
                if (system.Wing.LaunchedCount <= 0)
                {
                    system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
                    break;
                }

                var contribution = ScaleDamageProfile(system.VolleyDamageProfilePerUnit, system.Wing.LaunchedCount);
                damageProfile = AddDamageProfiles(damageProfile, contribution);
                activations++;
                if (engagedSystems.Add(system.SystemId))
                {
                    engagedUnits += system.Wing.LaunchedCount;
                }

                system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
            }
        }

        return new WeaponResolution(
            RoundDamageProfile(damageProfile),
            ZeroDamageProfile(),
            ZeroDamageProfile(),
            ZeroDamageProfile(),
            0m,
            activations,
            0,
            0,
            engagedUnits,
            false);
    }

    private static WeaponResolution ResolveAggregatedDroneDamage(
        FitAttributeView attributes,
        MutableShipState attackerState,
        decimal tickDurationSeconds)
    {
        var totalInitialLaunched = attackerState.InitialLaunchedDroneCount;
        var currentLaunched = attackerState.TotalLaunchedDroneCount;
        var droneFactor = totalInitialLaunched > 0
            ? decimal.Clamp((decimal)currentLaunched / totalInitialLaunched, 0m, 1m)
            : 0m;
        var damageProfile = ScaleDamageProfile(
            attributes.AppliedDroneDamageProfilePerSecond,
            Math.Max(0m, tickDurationSeconds) * droneFactor);
        return new WeaponResolution(
            RoundDamageProfile(damageProfile),
            ZeroDamageProfile(),
            ZeroDamageProfile(),
            ZeroDamageProfile(),
            0m,
            TotalDamage(damageProfile) > 0m ? 1 : 0,
            0,
            0,
            currentLaunched,
            false);
    }

    private static TankResolution ResolveModuleRepair(
        MutableShipState state,
        decimal tickStartSeconds,
        decimal tickDurationSeconds)
    {
        var tickEndSeconds = tickStartSeconds + tickDurationSeconds;
        var shieldRepairAmount = 0m;
        var armorRepairAmount = 0m;
        var structureRepairAmount = 0m;
        var capacitorSpent = 0m;
        var activations = 0;
        var suppressedCount = 0;
        var hasCapacitorDependentTank = false;

        foreach (var system in state.RepairSystems)
        {
            if (system.CapacitorCostPerActivation > 0m || system.CapacitorUsagePerSecond > 0m)
            {
                hasCapacitorDependentTank = true;
            }

            if (system.IsContinuous)
            {
                var capacitorNeed = Math.Max(0m, system.CapacitorUsagePerSecond * tickDurationSeconds);
                if (capacitorNeed > 0m && state.CapacitorRemaining < capacitorNeed)
                {
                    suppressedCount++;
                    continue;
                }

                shieldRepairAmount += system.ShieldRepairPerSecond * tickDurationSeconds;
                armorRepairAmount += system.ArmorRepairPerSecond * tickDurationSeconds;
                structureRepairAmount += system.StructureRepairPerSecond * tickDurationSeconds;
                activations++;
                if (capacitorNeed > 0m)
                {
                    var spent = Math.Min(state.CapacitorRemaining, capacitorNeed);
                    state.CapacitorRemaining = Math.Max(0m, state.CapacitorRemaining - spent);
                    capacitorSpent += spent;
                }

                continue;
            }

            while (system.NextActivationTimeSeconds > 0m && system.NextActivationTimeSeconds <= tickEndSeconds)
            {
                if (system.CapacitorCostPerActivation > 0m && state.CapacitorRemaining < system.CapacitorCostPerActivation)
                {
                    suppressedCount++;
                    system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
                    continue;
                }

                shieldRepairAmount += system.ShieldRepairPerActivation;
                armorRepairAmount += system.ArmorRepairPerActivation;
                structureRepairAmount += system.StructureRepairPerActivation;
                activations++;
                if (system.CapacitorCostPerActivation > 0m)
                {
                    var spent = Math.Min(state.CapacitorRemaining, system.CapacitorCostPerActivation);
                    state.CapacitorRemaining = Math.Max(0m, state.CapacitorRemaining - spent);
                    capacitorSpent += spent;
                }

                system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
            }
        }

        return new TankResolution(
            decimal.Round(shieldRepairAmount, 6),
            decimal.Round(armorRepairAmount, 6),
            decimal.Round(structureRepairAmount, 6),
            decimal.Round(capacitorSpent, 6),
            activations,
            suppressedCount,
            hasCapacitorDependentTank);
    }

    private static TankResolution ResolveAggregatedActiveTank(
        FitAttributeView attributes,
        MutableShipState state,
        decimal tickDurationSeconds)
    {
        var shieldRepairBudget = Math.Max(0m, (attributes.ShieldRepairPerSecond - attributes.PassiveShieldRechargePerSecond) * tickDurationSeconds);
        var armorRepairBudget = Math.Max(0m, attributes.ArmorRepairPerSecond * tickDurationSeconds);
        var structureRepairBudget = Math.Max(0m, attributes.StructureRepairPerSecond * tickDurationSeconds);
        var activeTankCapacitorNeed = Math.Max(0m, attributes.ActiveTankCapacitorUsagePerSecond * tickDurationSeconds);
        var activeTankSuppressedByCapacitor = activeTankCapacitorNeed > 0m && state.CapacitorRemaining < activeTankCapacitorNeed;

        if (activeTankSuppressedByCapacitor)
        {
            return new TankResolution(
                0m,
                0m,
                0m,
                0m,
                0,
                1,
                activeTankCapacitorNeed > 0m);
        }

        var capacitorSpent = 0m;
        if (activeTankCapacitorNeed > 0m)
        {
            capacitorSpent = Math.Min(state.CapacitorRemaining, activeTankCapacitorNeed);
            state.CapacitorRemaining = Math.Max(0m, state.CapacitorRemaining - capacitorSpent);
        }

        var hasActiveTank = shieldRepairBudget > 0m || armorRepairBudget > 0m || structureRepairBudget > 0m;
        return new TankResolution(
            decimal.Round(shieldRepairBudget, 6),
            decimal.Round(armorRepairBudget, 6),
            decimal.Round(structureRepairBudget, 6),
            decimal.Round(capacitorSpent, 6),
            hasActiveTank ? 1 : 0,
            0,
            activeTankCapacitorNeed > 0m);
    }

    private static DamageApplicationResolution ResolveDamageApplication(
        WeaponResolution moduleWeaponResolution,
        WeaponResolution droneWeaponResolution,
        FitAttributeView attackerAttributes,
        FitAttributeView defenderAttributes,
        CombatShipEngagementProfile engagement)
    {
        var manualMultiplier = ClampApplicationMultiplier(engagement.ManualApplicationMultiplier <= 0m
            ? 1m
            : engagement.ManualApplicationMultiplier);
        var turretMultiplier = ClampApplicationMultiplier(ResolveTurretApplication(attackerAttributes, defenderAttributes, engagement) * manualMultiplier);
        var missileMultiplier = ClampApplicationMultiplier(ResolveMissileApplication(attackerAttributes, defenderAttributes, engagement) * manualMultiplier);
        var droneMultiplier = ClampApplicationMultiplier(ResolveDroneApplication(attackerAttributes, defenderAttributes, engagement) * manualMultiplier);
        var directMultiplier = manualMultiplier;
        var turretProfile = ScaleDamageProfile(moduleWeaponResolution.TurretDamageProfile, turretMultiplier);
        var missileProfile = ScaleDamageProfile(moduleWeaponResolution.MissileDamageProfile, missileMultiplier);
        var directProfile = ScaleDamageProfile(moduleWeaponResolution.DirectDamageProfile, directMultiplier);
        var droneProfile = ScaleDamageProfile(droneWeaponResolution.DamageProfile, droneMultiplier);
        var appliedProfile = AddDamageProfiles(
            AddDamageProfiles(turretProfile, missileProfile),
            AddDamageProfiles(directProfile, droneProfile));
        var rawTotal = TotalDamage(AddDamageProfiles(moduleWeaponResolution.DamageProfile, droneWeaponResolution.DamageProfile));
        var appliedTotal = TotalDamage(appliedProfile);

        return new DamageApplicationResolution(
            AppliedDamageProfile: RoundDamageProfile(appliedProfile),
            OverallMultiplier: rawTotal > 0m ? appliedTotal / rawTotal : 1m,
            TurretMultiplier: turretMultiplier,
            MissileMultiplier: missileMultiplier,
            DroneMultiplier: droneMultiplier,
            RangeMeters: Math.Max(0m, engagement.RangeMeters),
            AngularVelocityRadiansPerSecond: Math.Max(0m, engagement.AngularVelocityRadiansPerSecond),
            RelativeVelocityMetersPerSecond: Math.Max(0m, engagement.RelativeVelocityMetersPerSecond),
            TargetSignatureRadiusMeters: ResolveTargetSignatureRadius(defenderAttributes, engagement),
            Notes: BuildApplicationNotes(turretMultiplier, missileMultiplier, droneMultiplier, engagement));
    }

    private static decimal ResolveTurretApplication(
        FitAttributeView attackerAttributes,
        FitAttributeView defenderAttributes,
        CombatShipEngagementProfile engagement)
    {
        var tracking = Math.Max(0m, attackerAttributes.WeaponTracking);
        var range = Math.Max(0m, engagement.RangeMeters);
        var optimal = Math.Max(0m, attackerAttributes.WeaponOptimalRangeMeters);
        var falloff = Math.Max(0m, attackerAttributes.WeaponFalloffRangeMeters);
        var signatureResolution = attackerAttributes.WeaponSignatureResolutionMeters > 0m
            ? attackerAttributes.WeaponSignatureResolutionMeters
            : 0m;
        var targetSignature = ResolveTargetSignatureRadius(defenderAttributes, engagement);

        if (tracking <= 0m && optimal <= 0m && falloff <= 0m)
        {
            return 1m;
        }

        if (range <= 0m && engagement.AngularVelocityRadiansPerSecond <= 0m)
        {
            return 1m;
        }

        var trackingTerm = 0m;
        if (tracking > 0m && targetSignature > 0m)
        {
            trackingTerm = engagement.AngularVelocityRadiansPerSecond * signatureResolution / (tracking * targetSignature);
        }

        var rangeTerm = 0m;
        if (falloff > 0m && range > optimal)
        {
            rangeTerm = (range - optimal) / falloff;
        }
        else if (falloff <= 0m && optimal > 0m && range > optimal)
        {
            return 0m;
        }

        var exponent = (double)((trackingTerm * trackingTerm) + (rangeTerm * rangeTerm));
        return ClampApplicationMultiplier((decimal)Math.Pow(0.5d, exponent));
    }

    private static decimal ResolveMissileApplication(
        FitAttributeView attackerAttributes,
        FitAttributeView defenderAttributes,
        CombatShipEngagementProfile engagement)
    {
        var explosionRadius = Math.Max(0m, attackerAttributes.MissileExplosionRadiusMeters);
        var explosionVelocity = Math.Max(0m, attackerAttributes.MissileExplosionVelocityMetersPerSecond);
        var targetSignature = ResolveTargetSignatureRadius(defenderAttributes, engagement);
        var relativeVelocity = Math.Max(0m, engagement.RelativeVelocityMetersPerSecond);

        if (explosionRadius <= 0m && explosionVelocity <= 0m)
        {
            return 1m;
        }

        if (relativeVelocity <= 0m && engagement.SignatureRadiusOverrideMeters is null)
        {
            return 1m;
        }

        var signatureFactor = explosionRadius > 0m
            ? Math.Min(1m, targetSignature / explosionRadius)
            : 1m;
        if (relativeVelocity <= 0m || explosionVelocity <= 0m)
        {
            return ClampApplicationMultiplier(signatureFactor);
        }

        var drf = attackerAttributes.MissileDamageReductionFactor > 0m
            ? attackerAttributes.MissileDamageReductionFactor
            : 1m;
        var drs = attackerAttributes.MissileDamageReductionSensitivity > 0m
            ? attackerAttributes.MissileDamageReductionSensitivity
            : 1m;
        var velocityRatio = explosionVelocity / Math.Max(1m, relativeVelocity);
        var velocityFactor = signatureFactor * (decimal)Math.Pow(
            (double)Math.Max(0.000001m, velocityRatio),
            (double)Math.Max(0.01m, drf * drs));

        return ClampApplicationMultiplier(Math.Min(signatureFactor, velocityFactor));
    }

    private static decimal ResolveDroneApplication(
        FitAttributeView attackerAttributes,
        FitAttributeView defenderAttributes,
        CombatShipEngagementProfile engagement)
    {
        var tracking = Math.Max(0m, attackerAttributes.WeaponTracking);
        var range = Math.Max(0m, engagement.RangeMeters);
        var optimal = Math.Max(0m, attackerAttributes.WeaponOptimalRangeMeters);

        if (tracking <= 0m && optimal <= 0m)
        {
            return 1m;
        }

        // Drones still do not model travel, orbit, AI, or target switching; use a light turret-like envelope.
        return ResolveTurretApplication(attackerAttributes, defenderAttributes, engagement);
    }

    private static decimal ResolveTargetSignatureRadius(
        FitAttributeView defenderAttributes,
        CombatShipEngagementProfile engagement)
    {
        if (engagement.SignatureRadiusOverrideMeters is > 0m)
        {
            return engagement.SignatureRadiusOverrideMeters.Value;
        }

        return defenderAttributes.SignatureRadius > 0m ? defenderAttributes.SignatureRadius : 0m;
    }

    private static IReadOnlyList<DroneEvent> ResolveDroneStateAfterIncomingDamage(
        int tickNumber,
        MutableShipState state,
        FitAttributeView attributes,
        DamageEvent damageEvent)
    {
        if (state.DroneWings.Count == 0)
        {
            return Array.Empty<DroneEvent>();
        }

        var events = new List<DroneEvent>();
        if (state.IsDestroyed)
        {
            foreach (var wing in state.DroneWings.Where(wing => wing.LaunchedCount > 0))
            {
                var launchedBefore = wing.LaunchedCount;
                var bayBefore = wing.BayCount;
                var lostBefore = wing.LostCount;
                events.Add(BuildDroneEvent(
                    tickNumber,
                    state.FitId,
                    wing,
                    DroneEventKind.LostOnShipDestruction,
                    wing.LaunchedCount,
                    launchedBefore,
                    0,
                    bayBefore,
                    wing.BayCount,
                    lostBefore,
                    wing.LostCount + wing.LaunchedCount,
                    "Launched drones were lost when the parent ship was destroyed."));
                wing.LostCount += wing.LaunchedCount;
                wing.LaunchedCount = 0;
                wing.PendingExposureDamage = 0m;
            }

            return events;
        }

        var totalLaunched = state.TotalLaunchedDroneCount;
        if (totalLaunched > 0 && damageEvent.AppliedDamage > 0m)
        {
            var exposureBudget = ComputeDroneExposureDamage(damageEvent);
            if (exposureBudget > 0m)
            {
                foreach (var wing in state.DroneWings.Where(wing => wing.LaunchedCount > 0))
                {
                    var launchedBefore = wing.LaunchedCount;
                    var bayBefore = wing.BayCount;
                    var lostBefore = wing.LostCount;
                    var share = totalLaunched > 0 ? (decimal)wing.LaunchedCount / totalLaunched : 0m;
                    wing.PendingExposureDamage += exposureBudget * share;
                    var destroyed = wing.DurabilityPerUnit > 0m
                        ? Math.Min(wing.LaunchedCount, (int)decimal.Floor(wing.PendingExposureDamage / wing.DurabilityPerUnit))
                        : 0;
                    if (destroyed <= 0)
                    {
                        continue;
                    }

                    wing.PendingExposureDamage -= destroyed * wing.DurabilityPerUnit;
                    wing.LaunchedCount -= destroyed;
                    wing.LostCount += destroyed;
                    events.Add(BuildDroneEvent(
                        tickNumber,
                        state.FitId,
                        wing,
                        DroneEventKind.Destroyed,
                        destroyed,
                        launchedBefore,
                        wing.LaunchedCount,
                        bayBefore,
                        wing.BayCount,
                        lostBefore,
                        wing.LostCount,
                        "Launched drones took simplified exposure damage during incoming fire and were destroyed."));
                }
            }
        }

        if (ShouldRecallDrones(state) && state.TotalLaunchedDroneCount > 0)
        {
            foreach (var wing in state.DroneWings.Where(wing => wing.LaunchedCount > 0))
            {
                var launchedBefore = wing.LaunchedCount;
                var bayBefore = wing.BayCount;
                var lostBefore = wing.LostCount;
                wing.BayCount += wing.LaunchedCount;
                wing.LaunchedCount = 0;
                wing.PendingExposureDamage = 0m;
                events.Add(BuildDroneEvent(
                    tickNumber,
                    state.FitId,
                    wing,
                    DroneEventKind.Recalled,
                    launchedBefore,
                    launchedBefore,
                    0,
                    bayBefore,
                    wing.BayCount,
                    lostBefore,
                    wing.LostCount,
                    "The ship auto-recalled launched drones after entering a high-pressure defensive state, releasing drone bandwidth."));
            }
        }

        return events;
    }

    private static bool ShouldRecallDrones(MutableShipState state)
    {
        if (state.TotalLaunchedDroneCount <= 0)
        {
            return false;
        }

        if (state.MaxArmorHitpoints > 0m && state.ArmorHitpointsRemaining < state.MaxArmorHitpoints)
        {
            return true;
        }

        return state.MaxShieldHitpoints > 0m &&
               state.ShieldHitpointsRemaining <= state.MaxShieldHitpoints * 0.25m;
    }

    private static decimal ComputeDroneExposureDamage(DamageEvent damageEvent)
    {
        if (damageEvent.AppliedDamage <= 0m)
        {
            return 0m;
        }

        var factor = damageEvent.DamageToStructure > 0m
            ? 1.0m
            : damageEvent.DamageToArmor > 0m
                ? 0.8m
                : 0.5m;
        return decimal.Round(damageEvent.AppliedDamage * factor, 6);
    }

    private static DroneEvent BuildDroneEvent(
        int tickNumber,
        string fitId,
        MutableDroneWingState wing,
        DroneEventKind kind,
        int quantityChanged,
        int launchedBefore,
        int launchedAfter,
        int bayBefore,
        int bayAfter,
        int lostBefore,
        int lostAfter,
        string notes) =>
        new()
        {
            TickNumber = tickNumber,
            FitId = fitId,
            DroneTypeId = wing.DroneTypeId,
            DroneName = wing.DroneName,
            Kind = kind,
            QuantityChanged = quantityChanged,
            LaunchedBefore = launchedBefore,
            LaunchedAfter = launchedAfter,
            BayBefore = bayBefore,
            BayAfter = bayAfter,
            LostBefore = lostBefore,
            LostAfter = lostAfter,
            Notes = notes
        };

    private static decimal RechargeCapacitor(
        MutableShipState state,
        FitAttributeView attributes,
        decimal tickDurationSeconds)
    {
        if (tickDurationSeconds <= 0m ||
            attributes.PeakCapacitorRechargePerSecond <= 0m ||
            state.CapacitorCapacity <= 0m ||
            state.CapacitorRemaining >= state.CapacitorCapacity)
        {
            return 0m;
        }

        var recharge = Math.Max(0m, attributes.PeakCapacitorRechargePerSecond * tickDurationSeconds);
        if (recharge <= 0m)
        {
            return 0m;
        }

        var before = state.CapacitorRemaining;
        state.CapacitorRemaining = Math.Min(state.CapacitorCapacity, state.CapacitorRemaining + recharge);
        return Math.Max(0m, state.CapacitorRemaining - before);
    }

    private static LayerDamageResolution ApplyDamageToLayer(
        ref decimal currentHitpoints,
        DamageProfile incomingDamageProfile,
        ResistanceProfile resistances)
    {
        if (currentHitpoints <= 0m)
        {
            return new LayerDamageResolution(ZeroDamageProfile(), ZeroDamageProfile(), incomingDamageProfile);
        }

        if (TotalDamage(incomingDamageProfile) <= 0m)
        {
            return new LayerDamageResolution(ZeroDamageProfile(), ZeroDamageProfile(), ZeroDamageProfile());
        }

        var mitigatedDamageProfile = ApplyResistances(incomingDamageProfile, resistances);
        var effectiveDamageProfile = SubtractDamageProfiles(incomingDamageProfile, mitigatedDamageProfile);
        var effectiveDamage = TotalDamage(effectiveDamageProfile);
        if (effectiveDamage <= 0m)
        {
            return new LayerDamageResolution(ZeroDamageProfile(), mitigatedDamageProfile, ZeroDamageProfile());
        }

        if (currentHitpoints >= effectiveDamage)
        {
            currentHitpoints -= effectiveDamage;
            return new LayerDamageResolution(effectiveDamageProfile, mitigatedDamageProfile, ZeroDamageProfile());
        }

        var appliedRatio = currentHitpoints / effectiveDamage;
        var appliedDamageProfile = ScaleDamageProfile(effectiveDamageProfile, appliedRatio);
        // Only the raw fraction consumed by this layer is mitigated here.
        // The remaining raw damage reaches the next layer with its own resistances.
        var overflowDamageProfile = ScaleDamageProfile(incomingDamageProfile, 1m - appliedRatio);
        mitigatedDamageProfile = ScaleDamageProfile(mitigatedDamageProfile, appliedRatio);
        currentHitpoints = 0m;
        return new LayerDamageResolution(appliedDamageProfile, mitigatedDamageProfile, overflowDamageProfile);
    }

    private static decimal RepairLayer(ref decimal currentHitpoints, decimal maxHitpoints, decimal repairBudget)
    {
        if (repairBudget <= 0m || currentHitpoints >= maxHitpoints)
        {
            return 0m;
        }

        var repaired = Math.Min(maxHitpoints - currentHitpoints, repairBudget);
        currentHitpoints += repaired;
        return repaired;
    }

    private static IReadOnlyList<string> ValidateScenario(CombatScenario scenario)
    {
        var errors = new List<string>();
        if (scenario.TickDurationSeconds <= 0m)
        {
            errors.Add("scenario.tick_duration_seconds must be greater than zero.");
        }

        if (scenario.MaxTicks <= 0)
        {
            errors.Add("scenario.max_ticks must be greater than zero.");
        }

        return errors;
    }

    private static string CreateTraceId(string operation) => $"{operation}:{Guid.NewGuid():N}";

    private static DamageProfile ApplyResistances(DamageProfile incomingDamageProfile, ResistanceProfile resistances) =>
        new()
        {
            Em = decimal.Round(incomingDamageProfile.Em * ClampResistance(resistances.EmPercent), 6),
            Thermal = decimal.Round(incomingDamageProfile.Thermal * ClampResistance(resistances.ThermalPercent), 6),
            Kinetic = decimal.Round(incomingDamageProfile.Kinetic * ClampResistance(resistances.KineticPercent), 6),
            Explosive = decimal.Round(incomingDamageProfile.Explosive * ClampResistance(resistances.ExplosivePercent), 6)
        };

    private static decimal ClampApplicationMultiplier(decimal value) => Math.Clamp(value, 0m, 1m);

    private static void AddWeaponContributionByKind(
        WeaponApplicationKind kind,
        DamageProfile contribution,
        ref DamageProfile turretDamageProfile,
        ref DamageProfile missileDamageProfile,
        ref DamageProfile directDamageProfile)
    {
        switch (kind)
        {
            case WeaponApplicationKind.Turret:
                turretDamageProfile = AddDamageProfiles(turretDamageProfile, contribution);
                break;
            case WeaponApplicationKind.Missile:
                missileDamageProfile = AddDamageProfiles(missileDamageProfile, contribution);
                break;
            default:
                directDamageProfile = AddDamageProfiles(directDamageProfile, contribution);
                break;
        }
    }

    private static bool IsReloadingDuringTick(MutableWeaponSystemState system, decimal tickStartSeconds, decimal tickEndSeconds)
    {
        if (system.ReloadStartedAtSeconds is null || system.ReloadUntilSeconds is null)
        {
            return false;
        }

        return system.ReloadUntilSeconds > tickStartSeconds && system.ReloadStartedAtSeconds < tickEndSeconds;
    }

    private static void AdvanceWeaponSystemAfterActivation(MutableWeaponSystemState system)
    {
        if (system.ShotsPerMagazine != int.MaxValue)
        {
            system.ShotsRemainingInMagazine--;
            if (system.ShotsRemainingInMagazine <= 0 && system.ReloadTimeSeconds > 0m)
            {
                system.ReloadStartedAtSeconds = system.NextActivationTimeSeconds;
                system.ReloadUntilSeconds = system.NextActivationTimeSeconds + system.ReloadTimeSeconds;
                system.ShotsRemainingInMagazine = system.ShotsPerMagazine;
                system.NextActivationTimeSeconds = system.ReloadUntilSeconds.Value + Math.Max(system.CycleTimeSeconds, 0.001m);
                return;
            }
        }

        system.ReloadStartedAtSeconds = null;
        system.ReloadUntilSeconds = null;
        system.NextActivationTimeSeconds += Math.Max(system.CycleTimeSeconds, 0.001m);
    }

    private static string? BuildDamageNotes(
        decimal rawDamage,
        int weaponActivations,
        int weaponSystemsSuppressed,
        int weaponSystemsReloading,
        int droneActivations,
        int droneUnitsAttacking,
        decimal mitigatedDamage,
        DamageApplicationResolution application)
    {
        if (rawDamage <= 0m)
        {
            return weaponSystemsSuppressed > 0
                ? "No damage was applied on this tick because capacitor-gated weapon systems could not activate."
                : "No applied damage on this tick because no weapon or drone system completed a damage cycle inside the tick window.";
        }

        var noteParts = new List<string>();
        if (weaponActivations > 0)
        {
            noteParts.Add($"{weaponActivations} weapon activation(s) resolved");
        }

        if (weaponSystemsReloading > 0)
        {
            noteParts.Add($"{weaponSystemsReloading} weapon system(s) were reloading");
        }

        if (weaponSystemsSuppressed > 0)
        {
            noteParts.Add($"{weaponSystemsSuppressed} weapon system(s) were suppressed by capacitor");
        }

        if (droneActivations > 0)
        {
            noteParts.Add($"{droneActivations} drone attack cycle(s) resolved across {droneUnitsAttacking} launched drone(s)");
        }

        if (application.OverallMultiplier < 0.999m)
        {
            noteParts.Add(
                $"application multiplier {application.OverallMultiplier:0.###} at {application.RangeMeters:0.#}m range, {application.AngularVelocityRadiansPerSecond:0.###}rad/s angular, {application.RelativeVelocityMetersPerSecond:0.#}m/s relative, {application.TargetSignatureRadiusMeters:0.#}m target signature");
        }

        if (mitigatedDamage > 0m)
        {
            noteParts.Add("incoming damage was reduced by projected layer resistances");
        }

        if (!string.IsNullOrWhiteSpace(application.Notes))
        {
            noteParts.Add(application.Notes);
        }

        return noteParts.Count == 0 ? null : string.Join("; ", noteParts) + ".";
    }

    private static string? BuildApplicationNotes(
        decimal turretMultiplier,
        decimal missileMultiplier,
        decimal droneMultiplier,
        CombatShipEngagementProfile engagement)
    {
        var parts = new List<string>();
        if (turretMultiplier < 0.999m)
        {
            parts.Add($"turret deterministic tracking/range application {turretMultiplier:0.###}");
        }

        if (missileMultiplier < 0.999m)
        {
            parts.Add($"missile deterministic signature/velocity application {missileMultiplier:0.###}");
        }

        if (droneMultiplier < 0.999m)
        {
            parts.Add($"drone simplified turret-like application {droneMultiplier:0.###}");
        }

        if (engagement.ManualApplicationMultiplier > 0m && engagement.ManualApplicationMultiplier < 0.999m)
        {
            parts.Add($"manual application override {engagement.ManualApplicationMultiplier:0.###}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildTankNotes(
        decimal passiveShieldRepaired,
        decimal activeShieldRepaired,
        decimal armorRepaired,
        decimal structureRepaired,
        int activeTankActivations,
        int suppressedCount)
    {
        if (passiveShieldRepaired + activeShieldRepaired + armorRepaired + structureRepaired <= 0m)
        {
            return suppressedCount > 0
                ? "No tank repaired damage on this tick because active repair systems were suppressed by capacitor and passive recharge had no missing shield to restore."
                : "No passive or active tank repaired damage on this tick.";
        }

        var noteParts = new List<string>();
        if (passiveShieldRepaired > 0m)
        {
            noteParts.Add("passive shield recharge restored hitpoints");
        }

        if (activeTankActivations > 0)
        {
            noteParts.Add($"{activeTankActivations} active tank activation(s) resolved");
        }

        if (suppressedCount > 0)
        {
            noteParts.Add($"{suppressedCount} active tank system(s) were suppressed by capacitor");
        }

        return noteParts.Count == 0 ? null : string.Join("; ", noteParts) + ".";
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

    private static decimal ClampResistance(decimal value) => Math.Clamp(value, 0m, 0.95m);

    private static DamageProfile ZeroDamageProfile() => new();

    private static DamageProfile AddDamageProfiles(DamageProfile left, DamageProfile right) =>
        new()
        {
            Em = left.Em + right.Em,
            Thermal = left.Thermal + right.Thermal,
            Kinetic = left.Kinetic + right.Kinetic,
            Explosive = left.Explosive + right.Explosive
        };

    private static DamageProfile SubtractDamageProfiles(DamageProfile left, DamageProfile right) =>
        new()
        {
            Em = Math.Max(0m, left.Em - right.Em),
            Thermal = Math.Max(0m, left.Thermal - right.Thermal),
            Kinetic = Math.Max(0m, left.Kinetic - right.Kinetic),
            Explosive = Math.Max(0m, left.Explosive - right.Explosive)
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

    private static decimal TotalDamage(DamageProfile profile) =>
        Math.Max(0m, profile.Em)
        + Math.Max(0m, profile.Thermal)
        + Math.Max(0m, profile.Kinetic)
        + Math.Max(0m, profile.Explosive);

    private static DamageProfile RoundDamageProfile(DamageProfile profile) =>
        new()
        {
            Em = decimal.Round(profile.Em, 3),
            Thermal = decimal.Round(profile.Thermal, 3),
            Kinetic = decimal.Round(profile.Kinetic, 3),
            Explosive = decimal.Round(profile.Explosive, 3)
        };

    private sealed class MutableShipState
    {
        public required string FitId { get; init; }
        public required decimal DroneBandwidthAvailable { get; init; }
        public required decimal DroneBayAvailable { get; init; }
        public required int InitialLaunchedDroneCount { get; init; }
        public required List<MutableDroneWingState> DroneWings { get; init; }
        public required List<MutableDroneAttackSystemState> DroneAttackSystems { get; init; }
        public required List<MutableWeaponSystemState> WeaponSystems { get; init; }
        public required List<MutableRepairSystemState> RepairSystems { get; init; }
        public decimal MaxShieldHitpoints;
        public decimal MaxArmorHitpoints;
        public decimal MaxStructureHitpoints;
        public decimal CapacitorCapacity;
        public decimal ShieldHitpointsRemaining;
        public decimal ArmorHitpointsRemaining;
        public decimal StructureHitpointsRemaining;
        public decimal CapacitorRemaining;
        public bool TurretWeaponsSuppressedByCapacitor;
        public bool ActiveTankSuppressedByCapacitor;
        public bool IsDestroyed;

        public int TotalLaunchedDroneCount => DroneWings.Sum(wing => wing.LaunchedCount);
        public int TotalDronesInBayCount => DroneWings.Sum(wing => wing.BayCount);
        public int TotalLostDroneCount => DroneWings.Sum(wing => wing.LostCount);
        public decimal CurrentDroneBandwidthUsed => DroneWings.Sum(wing => wing.BandwidthPerUnit * wing.LaunchedCount);
        public decimal CurrentDroneBayUsed => DroneWings.Sum(wing => wing.VolumePerUnit * wing.BayCount);
        public decimal TotalHitpointsRemaining => ShieldHitpointsRemaining + ArmorHitpointsRemaining + StructureHitpointsRemaining;

        public CombatShipStateView ToView() =>
            new()
            {
                FitId = FitId,
                LaunchedDroneCount = TotalLaunchedDroneCount,
                DronesInBayCount = TotalDronesInBayCount,
                LostDroneCount = TotalLostDroneCount,
                DroneBandwidthUsed = decimal.Round(CurrentDroneBandwidthUsed, 3),
                DroneBandwidthAvailable = DroneBandwidthAvailable,
                DroneBayUsed = decimal.Round(CurrentDroneBayUsed, 3),
                DroneBayAvailable = DroneBayAvailable,
                ShieldHitpointsRemaining = decimal.Round(ShieldHitpointsRemaining, 3),
                ArmorHitpointsRemaining = decimal.Round(ArmorHitpointsRemaining, 3),
                StructureHitpointsRemaining = decimal.Round(StructureHitpointsRemaining, 3),
                CapacitorRemaining = decimal.Round(CapacitorRemaining, 3),
                TurretWeaponsSuppressedByCapacitor = TurretWeaponsSuppressedByCapacitor,
                ActiveTankSuppressedByCapacitor = ActiveTankSuppressedByCapacitor,
                IsDestroyed = IsDestroyed
            };

        public static MutableShipState From(FitSnapshot fit, FitAttributeView attributes)
        {
            var launchedTotal = fit.DroneBay.Drones
                .Where(drone => drone.State is FittingItemState.Active or FittingItemState.Overload)
                .Sum(drone => Math.Max(0, drone.Quantity));
            var bayTotal = fit.DroneBay.Drones.Sum(drone => Math.Max(0, drone.BayQuantity ?? drone.Quantity));
            var averageBandwidthPerDrone = launchedTotal > 0 ? attributes.DroneBandwidthUsed / launchedTotal : 0m;
            var averageBayVolumePerDrone = bayTotal > 0 ? attributes.DroneBayUsed / bayTotal : 0m;
            var droneWings = fit.DroneBay.Drones
                .Select((drone, index) => MutableDroneWingState.From(drone, index, averageBandwidthPerDrone, averageBayVolumePerDrone))
                .ToList();

            return new MutableShipState
            {
                FitId = attributes.FitId,
                DroneBandwidthAvailable = attributes.DroneBandwidthAvailable,
                DroneBayAvailable = attributes.DroneBayAvailable,
                InitialLaunchedDroneCount = launchedTotal,
                DroneWings = droneWings,
                DroneAttackSystems = BuildDroneAttackSystems(fit.DroneBay.Drones, droneWings),
                WeaponSystems = BuildWeaponSystems(fit.Modules, attributes.TurretDamageMultiplier),
                RepairSystems = BuildRepairSystems(fit.Modules),
                MaxShieldHitpoints = attributes.ShieldHitpoints,
                MaxArmorHitpoints = attributes.ArmorHitpoints,
                MaxStructureHitpoints = attributes.StructureHitpoints,
                CapacitorCapacity = attributes.CapacitorCapacity,
                ShieldHitpointsRemaining = attributes.ShieldHitpoints,
                ArmorHitpointsRemaining = attributes.ArmorHitpoints,
                StructureHitpointsRemaining = attributes.StructureHitpoints,
                CapacitorRemaining = attributes.CapacitorCapacity,
                TurretWeaponsSuppressedByCapacitor = false,
                ActiveTankSuppressedByCapacitor = false,
                IsDestroyed = false
            };
        }
    }

    private sealed class MutableDroneWingState
    {
        public required string DroneTypeId { get; init; }
        public required string DroneName { get; init; }
        public required int WingIndex { get; init; }
        public decimal BandwidthPerUnit { get; init; }
        public decimal VolumePerUnit { get; init; }
        public decimal DurabilityPerUnit { get; init; }
        public int LaunchedCount { get; set; }
        public int BayCount { get; set; }
        public int LostCount { get; set; }
        public decimal PendingExposureDamage { get; set; }

        public static MutableDroneWingState From(
            DroneStack drone,
            int wingIndex,
            decimal fallbackBandwidthPerDrone,
            decimal fallbackBayVolumePerDrone)
        {
            var launchedCount = drone.State is FittingItemState.Active or FittingItemState.Overload ? Math.Max(0, drone.Quantity) : 0;
            var bayCount = Math.Max(0, drone.BayQuantity ?? drone.Quantity);
            var bandwidthPerUnit = drone.BandwidthPerUnit > 0m ? drone.BandwidthPerUnit : fallbackBandwidthPerDrone;
            var volumePerUnit = drone.VolumePerUnit > 0m ? drone.VolumePerUnit : fallbackBayVolumePerDrone;
            return new MutableDroneWingState
            {
                DroneTypeId = drone.DroneTypeId,
                DroneName = drone.Name,
                WingIndex = wingIndex,
                BandwidthPerUnit = bandwidthPerUnit,
                VolumePerUnit = volumePerUnit,
                DurabilityPerUnit = ComputeDroneDurabilityProxy(drone, bandwidthPerUnit, volumePerUnit),
                LaunchedCount = launchedCount,
                BayCount = bayCount,
                LostCount = 0,
                PendingExposureDamage = 0m
            };
        }
    }

    private sealed class MutableWeaponSystemState
    {
        public required string SystemId { get; init; }
        public required bool IsContinuous { get; init; }
        public WeaponApplicationKind ApplicationKind { get; init; }
        public DamageProfile DamageProfilePerSecond { get; init; } = new();
        public DamageProfile VolleyDamageProfile { get; init; } = new();
        public decimal CycleTimeSeconds { get; init; }
        public decimal ReloadTimeSeconds { get; init; }
        public int ShotsPerMagazine { get; init; }
        public int ShotsRemainingInMagazine { get; set; }
        public decimal CapacitorUsagePerSecond { get; init; }
        public decimal CapacitorCostPerActivation { get; init; }
        public decimal NextActivationTimeSeconds { get; set; }
        public decimal? ReloadStartedAtSeconds { get; set; }
        public decimal? ReloadUntilSeconds { get; set; }
    }

    private sealed class MutableRepairSystemState
    {
        public required string SystemId { get; init; }
        public required bool IsContinuous { get; init; }
        public decimal ShieldRepairPerSecond { get; init; }
        public decimal ArmorRepairPerSecond { get; init; }
        public decimal StructureRepairPerSecond { get; init; }
        public decimal ShieldRepairPerActivation { get; init; }
        public decimal ArmorRepairPerActivation { get; init; }
        public decimal StructureRepairPerActivation { get; init; }
        public decimal CycleTimeSeconds { get; init; }
        public decimal CapacitorUsagePerSecond { get; init; }
        public decimal CapacitorCostPerActivation { get; init; }
        public decimal NextActivationTimeSeconds { get; set; }
    }

    private sealed class MutableDroneAttackSystemState
    {
        public required string SystemId { get; init; }
        public required MutableDroneWingState Wing { get; init; }
        public required bool IsContinuous { get; init; }
        public DamageProfile DamageProfilePerSecond { get; init; } = new();
        public DamageProfile VolleyDamageProfilePerUnit { get; init; } = new();
        public decimal CycleTimeSeconds { get; init; }
        public decimal NextActivationTimeSeconds { get; set; }
    }

    private sealed record LayerDamageResolution(
        DamageProfile AppliedDamageProfile,
        DamageProfile MitigatedDamageProfile,
        DamageProfile OverflowDamageProfile);

    private sealed record WeaponResolution(
        DamageProfile DamageProfile,
        DamageProfile TurretDamageProfile,
        DamageProfile MissileDamageProfile,
        DamageProfile DirectDamageProfile,
        decimal CapacitorSpent,
        int Activations,
        int SuppressedCount,
        int ReloadingCount,
        int EngagedUnits,
        bool HasCapacitorDependentOffense);

    private sealed record DamageApplicationResolution(
        DamageProfile AppliedDamageProfile,
        decimal OverallMultiplier,
        decimal TurretMultiplier,
        decimal MissileMultiplier,
        decimal DroneMultiplier,
        decimal RangeMeters,
        decimal AngularVelocityRadiansPerSecond,
        decimal RelativeVelocityMetersPerSecond,
        decimal TargetSignatureRadiusMeters,
        string? Notes);

    private sealed record TankResolution(
        decimal ShieldRepairAmount,
        decimal ArmorRepairAmount,
        decimal StructureRepairAmount,
        decimal CapacitorSpent,
        int Activations,
        int SuppressedCount,
        bool HasCapacitorDependentTank);

    private static List<MutableWeaponSystemState> BuildWeaponSystems(
        IReadOnlyList<FittedModule> modules,
        decimal turretDamageMultiplier)
    {
        var systems = new List<MutableWeaponSystemState>();

        foreach (var module in modules.Where(module =>
                     module.SlotKind == ModuleSlotKind.High &&
                     module.State is FittingItemState.Active or FittingItemState.Overload))
        {
            var baseDamageProfilePerSecond = ResolveModuleDamageProfilePerSecond(module);
            var baseVolleyProfile = ResolveModuleVolleyProfile(module, baseDamageProfilePerSecond);
            var damageProfilePerSecond = ScaleDamageProfile(baseDamageProfilePerSecond, turretDamageMultiplier);
            var volleyDamageProfile = ScaleDamageProfile(baseVolleyProfile, turretDamageMultiplier);
            var damagePerSecond = TotalDamage(damageProfilePerSecond);
            var volleyDamage = TotalDamage(volleyDamageProfile);

            if (damagePerSecond <= 0m && volleyDamage <= 0m)
            {
                continue;
            }

            var cycleTimeSeconds = ResolveCycleTimeSeconds(module, TotalDamage(baseVolleyProfile), TotalDamage(baseDamageProfilePerSecond));
            var capacitorUsagePerSecond = ResolveCapacitorUsagePerSecond(module);
            var isContinuous = cycleTimeSeconds <= 0m || volleyDamage <= 0m;
            var shotsPerMagazine = module.ReloadTimeSeconds > 0m && module.MagazineCapacity > 0
                ? Math.Max(1, module.MagazineCapacity / Math.Max(1, module.ChargeUnitsPerCycle))
                : int.MaxValue;

            systems.Add(new MutableWeaponSystemState
            {
                SystemId = module.TypeId,
                IsContinuous = isContinuous,
                ApplicationKind = ResolveWeaponApplicationKind(module),
                DamageProfilePerSecond = isContinuous ? damageProfilePerSecond : ZeroDamageProfile(),
                VolleyDamageProfile = isContinuous ? ZeroDamageProfile() : volleyDamageProfile,
                CycleTimeSeconds = Math.Max(0m, cycleTimeSeconds),
                ReloadTimeSeconds = Math.Max(0m, module.ReloadTimeSeconds),
                ShotsPerMagazine = shotsPerMagazine,
                ShotsRemainingInMagazine = shotsPerMagazine,
                CapacitorUsagePerSecond = capacitorUsagePerSecond,
                CapacitorCostPerActivation = cycleTimeSeconds > 0m ? capacitorUsagePerSecond * cycleTimeSeconds : 0m,
                NextActivationTimeSeconds = isContinuous ? 0m : Math.Max(cycleTimeSeconds, 0.001m),
                ReloadStartedAtSeconds = null,
                ReloadUntilSeconds = null
            });
        }

        return systems;
    }

    private static WeaponApplicationKind ResolveWeaponApplicationKind(FittedModule module)
    {
        if (module.ApplicationKind != WeaponApplicationKind.Direct)
        {
            return module.ApplicationKind;
        }

        if (module.MissileExplosionRadiusMeters > 0m ||
            module.MissileExplosionVelocityMetersPerSecond > 0m ||
            module.Charge?.MissileExplosionRadiusMeters > 0m ||
            module.Charge?.MissileExplosionVelocityMetersPerSecond > 0m)
        {
            return WeaponApplicationKind.Missile;
        }

        if (module.TrackingSpeed > 0m ||
            module.OptimalRangeMeters > 0m ||
            module.FalloffRangeMeters > 0m)
        {
            return WeaponApplicationKind.Turret;
        }

        return WeaponApplicationKind.Direct;
    }

    private static List<MutableRepairSystemState> BuildRepairSystems(IReadOnlyList<FittedModule> modules)
    {
        var systems = new List<MutableRepairSystemState>();

        foreach (var module in modules.Where(module =>
                     module.State is FittingItemState.Active or FittingItemState.Overload &&
                     (module.ShieldRepairPerSecond > 0m ||
                      module.ArmorRepairPerSecond > 0m ||
                      module.StructureRepairPerSecond > 0m)))
        {
            var cycleTimeSeconds = Math.Max(0m, module.CycleTimeSeconds);
            var isContinuous = cycleTimeSeconds <= 0m;
            var capacitorUsagePerSecond = ResolveCapacitorUsagePerSecond(module);

            systems.Add(new MutableRepairSystemState
            {
                SystemId = module.TypeId,
                IsContinuous = isContinuous,
                ShieldRepairPerSecond = isContinuous ? module.ShieldRepairPerSecond : 0m,
                ArmorRepairPerSecond = isContinuous ? module.ArmorRepairPerSecond : 0m,
                StructureRepairPerSecond = isContinuous ? module.StructureRepairPerSecond : 0m,
                ShieldRepairPerActivation = isContinuous ? 0m : module.ShieldRepairPerSecond * cycleTimeSeconds,
                ArmorRepairPerActivation = isContinuous ? 0m : module.ArmorRepairPerSecond * cycleTimeSeconds,
                StructureRepairPerActivation = isContinuous ? 0m : module.StructureRepairPerSecond * cycleTimeSeconds,
                CycleTimeSeconds = cycleTimeSeconds,
                CapacitorUsagePerSecond = capacitorUsagePerSecond,
                CapacitorCostPerActivation = cycleTimeSeconds > 0m ? capacitorUsagePerSecond * cycleTimeSeconds : 0m,
                NextActivationTimeSeconds = isContinuous ? 0m : Math.Max(cycleTimeSeconds, 0.001m)
            });
        }

        return systems;
    }

    private static List<MutableDroneAttackSystemState> BuildDroneAttackSystems(
        IReadOnlyList<DroneStack> drones,
        IReadOnlyList<MutableDroneWingState> wings)
    {
        var systems = new List<MutableDroneAttackSystemState>();

        for (var index = 0; index < drones.Count && index < wings.Count; index++)
        {
            var drone = drones[index];
            var wing = wings[index];
            var damageProfilePerSecond = ResolveDroneDamageProfilePerSecond(drone);
            var volleyProfilePerUnit = ResolveDroneVolleyDamageProfile(drone, damageProfilePerSecond);
            var isContinuous = drone.CycleTimeSeconds <= 0m || TotalDamage(volleyProfilePerUnit) <= 0m;

            if (TotalDamage(damageProfilePerSecond) <= 0m && TotalDamage(volleyProfilePerUnit) <= 0m)
            {
                continue;
            }

            systems.Add(new MutableDroneAttackSystemState
            {
                SystemId = $"{wing.DroneTypeId}:{wing.WingIndex}",
                Wing = wing,
                IsContinuous = isContinuous,
                DamageProfilePerSecond = isContinuous ? damageProfilePerSecond : ZeroDamageProfile(),
                VolleyDamageProfilePerUnit = isContinuous ? ZeroDamageProfile() : volleyProfilePerUnit,
                CycleTimeSeconds = Math.Max(0m, drone.CycleTimeSeconds),
                NextActivationTimeSeconds = isContinuous ? 0m : Math.Max(drone.CycleTimeSeconds, 0.001m)
            });
        }

        return systems;
    }

    private static decimal ComputeDroneDurabilityProxy(
        DroneStack drone,
        decimal bandwidthPerUnit,
        decimal volumePerUnit)
    {
        var perDroneDps = ResolveDroneDamagePerSecond(drone);
        return Math.Max(25m, decimal.Round(20m + perDroneDps * 2m + bandwidthPerUnit * 3m + volumePerUnit * 2m, 3));
    }
}
