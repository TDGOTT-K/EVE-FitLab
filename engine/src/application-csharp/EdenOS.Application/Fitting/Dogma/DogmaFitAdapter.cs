using EdenOS.Application.Fitting.Dogma.Engine;
using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.Fitting.Dogma.Models;
using EdenOS.Application.Fitting.Dogma.Rules;
using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting.Dogma;

internal sealed class DogmaFitAdapter
{
    private static readonly HashSet<int> RemoteSupportGroupIds = [65, 66, 67, 325, 1698, 2018];

    private readonly IDogmaDataSource dogmaDataSource;
    private readonly FittingCalculator calculator = new();

    public DogmaFitAdapter(string sdeRootPath, string dataRootPath)
        : this(new JsonlFileDogmaDataSource(sdeRootPath, dataRootPath))
    {
    }

    public DogmaFitAdapter(IDogmaDataSource dogmaDataSource)
    {
        this.dogmaDataSource = dogmaDataSource;
    }

    public bool CanHandle(FitSnapshot snapshot) => snapshot.ShipHull.DogmaTypeId is > 0;

    public DogmaFitComputationResult Compute(
        FitSnapshot snapshot,
        bool includeAttributeSnapshot = true,
        FitSimulationMode simulationMode = FitSimulationMode.SingleShip)
    {
        var warnings = new List<string>();
        var issues = ValidateDogmaInput(snapshot).ToList();
        var normalizedSnapshot = NormalizeSnapshot(snapshot);

        var dogmaContext = DogmaContext.GetOrCreate(dogmaDataSource);
        var slotResolvedSnapshot = ResolveModuleSlotKinds(normalizedSnapshot, dogmaContext, warnings);
        var stateResolvedSnapshot = ResolveModuleStates(slotResolvedSnapshot, dogmaContext, warnings);
        var fitDocument = ToFitDocument(stateResolvedSnapshot);
        var skills = stateResolvedSnapshot.Skills
            .Where(skill => skill.SkillTypeId > 0)
            .GroupBy(skill => skill.SkillTypeId)
            .ToDictionary(group => group.Key, group => Math.Clamp(group.Last().Level, 0, 5));
        var combatResolvedSnapshot = ResolveModuleCombatMetadata(stateResolvedSnapshot, fitDocument, skills, dogmaContext);
        var calculation = calculator.Calculate(fitDocument, skills, dogmaContext, includeAttributeSnapshot);
        var attributes = ToAttributeView(combatResolvedSnapshot, calculation, dogmaContext, simulationMode);

        AppendValidationIssues(issues, combatResolvedSnapshot, calculation.Summary, calculation.Summary.AttributeSnapshot, dogmaContext);
        warnings.AddRange(calculation.Warnings);
        return new DogmaFitComputationResult(combatResolvedSnapshot, attributes, issues, warnings);
    }

    private static FitSnapshot NormalizeSnapshot(FitSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
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
            Cargo = snapshot.Cargo
                .Select(entry => entry with
                {
                    TypeId = entry.TypeId.Trim(),
                    Name = entry.Name.Trim()
                })
                .ToArray(),
            Implants = snapshot.Implants
                .Select(implant => implant with
                {
                    TypeId = implant.TypeId.Trim(),
                    Name = implant.Name.Trim()
                })
                .ToArray(),
            Boosters = snapshot.Boosters
                .Select(booster => booster with
                {
                    TypeId = booster.TypeId.Trim(),
                    Name = booster.Name.Trim()
                })
                .ToArray(),
            CreatedAtUtc = snapshot.CreatedAtUtc == default ? now : snapshot.CreatedAtUtc,
            UpdatedAtUtc = now
        };
    }

    private static IReadOnlyList<FitValidationIssue> ValidateDogmaInput(FitSnapshot snapshot)
    {
        var issues = new List<FitValidationIssue>();
        if (snapshot.ShipHull.DogmaTypeId is null or <= 0)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_hull_type_id",
                Category = "dogma",
                Message = "ship_hull.dogma_type_id is required for dogma-backed fitting.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        foreach (var module in snapshot.Modules.Where(module => module.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_module_type_id",
                Category = "dogma",
                Message = $"Module '{module.Name}' is missing dogma_type_id.",
                RelatedSlotId = module.SlotId,
                RelatedTypeId = module.TypeId
            });
        }

        foreach (var rig in snapshot.Rigs.Where(rig => rig.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_rig_type_id",
                Category = "dogma",
                Message = $"Rig '{rig.Name}' is missing dogma_type_id.",
                RelatedSlotId = rig.SlotId,
                RelatedTypeId = rig.TypeId
            });
        }

        foreach (var charge in snapshot.Modules.Select(module => module.Charge).Where(charge => charge is not null && charge.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_charge_type_id",
                Category = "dogma",
                Message = $"Charge '{charge!.Name}' is missing dogma_type_id.",
                RelatedTypeId = charge.ChargeId
            });
        }

        foreach (var drone in snapshot.DroneBay.Drones.Where(drone => drone.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_drone_type_id",
                Category = "dogma",
                Message = $"Drone '{drone.Name}' is missing dogma_type_id.",
                RelatedTypeId = drone.DroneTypeId
            });
        }

        foreach (var implant in snapshot.Implants.Where(implant => implant.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_implant_type_id",
                Category = "dogma",
                Message = $"Implant '{implant.Name}' is missing dogma_type_id.",
                RelatedTypeId = implant.TypeId
            });
        }

        foreach (var booster in snapshot.Boosters.Where(booster => booster.DogmaTypeId is null or <= 0))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "missing_dogma_booster_type_id",
                Category = "dogma",
                Message = $"Booster '{booster.Name}' is missing dogma_type_id.",
                RelatedTypeId = booster.TypeId
            });
        }

        foreach (var duplicateImplantSlot in snapshot.Implants
                     .Where(implant => implant.SlotIndex is > 0)
                     .GroupBy(implant => implant.SlotIndex!.Value)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "implant_slot_conflict",
                Category = "implant",
                Message = $"Implant slot {duplicateImplantSlot.Key} is occupied by multiple implants.",
                RelatedTypeId = duplicateImplantSlot.First().TypeId
            });
        }

        foreach (var duplicateBoosterSlot in snapshot.Boosters
                     .Where(booster => booster.BoosterSlot is > 0)
                     .GroupBy(booster => booster.BoosterSlot!.Value)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new FitValidationIssue
            {
                Code = "booster_slot_conflict",
                Category = "booster",
                Message = $"Booster slot {duplicateBoosterSlot.Key} is occupied by multiple boosters.",
                RelatedTypeId = duplicateBoosterSlot.First().TypeId
            });
        }

        var occupiedSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in snapshot.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.SlotId))
            {
                continue;
            }

            if (occupiedSlots.TryGetValue(module.SlotId, out var existing))
            {
                issues.Add(new FitValidationIssue
                {
                    Code = "slot_conflict",
                    Category = "slot",
                    Message = $"Slot '{module.SlotId}' is already occupied by '{existing}'.",
                    RelatedSlotId = module.SlotId,
                    RelatedTypeId = module.TypeId
                });
                continue;
            }

            occupiedSlots[module.SlotId] = module.TypeId;
        }

        foreach (var rig in snapshot.Rigs)
        {
            if (string.IsNullOrWhiteSpace(rig.SlotId))
            {
                continue;
            }

            if (occupiedSlots.TryGetValue(rig.SlotId, out var existing))
            {
                issues.Add(new FitValidationIssue
                {
                    Code = "slot_conflict",
                    Category = "slot",
                    Message = $"Slot '{rig.SlotId}' is already occupied by '{existing}'.",
                    RelatedSlotId = rig.SlotId,
                    RelatedTypeId = rig.TypeId
                });
                continue;
            }

            occupiedSlots[rig.SlotId] = rig.TypeId;
        }

        return issues;
    }

    private static FitSnapshot ResolveModuleStates(
        FitSnapshot snapshot,
        DogmaContext dogmaContext,
        ICollection<string> warnings)
    {
        var resolvedModules = new List<FittedModule>(snapshot.Modules.Count);
        foreach (var module in snapshot.Modules)
        {
            if (module.State == FittingItemState.Offline)
            {
                resolvedModules.Add(module);
                continue;
            }
            var requestedState = ToEffectState(module.State);
            var maxState = ResolveMaxState(module.DogmaTypeId ?? 0, dogmaContext);
            var resolvedState = requestedState <= maxState ? requestedState : maxState;
            var resolvedFittingState = ToFittingItemState(resolvedState);

            if (resolvedFittingState != module.State)
            {
                warnings.Add(
                    $"Module '{module.Name}' requested state '{module.State}' exceeds dogma max state '{resolvedFittingState}' and was clamped.");
            }

            resolvedModules.Add(module with { State = resolvedFittingState });
        }

        return snapshot with
        {
            Modules = resolvedModules
        };
    }

    private static FitSnapshot ResolveModuleSlotKinds(
        FitSnapshot snapshot,
        DogmaContext dogmaContext,
        ICollection<string> warnings)
    {
        var resolvedModules = new List<FittedModule>(snapshot.Modules.Count);
        foreach (var module in snapshot.Modules)
        {
            var inferredSlotClass = InferModuleSlotClass(module.DogmaTypeId ?? 0, dogmaContext);
            switch (inferredSlotClass)
            {
                case ModuleSlotKind.High:
                {
                    if (module.SlotKind != ModuleSlotKind.High)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'High' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.High });
                    break;
                }
                case ModuleSlotKind.Mid:
                {
                    if (module.SlotKind != ModuleSlotKind.Mid)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'Mid' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.Mid });
                    break;
                }
                case ModuleSlotKind.Low:
                {
                    if (module.SlotKind != ModuleSlotKind.Low)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'Low' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.Low });
                    break;
                }
                case ModuleSlotKind.Rig:
                {
                    if (module.SlotKind != ModuleSlotKind.Rig)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'Rig' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.Rig });
                    break;
                }
                case ModuleSlotKind.Subsystem:
                {
                    if (module.SlotKind != ModuleSlotKind.Subsystem)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'Subsystem' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.Subsystem });
                    break;
                }
                case ModuleSlotKind.Service:
                {
                    if (module.SlotKind != ModuleSlotKind.Service)
                    {
                        warnings.Add($"Module '{module.Name}' slot kind was corrected from '{module.SlotKind}' to 'Service' from dogma effects.");
                    }

                    resolvedModules.Add(module with { SlotKind = ModuleSlotKind.Service });
                    break;
                }
                default:
                    resolvedModules.Add(module);
                    break;
            }
        }

        return snapshot with
        {
            Modules = resolvedModules
        };
    }

    private FitSnapshot ResolveModuleCombatMetadata(
        FitSnapshot snapshot,
        FitDocument fitDocument,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext)
    {
        var projections = calculator.ProjectCombatModules(fitDocument, skills, dogmaContext);
        var projectionByIndex = projections.ToDictionary(projection => projection.ModuleIndex);
        var resolvedModules = snapshot.Modules
            .Select((module, moduleIndex) =>
            {
                if (!projectionByIndex.TryGetValue(moduleIndex, out var projection))
                {
                    return module;
                }

                return module with
                {
                    AttributeTraces = projection.AttributeTraces,
                    CpuUsage = projection.CpuUsage,
                    PowergridUsage = projection.PowergridUsage,
                    DamagePerSecond = projection.DamagePerSecond,
                    DamageProfilePerSecond = ToDamageProfile(projection.DamageProfilePerSecond, projection.DamagePerSecond),
                    VolleyDamage = projection.VolleyDamage,
                    VolleyDamageProfile = ToDamageProfile(projection.VolleyDamageProfile, projection.VolleyDamage),
                    CycleTimeSeconds = projection.CycleTimeSeconds,
                    ReloadTimeSeconds = projection.ReloadTimeSeconds,
                    MagazineCapacity = projection.MagazineCapacity,
                    ChargeUnitsPerCycle = projection.ChargeUnitsPerCycle,
                    ShieldRepairPerSecond = projection.ShieldRepairPerSecond,
                    ArmorRepairPerSecond = projection.ArmorRepairPerSecond,
                    StructureRepairPerSecond = projection.StructureRepairPerSecond,
                    CapacitorTransferPerSecond = projection.CapacitorTransferPerSecond,
                    CapacitorUsagePerSecond = projection.CapacitorUsagePerSecond,
                    OptimalRangeMeters = projection.OptimalRangeMeters,
                    FalloffRangeMeters = projection.FalloffRangeMeters,
                    TrackingSpeed = projection.TrackingSpeed,
                    SignatureResolutionMeters = projection.SignatureResolutionMeters,
                    ApplicationKind = projection.ApplicationKind,
                    MissileExplosionRadiusMeters = projection.MissileExplosionRadiusMeters,
                    MissileExplosionVelocityMetersPerSecond = projection.MissileExplosionVelocityMetersPerSecond,
                    MissileDamageReductionFactor = projection.MissileDamageReductionFactor,
                    MissileDamageReductionSensitivity = projection.MissileDamageReductionSensitivity,
                    Charge = module.Charge is null
                        ? null
                        : module.Charge with
                        {
                            AttributeTraces = projection.ChargeAttributeTraces,
                            DamagePerSecondBonus = projection.ChargeDamagePerSecond,
                            DamageProfileBonus = ToDamageProfile(projection.ChargeDamageProfilePerSecond, projection.ChargeDamagePerSecond),
                            VolleyDamageBonus = projection.ChargeVolleyDamage,
                            VolleyDamageProfileBonus = ToDamageProfile(projection.ChargeVolleyDamageProfile, projection.ChargeVolleyDamage),
                            CapacitorUsagePerSecond = 0m,
                            MissileExplosionRadiusMeters = projection.MissileExplosionRadiusMeters,
                            MissileExplosionVelocityMetersPerSecond = projection.MissileExplosionVelocityMetersPerSecond,
                            MissileDamageReductionFactor = projection.MissileDamageReductionFactor,
                            MissileDamageReductionSensitivity = projection.MissileDamageReductionSensitivity
                        }
                };
            })
            .ToArray();

        return snapshot with
        {
            Modules = resolvedModules,
            DroneBay = snapshot.DroneBay with { Drones = calculator.ProjectDroneMetadata(fitDocument, snapshot.DroneBay.Drones, skills, dogmaContext) }
        };
    }

    private static void AppendValidationIssues(
        ICollection<FitValidationIssue> issues,
        FitSnapshot snapshot,
        FittingSummary summary,
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        DogmaContext dogmaContext)
    {
        if (summary.Fitting.PowerLoad > summary.Fitting.PowerOutput && summary.Fitting.PowerOutput > 0m)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "powergrid_exceeded",
                Category = "resource",
                Message = $"Powergrid load {summary.Fitting.PowerLoad:0.###} exceeds output {summary.Fitting.PowerOutput:0.###}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        if (summary.Fitting.CpuLoad > summary.Fitting.CpuOutput && summary.Fitting.CpuOutput > 0m)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "cpu_exceeded",
                Category = "resource",
                Message = $"CPU load {summary.Fitting.CpuLoad:0.###} exceeds output {summary.Fitting.CpuOutput:0.###}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        if (summary.Fitting.CalibrationLoad > summary.Fitting.Calibration && summary.Fitting.Calibration > 0m)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "calibration_exceeded",
                Category = "resource",
                Message = $"Calibration load {summary.Fitting.CalibrationLoad:0.###} exceeds capacity {summary.Fitting.Calibration:0.###}.",
                RelatedTypeId = snapshot.ShipHull.HullId
            });
        }

        if (summary.Drones.BandwidthLoad > summary.Drones.Bandwidth && summary.Drones.Bandwidth > 0m)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "drone_bandwidth_exceeded",
                Category = "resource",
                Message = $"Drone bandwidth usage {summary.Drones.BandwidthLoad:0.###} exceeds available {summary.Drones.Bandwidth:0.###}.",
                RelatedTypeId = snapshot.FitId
            });
        }

        if (summary.Drones.CapacityLoad > summary.Drones.Capacity && summary.Drones.Capacity > 0m)
        {
            issues.Add(new FitValidationIssue
            {
                Code = "drone_capacity_exceeded",
                Category = "resource",
                Message = $"Drone bay usage {summary.Drones.CapacityLoad:0.###} exceeds available {summary.Drones.Capacity:0.###}.",
                RelatedTypeId = snapshot.FitId
            });
        }

        var highSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.High, dogmaContext);
        if (highSlots is not null &&
            snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.High) > highSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.High, highSlots.Value));
        }

        var midSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Mid, dogmaContext);
        if (midSlots is not null &&
            snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Mid) > midSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.Mid, midSlots.Value));
        }

        var lowSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Low, dogmaContext);
        if (lowSlots is not null &&
            snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Low) > lowSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.Low, lowSlots.Value));
        }

        var rigSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Rig, dogmaContext);
        if (rigSlots is not null &&
            snapshot.Rigs.Count > rigSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.Rig, rigSlots.Value));
        }

        var subsystemSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Subsystem, dogmaContext);
        if (subsystemSlots is not null &&
            snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Subsystem) > subsystemSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.Subsystem, subsystemSlots.Value));
        }

        var serviceSlots = ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Service, dogmaContext);
        if (serviceSlots is not null &&
            snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Service) > serviceSlots.Value)
        {
            issues.Add(BuildSlotCapacityIssue(snapshot, ModuleSlotKind.Service, serviceSlots.Value));
        }

        var turretHardpoints = ResolveHardpointCapacity(attributeSnapshot, "turretSlotsLeft", "turretHardPointModifier");
        var turretUsage = CountHardpointUsage(snapshot, dogmaContext, HardpointKind.Turret);
        if (turretHardpoints is not null &&
            turretUsage > turretHardpoints.Value)
        {
            issues.Add(BuildHardpointIssue("turret_hardpoints_exceeded", "Turret", turretUsage, turretHardpoints.Value, snapshot.ShipHull.HullId));
        }

        var launcherHardpoints = ResolveHardpointCapacity(attributeSnapshot, "launcherSlotsLeft", "launcherHardPointModifier");
        var launcherUsage = CountHardpointUsage(snapshot, dogmaContext, HardpointKind.Launcher);
        if (launcherHardpoints is not null &&
            launcherUsage > launcherHardpoints.Value)
        {
            issues.Add(BuildHardpointIssue("launcher_hardpoints_exceeded", "Launcher", launcherUsage, launcherHardpoints.Value, snapshot.ShipHull.HullId));
        }
    }

    private static decimal? ResolveSlotCapacity(
        FitSnapshot snapshot,
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        ModuleSlotKind slotKind,
        DogmaContext? dogmaContext = null)
    {
        var fallback = slotKind switch
        {
            ModuleSlotKind.High => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.High),
            ModuleSlotKind.Mid => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.Mid),
            ModuleSlotKind.Low => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.Low),
            ModuleSlotKind.Rig => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.Rig),
            ModuleSlotKind.Subsystem => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.Subsystem),
            ModuleSlotKind.Service => snapshot.ShipHull.Slots.Count(slot => slot.Kind == ModuleSlotKind.Service),
            _ => 0
        };
        var hasCapacity = slotKind switch
        {
            ModuleSlotKind.High => TryGetEffectiveSlotCapacity(attributeSnapshot, "hiSlots", "highSlots", "hiSlotModifier", out var _),
            ModuleSlotKind.Mid => TryGetEffectiveSlotCapacity(attributeSnapshot, "medSlots", null, "medSlotModifier", out var _),
            ModuleSlotKind.Low => TryGetEffectiveSlotCapacity(attributeSnapshot, "lowSlots", null, "lowSlotModifier", out var _),
            ModuleSlotKind.Rig => TryGetEffectiveSlotCapacity(attributeSnapshot, "rigSlots", null, null, out var _),
            ModuleSlotKind.Subsystem => TryGetEffectiveSlotCapacity(attributeSnapshot, "maxSubSystems", "subSystemSlot", null, out var _),
            ModuleSlotKind.Service => TryGetEffectiveSlotCapacity(attributeSnapshot, "serviceSlots", null, null, out var _),
            _ => false
        };
        if (!hasCapacity &&
            dogmaContext is not null &&
            TryResolveHullSlotCapacityFromDogma(snapshot, slotKind, dogmaContext, out var dogmaCapacity))
        {
            var modifierName = slotKind switch
            {
                ModuleSlotKind.High => "hiSlotModifier",
                ModuleSlotKind.Mid => "medSlotModifier",
                ModuleSlotKind.Low => "lowSlotModifier",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(modifierName) &&
                attributeSnapshot.TryGetValue(modifierName, out var modifier))
            {
                dogmaCapacity += modifier;
            }

            return dogmaCapacity;
        }

        if (!hasCapacity && fallback <= 0)
        {
            return null;
        }

        return slotKind switch
        {
            ModuleSlotKind.High => TryGetEffectiveSlotCapacity(attributeSnapshot, "hiSlots", "highSlots", "hiSlotModifier", out var highCapacity)
                ? highCapacity
                : fallback,
            ModuleSlotKind.Mid => TryGetEffectiveSlotCapacity(attributeSnapshot, "medSlots", null, "medSlotModifier", out var midCapacity)
                ? midCapacity
                : fallback,
            ModuleSlotKind.Low => TryGetEffectiveSlotCapacity(attributeSnapshot, "lowSlots", null, "lowSlotModifier", out var lowCapacity)
                ? lowCapacity
                : fallback,
            ModuleSlotKind.Rig => TryGetEffectiveSlotCapacity(attributeSnapshot, "rigSlots", null, null, out var rigCapacity)
                ? rigCapacity
                : fallback,
            ModuleSlotKind.Subsystem => TryGetEffectiveSlotCapacity(attributeSnapshot, "maxSubSystems", "subSystemSlot", null, out var subsystemCapacity)
                ? subsystemCapacity
                : fallback,
            ModuleSlotKind.Service => TryGetEffectiveSlotCapacity(attributeSnapshot, "serviceSlots", null, null, out var serviceCapacity)
                ? serviceCapacity
                : fallback,
            _ => null
        };
    }

    private static bool TryResolveHullSlotCapacityFromDogma(
        FitSnapshot snapshot,
        ModuleSlotKind slotKind,
        DogmaContext dogmaContext,
        out decimal capacity)
    {
        capacity = 0m;
        if (snapshot.ShipHull.DogmaTypeId is null or <= 0)
        {
            return false;
        }

        var baseCapacity = slotKind switch
        {
            ModuleSlotKind.High => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "hiSlots", "highSlots"),
            ModuleSlotKind.Mid => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "medSlots"),
            ModuleSlotKind.Low => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "lowSlots"),
            ModuleSlotKind.Rig => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "rigSlots"),
            ModuleSlotKind.Subsystem => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "maxSubSystems", "subSystemSlot"),
            ModuleSlotKind.Service => ResolveDogmaSlotCapacityValue(dogmaContext, snapshot.ShipHull.DogmaTypeId.Value, "serviceSlots"),
            _ => null
        };

        if (baseCapacity is null)
        {
            return false;
        }

        capacity = Math.Max(0m, decimal.Round(Convert.ToDecimal(baseCapacity.Value), 3));
        return true;
    }

    private static double? ResolveDogmaSlotCapacityValue(
        DogmaContext dogmaContext,
        int hullTypeId,
        params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            var value = dogmaContext.GetTypeAttributeValue(hullTypeId, attributeName, double.NaN);
            if (!double.IsNaN(value) && (value > 0d || (value == 0d && dogmaContext.GetTypeAttributeValue(hullTypeId, "maxSubSystems", 0d) > 0d)))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetEffectiveSlotCapacity(
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        string primaryName,
        string? legacyName,
        string? modifierName,
        out decimal capacity)
    {
        if (!TryGetSlotCapacity(attributeSnapshot, primaryName, legacyName, out capacity))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(modifierName) &&
            attributeSnapshot.TryGetValue(modifierName, out var modifier))
        {
            capacity += modifier;
        }

        capacity = Math.Max(0m, capacity);
        return true;
    }

    private static bool TryGetSlotCapacity(
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        string primaryName,
        string? legacyName,
        out decimal capacity)
    {
        if (attributeSnapshot.TryGetValue(primaryName, out capacity))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(legacyName) && attributeSnapshot.TryGetValue(legacyName, out capacity);
    }

    private static decimal? ResolveHardpointCapacity(
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        string baseName,
        string modifierName)
    {
        if (!attributeSnapshot.TryGetValue(baseName, out var capacity) && !attributeSnapshot.ContainsKey(modifierName))
        {
            return null;
        }

        if (attributeSnapshot.TryGetValue(modifierName, out var modifier))
        {
            capacity += modifier;
        }

        return Math.Max(0m, capacity);
    }

    private static FitValidationIssue BuildSlotCapacityIssue(FitSnapshot snapshot, ModuleSlotKind slotKind, decimal capacity) =>
        new()
        {
            Code = "slot_capacity_exceeded",
            Category = "slot",
            Message = $"{slotKind} slot usage exceeds dogma slot count {capacity:0.###}.",
            RelatedTypeId = snapshot.ShipHull.HullId
        };

    private static FitValidationIssue BuildHardpointIssue(
        string code,
        string hardpointLabel,
        decimal used,
        decimal available,
        string hullId) =>
        new()
        {
            Code = code,
            Category = "hardpoint",
            Message = $"{hardpointLabel} hardpoint usage {used} exceeds dogma capacity {available:0.###}.",
            RelatedTypeId = hullId
        };

    private static int CountHardpointUsage(FitSnapshot snapshot, DogmaContext dogmaContext, HardpointKind hardpointKind) =>
        snapshot.Modules.Count(module => ModuleConsumesHardpoint(module, dogmaContext, hardpointKind));

    private static bool ModuleConsumesHardpoint(
        FittedModule module,
        DogmaContext dogmaContext,
        HardpointKind hardpointKind)
    {
        if (module.SlotKind != ModuleSlotKind.High)
        {
            return false;
        }

        if (module.DogmaTypeId is > 0)
        {
            var effects = GetResolvedEffects(dogmaContext, module.DogmaTypeId.Value);
            var turret = effects.Any(e => e.EffectId == 42 || e.EffectName == "turretFitted");
            var launcher = effects.Any(e => e.EffectId == 40 || e.EffectName == "launcherFitted");
            // useMissiles also applies to probe launchers; only fitting effects consume hardpoints.
            if (dogmaContext.GetTypeDogma(module.DogmaTypeId.Value) is not null)
                return hardpointKind == HardpointKind.Turret ? turret : launcher;
        }
        return hardpointKind switch
        {
            HardpointKind.Launcher => module.ApplicationKind == WeaponApplicationKind.Missile,
            HardpointKind.Turret => module.ApplicationKind == WeaponApplicationKind.Turret,
            _ => false
        };
    }

    private static IReadOnlyList<DogmaResolvedEffect> GetResolvedEffects(DogmaContext dogmaContext, int typeId)
    {
        if (typeId <= 0)
        {
            return [];
        }

        var typeDogma = dogmaContext.GetTypeDogma(typeId);
        if (typeDogma?.DogmaEffects is null)
        {
            return [];
        }

        var effects = new List<DogmaResolvedEffect>(typeDogma.DogmaEffects.Count);
        foreach (var typeEffect in typeDogma.DogmaEffects)
        {
            var effect = dogmaContext.GetEffect(typeEffect.EffectId);
            effects.Add(new DogmaResolvedEffect(typeEffect.EffectId, effect?.Name));
        }

        return effects;
    }

    private static WeaponApplicationKind ResolveModuleWeaponApplicationKind(int typeId, DogmaContext dogmaContext) =>
        dogmaContext.RuleSet.ResolveWeaponApplicationKind(GetResolvedEffects(dogmaContext, typeId));

    private static FitDocument ToFitDocument(FitSnapshot snapshot)
    {
        return new FitDocument
        {
            ShipTypeId = snapshot.ShipHull.DogmaTypeId ?? 0,
            ShipTypeName = snapshot.ShipHull.Name,
            Modules = snapshot.Modules
                .Select(module => new FitModuleSlot
                {
                    TypeId = module.DogmaTypeId ?? 0,
                    TypeName = module.Name,
                    SlotGroup = ToSlotGroup(module.SlotKind),
                    SlotIndex = ResolveSlotIndex(module.SlotId),
                    State = ToDogmaState(module.State),
                    ChargeTypeId = module.Charge?.DogmaTypeId,
                    ChargeTypeName = module.Charge?.Name
                })
                .Concat(snapshot.Rigs.Select(rig => new FitModuleSlot
                {
                    TypeId = rig.DogmaTypeId ?? 0,
                    TypeName = rig.Name,
                    SlotGroup = "rig",
                    SlotIndex = ResolveSlotIndex(rig.SlotId),
                    State = "passive"
                }))
                .ToList(),
            Drones = snapshot.DroneBay.Drones
                .Select(drone => new FitDroneEntry
                {
                    TypeId = drone.DogmaTypeId ?? 0,
                    TypeName = drone.Name,
                    Quantity = drone.Quantity,
                    BayQuantity = drone.BayQuantity,
                    State = ToDogmaState(drone.State)
                })
                .ToList(),
            Cargo = snapshot.Cargo
                .Select(entry => new Models.FitCargoEntry
                {
                    TypeId = entry.DogmaTypeId ?? 0,
                    TypeName = entry.Name,
                    Quantity = entry.Quantity
                })
                .ToList(),
            Implants = snapshot.Implants
                .Select(implant => new FitImplantEntry
                {
                    TypeId = implant.DogmaTypeId ?? 0,
                    TypeName = implant.Name,
                    SlotIndex = implant.SlotIndex,
                    State = ToDogmaState(implant.State)
                })
                .ToList(),
            Boosters = snapshot.Boosters
                .Select(booster => new FitBoosterEntry
                {
                    TypeId = booster.DogmaTypeId ?? 0,
                    TypeName = booster.Name,
                    BoosterSlot = booster.BoosterSlot,
                    State = ToDogmaState(booster.State),
                    SideEffects = booster.SideEffects
                        .Where(sideEffect => sideEffect.EffectId > 0)
                        .GroupBy(sideEffect => sideEffect.EffectId)
                        .Select(group => new FitBoosterSideEffectEntry
                        {
                            EffectId = group.Key,
                            Active = group.Last().Active
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static FitAttributeView ToAttributeView(
        FitSnapshot snapshot,
        FittingCalculationOutput calculation,
        DogmaContext dogmaContext,
        FitSimulationMode simulationMode)
    {
        var summary = calculation.Summary;
        var hullCategoryId = snapshot.ShipHull.DogmaTypeId is int hullTypeId && hullTypeId > 0
            ? dogmaContext.GetTypeCategoryId(hullTypeId)
            : 0;
        var suppressAggregateEhp = hullCategoryId == 65;
        var shieldResistances = ToResistanceProfile(
            summary.Defense.ShieldEmResistPct,
            summary.Defense.ShieldThermalResistPct,
            summary.Defense.ShieldKineticResistPct,
            summary.Defense.ShieldExplosiveResistPct);
        var armorResistances = ToResistanceProfile(
            summary.Defense.ArmorEmResistPct,
            summary.Defense.ArmorThermalResistPct,
            summary.Defense.ArmorKineticResistPct,
            summary.Defense.ArmorExplosiveResistPct);
        var structureResistances = ToResistanceProfile(
            summary.Defense.HullEmResistPct,
            summary.Defense.HullThermalResistPct,
            summary.Defense.HullKineticResistPct,
            summary.Defense.HullExplosiveResistPct);
        var shieldEhp = ComputeEffectiveProjection(summary.ShieldHitPoints, shieldResistances);
        var armorEhp = ComputeEffectiveProjection(summary.ArmorHitPoints, armorResistances);
        var structureEhp = ComputeEffectiveProjection(summary.StructureHitPoints, structureResistances);
        var totalEhp = AddDamageTypeProjections(AddDamageTypeProjections(shieldEhp, armorEhp), structureEhp);
        if (summary.AttributeSnapshot.TryGetValue("shieldEhp", out var shieldEhpOmni) && shieldEhpOmni > 0m)
        {
            shieldEhp = shieldEhp with { Omni = shieldEhpOmni };
        }

        if (summary.AttributeSnapshot.TryGetValue("armorEhp", out var armorEhpOmni) && armorEhpOmni > 0m)
        {
            armorEhp = armorEhp with { Omni = armorEhpOmni };
        }

        if (summary.AttributeSnapshot.TryGetValue("hullEhp", out var hullEhpOmni) && hullEhpOmni > 0m)
        {
            structureEhp = structureEhp with { Omni = hullEhpOmni };
        }

        if (summary.Defense.EffectiveHitPoints > 0m || suppressAggregateEhp)
        {
            totalEhp = totalEhp with { Omni = summary.Defense.EffectiveHitPoints };
        }
        var appliedDamagePerSecond = summary.Offense.Dps > 0m ? summary.Offense.Dps : summary.Dps;
        var sustainedDamagePerSecond = summary.Offense.DpsWithReload > 0m
            ? summary.Offense.DpsWithReload
            : appliedDamagePerSecond;
        var turretDamagePerSecond = SumDamageProfile(summary.Offense.TurretDpsProfile);
        var turretOptimalRange = ResolveWeightedModuleMetric(snapshot.Modules, module => module.OptimalRangeMeters, WeaponApplicationKind.Turret);
        var turretFalloffRange = ResolveWeightedModuleMetric(snapshot.Modules, module => module.FalloffRangeMeters, WeaponApplicationKind.Turret);
        var turretTracking = ResolveWeightedModuleMetric(snapshot.Modules, module => module.TrackingSpeed, WeaponApplicationKind.Turret);
        var turretSignatureResolution = ResolveWeightedModuleMetric(snapshot.Modules, module => module.SignatureResolutionMeters, WeaponApplicationKind.Turret);
        var missileExplosionRadius = ResolveWeightedModuleMetric(snapshot.Modules, ResolveModuleMissileExplosionRadius, WeaponApplicationKind.Missile);
        var missileExplosionVelocity = ResolveWeightedModuleMetric(snapshot.Modules, ResolveModuleMissileExplosionVelocity, WeaponApplicationKind.Missile);
        var missileDamageReductionFactor = ResolveWeightedModuleMetric(snapshot.Modules, ResolveModuleMissileDamageReductionFactor, WeaponApplicationKind.Missile);
        var missileDamageReductionSensitivity = ResolveWeightedModuleMetric(snapshot.Modules, ResolveModuleMissileDamageReductionSensitivity, WeaponApplicationKind.Missile);
        var appliedDamage = ToDamageProfile(summary.Offense.DpsProfile, appliedDamagePerSecond);
        var turretDamage = ToDamageProfile(summary.Offense.TurretDpsProfile, turretDamagePerSecond);
        var volleyDamage = ToDamageProfile(summary.Offense.AlphaProfile, summary.Offense.Alpha > 0m ? summary.Offense.Alpha : summary.Volley);
        var droneDamage = ToDamageProfile(summary.Offense.DroneDpsProfile, summary.Offense.DroneDps);
        var localShieldRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.ShieldRepairPerSecond, remote: false)
            + summary.Defense.PassiveShieldRechargeRate;
        var localArmorRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.ArmorRepairPerSecond, remote: false);
        var localStructureRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.StructureRepairPerSecond, remote: false);
        var remoteShieldRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.ShieldRepairPerSecond, remote: true);
        var remoteArmorRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.ArmorRepairPerSecond, remote: true);
        var remoteStructureRepairPerSecond = ResolveRepairPerSecond(snapshot.Modules, dogmaContext, module => module.StructureRepairPerSecond, remote: true);
        var remoteCapacitorTransferPerSecond = decimal.Round(
            snapshot.Modules
                .Where(module => IsRemoteSupportModule(module, dogmaContext))
                .Sum(module => Math.Max(0m, module.CapacitorTransferPerSecond)),
            6);
        var capacitorUsagePerSecond = summary.Capacitor.UsePerSecond;
        var capacitorStable = summary.Capacitor.Stable;
        decimal? capacitorDepletionSeconds = summary.Capacitor.DepletesInSeconds < 0m
            ? null
            : summary.Capacitor.DepletesInSeconds;
        var peakCapacitorRechargePerSecond = summary.Capacitor.UsePerSecond + summary.Capacitor.PeakDelta;

        if (simulationMode == FitSimulationMode.SymmetricCapChain &&
            remoteCapacitorTransferPerSecond > 0m &&
            summary.Capacitor.Capacity > 0m &&
            summary.Capacitor.RechargeSeconds > 0m)
        {
            var capChainUsePerSecond = Math.Max(0m, summary.Capacitor.UsePerSecond - remoteCapacitorTransferPerSecond);
            var peakRecharge = Math.Max(0m, 2.5m * summary.Capacitor.Capacity / summary.Capacitor.RechargeSeconds);
            capacitorUsagePerSecond = decimal.Round(capChainUsePerSecond, 6);
            peakCapacitorRechargePerSecond = decimal.Round(peakRecharge, 6);

            if (peakRecharge >= capChainUsePerSecond)
            {
                capacitorStable = true;
                capacitorDepletionSeconds = null;
            }
            else
            {
                capacitorStable = false;
                capacitorDepletionSeconds = EstimateCapacitorDepletionSeconds(
                    summary.Capacitor.Capacity,
                    summary.Capacitor.RechargeSeconds,
                    capChainUsePerSecond);
            }
        }

        var effectiveRepair = AddDamageTypeProjections(
            AddDamageTypeProjections(
                ComputeEffectiveProjection(localShieldRepairPerSecond, shieldResistances),
                ComputeEffectiveProjection(localArmorRepairPerSecond, armorResistances)),
            ComputeEffectiveProjection(localStructureRepairPerSecond, structureResistances));
        return new FitAttributeView
        {
            FitId = snapshot.FitId,
            HullId = snapshot.ShipHull.HullId,
            PowergridUsed = summary.Fitting.PowerLoad,
            PowergridAvailable = summary.Fitting.PowerOutput,
            CpuUsed = summary.Fitting.CpuLoad,
            CalibrationUsed = summary.Fitting.CalibrationLoad,
            CalibrationAvailable = summary.Fitting.Calibration,
            TurretHardpointsUsed = CountHardpointUsage(snapshot, dogmaContext, HardpointKind.Turret),
            TurretHardpointsAvailable = ResolveHardpointCapacity(summary.AttributeSnapshot, "turretSlotsLeft", "turretHardPointModifier") ?? (snapshot.ShipHull.DogmaTypeId is > 0 ? (decimal)dogmaContext.GetTypeAttributeValue(snapshot.ShipHull.DogmaTypeId.Value, "turretSlotsLeft", 0d) : null),
            LauncherHardpointsUsed = CountHardpointUsage(snapshot, dogmaContext, HardpointKind.Launcher),
            LauncherHardpointsAvailable = ResolveHardpointCapacity(summary.AttributeSnapshot, "launcherSlotsLeft", "launcherHardPointModifier") ?? (snapshot.ShipHull.DogmaTypeId is > 0 ? (decimal)dogmaContext.GetTypeAttributeValue(snapshot.ShipHull.DogmaTypeId.Value, "launcherSlotsLeft", 0d) : null),
            CpuAvailable = summary.Fitting.CpuOutput,
            DroneBandwidthUsed = summary.Drones.BandwidthLoad,
            DroneBandwidthAvailable = summary.Drones.Bandwidth,
            DroneBayUsed = summary.Drones.CapacityLoad,
            DroneBayAvailable = summary.Drones.Capacity,
            ShieldHitpoints = summary.ShieldHitPoints,
            ShieldResistances = shieldResistances,
            ArmorHitpoints = summary.ArmorHitPoints,
            ArmorResistances = armorResistances,
            StructureHitpoints = summary.StructureHitPoints,
            StructureResistances = structureResistances,
            TotalHitpoints = summary.ShieldHitPoints + summary.ArmorHitPoints + summary.StructureHitPoints,
            ShieldEffectiveHitpointsOmni = shieldEhp.Omni,
            ShieldEffectiveHitpointsByDamageType = shieldEhp,
            ArmorEffectiveHitpointsOmni = armorEhp.Omni,
            ArmorEffectiveHitpointsByDamageType = armorEhp,
            StructureEffectiveHitpointsOmni = structureEhp.Omni,
            StructureEffectiveHitpointsByDamageType = structureEhp,
            TotalEffectiveHitpointsOmni = totalEhp.Omni,
            TotalEffectiveHitpointsByDamageType = totalEhp,
            CapacitorCapacity = summary.Capacitor.Capacity,
            CapacitorRechargeSeconds = summary.Capacitor.RechargeSeconds,
            CapacitorUsagePerSecond = capacitorUsagePerSecond,
            WeaponCapacitorUsagePerSecond = summary.Capacitor.WeaponUsePerSecond,
            ActiveTankCapacitorUsagePerSecond = summary.Capacitor.ActiveTankUsePerSecond,
            PeakCapacitorRechargePerSecond = peakCapacitorRechargePerSecond,
            CapacitorStable = capacitorStable,
            CapacitorDepletionSeconds = capacitorDepletionSeconds,
            MaxVelocity = summary.Mobility.MaxVelocity,
            SignatureRadius = summary.Targeting.SignatureRadius,
            MaxTargetRange = summary.Targeting.MaxTargetRange,
            TurretDamageMultiplier = summary.Offense.TurretDamageMultiplier > 0m
                ? summary.Offense.TurretDamageMultiplier
                : (snapshot.ShipHull.TurretDamageMultiplier > 0m ? snapshot.ShipHull.TurretDamageMultiplier : 1m),
            AppliedDamagePerSecond = appliedDamagePerSecond,
            AppliedDamageProfilePerSecond = appliedDamage,
            AppliedTurretDamagePerSecond = turretDamagePerSecond,
            AppliedTurretDamageProfilePerSecond = turretDamage,
            AppliedDroneDamagePerSecond = summary.Offense.DroneDps,
            AppliedDroneDamageProfilePerSecond = droneDamage,
            DamagePerSecondWithReload = sustainedDamagePerSecond,
            VolleyDamage = summary.Offense.Alpha > 0m ? summary.Offense.Alpha : summary.Volley,
            VolleyDamageProfile = volleyDamage,
            WeaponOptimalRangeMeters = turretOptimalRange > 0m ? turretOptimalRange : summary.WeaponOptimalRange,
            WeaponFalloffRangeMeters = turretFalloffRange,
            WeaponTracking = turretTracking > 0m ? turretTracking : summary.WeaponTracking,
            WeaponSignatureResolutionMeters = turretSignatureResolution,
            MissileExplosionRadiusMeters = missileExplosionRadius,
            MissileExplosionVelocityMetersPerSecond = missileExplosionVelocity,
            MissileDamageReductionFactor = missileDamageReductionFactor,
            MissileDamageReductionSensitivity = missileDamageReductionSensitivity,
            PassiveShieldRechargePerSecond = summary.Defense.PassiveShieldRechargeRate,
            ShieldRepairPerSecond = localShieldRepairPerSecond,
            ArmorRepairPerSecond = localArmorRepairPerSecond,
            StructureRepairPerSecond = localStructureRepairPerSecond,
            RemoteShieldRepairPerSecond = remoteShieldRepairPerSecond,
            RemoteArmorRepairPerSecond = remoteArmorRepairPerSecond,
            RemoteStructureRepairPerSecond = remoteStructureRepairPerSecond,
            RemoteCapacitorTransferPerSecond = remoteCapacitorTransferPerSecond,
            EffectiveRepairPerSecondByDamageType = effectiveRepair,
            SlotUsage = BuildSlotUsage(snapshot, summary.AttributeSnapshot, dogmaContext),
            AttributeTraces = summary.AttributeTraces,
            AttributeSnapshot = new Dictionary<string, decimal>(summary.AttributeSnapshot, StringComparer.OrdinalIgnoreCase),
            ApproximationNotes =
            [
                "Attributes were resolved from the dogma-backed fitting calculator port, using EVEShipFit-style four-pass effect evaluation and stacking behavior.",
                "Dogma combat projection now writes per-module cycle, reload, magazine, capacitor, typed damage, and local application metadata back into the public fit snapshot so the duel kernel can consume real module timing instead of only fit-level DPS.",
                "FitAttributeView still exposes only fit-level application envelopes. For mixed weapon systems, turret and missile envelopes are reduced to DPS-weighted averages by weapon family rather than independent per-module target solutions.",
                "Missile launcher fit summaries currently keep damage_per_second_with_reload aligned with paper launcher DPS. Reload pauses are modeled in the combat kernel's time-based firing loop instead of the static fit rollup.",
                "Drone bandwidth and bay usage are now projected from dogma hull capacity plus per-drone bandwidth and volume. This still treats listed drones as a static fit payload, not a launched-in-space state machine.",
                "SymmetricCapChain mode treats the fit's remote capacitor transmitters as receiving an equal same-fit return stream from a second identical hull. This currently adjusts only capacitor stability and depletion timing, not combat-kernel behavior.",
                "Combat remains an MVP duel kernel: deterministic application math, no random hit quality, no missile flight time, no capacitor warfare, and no full drone AI."
            ]
        };
    }

    private static decimal? EstimateCapacitorDepletionSeconds(
        decimal capacitorCapacity,
        decimal capacitorRechargeSeconds,
        decimal capacitorUsagePerSecond)
    {
        if (capacitorCapacity <= 0m || capacitorRechargeSeconds <= 0m || capacitorUsagePerSecond <= 0m)
        {
            return 0m;
        }

        var peakRechargePerSecond = 2.5m * capacitorCapacity / capacitorRechargeSeconds;
        if (peakRechargePerSecond >= capacitorUsagePerSecond)
        {
            return null;
        }

        var averageNetDrainPerSecond = capacitorUsagePerSecond - peakRechargePerSecond;
        if (averageNetDrainPerSecond <= 0m)
        {
            return null;
        }

        return decimal.Round(capacitorCapacity / averageNetDrainPerSecond, 3);
    }

    private static decimal ResolveWeightedModuleMetric(
        IReadOnlyList<FittedModule> modules,
        Func<FittedModule, decimal> selector,
        params WeaponApplicationKind[] applicationKinds)
    {
        if (modules.Count == 0)
        {
            return 0m;
        }

        var allowedKinds = applicationKinds.Length == 0
            ? null
            : applicationKinds.ToHashSet();
        decimal weightedTotal = 0m;
        decimal totalWeight = 0m;

        foreach (var module in modules)
        {
            if (allowedKinds is not null && !allowedKinds.Contains(module.ApplicationKind))
            {
                continue;
            }

            var metric = Math.Max(0m, selector(module));
            if (metric <= 0m)
            {
                continue;
            }

            var weight = ResolveModuleCombatWeight(module);
            if (weight <= 0m)
            {
                continue;
            }

            weightedTotal += metric * weight;
            totalWeight += weight;
        }

        return totalWeight > 0m ? decimal.Round(weightedTotal / totalWeight, 6) : 0m;
    }

    private static decimal ResolveRepairPerSecond(
        IReadOnlyList<FittedModule> modules,
        DogmaContext dogmaContext,
        Func<FittedModule, decimal> selector,
        bool remote)
    {
        return decimal.Round(
            modules
                .Where(module => IsRemoteRepairModule(module, dogmaContext) == remote)
                .Sum(module => Math.Max(0m, selector(module))),
            6);
    }

    private static bool IsRemoteRepairModule(FittedModule module, DogmaContext dogmaContext)
    {
        if (module.DogmaTypeId is null or <= 0)
        {
            return false;
        }

        return IsRemoteSupportModule(module, dogmaContext) &&
               (module.ShieldRepairPerSecond > 0m ||
                module.ArmorRepairPerSecond > 0m ||
                module.StructureRepairPerSecond > 0m);
    }

    private static bool IsRemoteSupportModule(FittedModule module, DogmaContext dogmaContext)
    {
        if (module.DogmaTypeId is null or <= 0)
        {
            return false;
        }

        var groupId = dogmaContext.GetTypeGroupId(module.DogmaTypeId.Value);
        return RemoteSupportGroupIds.Contains(groupId);
    }

    private static decimal ResolveModuleCombatWeight(FittedModule module)
    {
        var damageWeight = module.DamagePerSecond + (module.Charge?.DamagePerSecondBonus ?? 0m);
        if (damageWeight > 0m)
        {
            return damageWeight;
        }

        var volleyWeight = module.VolleyDamage + (module.Charge?.VolleyDamageBonus ?? 0m);
        if (volleyWeight > 0m)
        {
            return volleyWeight;
        }

        return 0m;
    }

    private static decimal ResolveModuleMissileExplosionRadius(FittedModule module) =>
        module.MissileExplosionRadiusMeters > 0m
            ? module.MissileExplosionRadiusMeters
            : (module.Charge?.MissileExplosionRadiusMeters ?? 0m);

    private static decimal ResolveModuleMissileExplosionVelocity(FittedModule module) =>
        module.MissileExplosionVelocityMetersPerSecond > 0m
            ? module.MissileExplosionVelocityMetersPerSecond
            : (module.Charge?.MissileExplosionVelocityMetersPerSecond ?? 0m);

    private static decimal ResolveModuleMissileDamageReductionFactor(FittedModule module) =>
        module.MissileDamageReductionFactor > 0m
            ? module.MissileDamageReductionFactor
            : (module.Charge?.MissileDamageReductionFactor ?? 0m);

    private static decimal ResolveModuleMissileDamageReductionSensitivity(FittedModule module) =>
        module.MissileDamageReductionSensitivity > 0m
            ? module.MissileDamageReductionSensitivity
            : (module.Charge?.MissileDamageReductionSensitivity ?? 0m);

    private static IReadOnlyList<FitSlotUsageView> BuildSlotUsage(
        FitSnapshot snapshot,
        IReadOnlyDictionary<string, decimal> attributeSnapshot,
        DogmaContext dogmaContext) =>
    [
        new FitSlotUsageView { Kind = ModuleSlotKind.High, Used = snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.High), Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.High, dogmaContext)) },
        new FitSlotUsageView { Kind = ModuleSlotKind.Mid, Used = snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Mid), Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Mid, dogmaContext)) },
        new FitSlotUsageView { Kind = ModuleSlotKind.Low, Used = snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Low), Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Low, dogmaContext)) },
        new FitSlotUsageView { Kind = ModuleSlotKind.Rig, Used = snapshot.Rigs.Count, Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Rig, dogmaContext)) },
        new FitSlotUsageView { Kind = ModuleSlotKind.Subsystem, Used = snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Subsystem), Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Subsystem, dogmaContext)) },
        new FitSlotUsageView { Kind = ModuleSlotKind.Service, Used = snapshot.Modules.Count(module => module.SlotKind == ModuleSlotKind.Service), Available = ToSlotAvailability(ResolveSlotCapacity(snapshot, attributeSnapshot, ModuleSlotKind.Service, dogmaContext)) }
    ];

    private static int ToSlotAvailability(decimal? capacity)
    {
        if (capacity is null or <= 0m)
        {
            return 0;
        }

        return (int)decimal.Floor(capacity.Value);
    }

    private static ResistanceProfile ToResistanceProfile(decimal emPct, decimal thermalPct, decimal kineticPct, decimal explosivePct) =>
        new()
        {
            EmPercent = decimal.Round(emPct / 100m, 6),
            ThermalPercent = decimal.Round(thermalPct / 100m, 6),
            KineticPercent = decimal.Round(kineticPct / 100m, 6),
            ExplosivePercent = decimal.Round(explosivePct / 100m, 6)
        };

    private static DamageTypeProjection ComputeEffectiveProjection(decimal rawValue, ResistanceProfile resistances) =>
        new()
        {
            Em = ComputeEffectiveValue(rawValue, resistances.EmPercent),
            Thermal = ComputeEffectiveValue(rawValue, resistances.ThermalPercent),
            Kinetic = ComputeEffectiveValue(rawValue, resistances.KineticPercent),
            Explosive = ComputeEffectiveValue(rawValue, resistances.ExplosivePercent),
            Omni = decimal.Round((ComputeEffectiveValue(rawValue, resistances.EmPercent)
                + ComputeEffectiveValue(rawValue, resistances.ThermalPercent)
                + ComputeEffectiveValue(rawValue, resistances.KineticPercent)
                + ComputeEffectiveValue(rawValue, resistances.ExplosivePercent)) / 4m, 3)
        };

    private static decimal ComputeEffectiveValue(decimal rawValue, decimal resistance)
    {
        if (rawValue <= 0m)
        {
            return 0m;
        }

        var vulnerability = Math.Max(0.000001m, 1m - Math.Clamp(resistance, 0m, 0.99m));
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

    private static DamageProfile ToDamageProfile(Models.FittingDamageProfileSummary profile, decimal fallbackTotal)
    {
        var total = profile.Em + profile.Thermal + profile.Kinetic + profile.Explosive;
        if (total > 0m)
        {
            return new DamageProfile
            {
                Em = profile.Em,
                Thermal = profile.Thermal,
                Kinetic = profile.Kinetic,
                Explosive = profile.Explosive
            };
        }

        if (fallbackTotal <= 0m)
        {
            return new DamageProfile();
        }

        var component = decimal.Round(fallbackTotal / 4m, 6);
        return new DamageProfile
        {
            Em = component,
            Thermal = component,
            Kinetic = component,
            Explosive = component
        };
    }

    private static decimal SumDamageProfile(Models.FittingDamageProfileSummary profile) =>
        decimal.Round(profile.Em + profile.Thermal + profile.Kinetic + profile.Explosive, 3);

    private static int ResolveSlotIndex(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return 0;
        }

        var parts = slotId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return int.TryParse(parts.LastOrDefault(), out var index) ? index : 0;
    }

    private static string ToSlotGroup(ModuleSlotKind kind) =>
        kind switch
        {
            ModuleSlotKind.High => "high",
            ModuleSlotKind.Mid => "mid",
            ModuleSlotKind.Low => "low",
            ModuleSlotKind.Rig => "rig",
            ModuleSlotKind.Subsystem => "subsystem",
            ModuleSlotKind.Service => "service",
            _ => "high"
        };

    private static string ToDogmaState(FittingItemState state) =>
        state switch
        {
            FittingItemState.Offline => "offline",
            FittingItemState.Passive => "passive",
            FittingItemState.Online => "online",
            FittingItemState.Active => "active",
            FittingItemState.Overload => "overload",
            _ => "active"
        };

    private static ModuleSlotKind? InferModuleSlotClass(int typeId, DogmaContext dogmaContext)
    {
        if (typeId <= 0)
        {
            return null;
        }

        return dogmaContext.RuleSet.ResolveSlotKind(GetResolvedEffects(dogmaContext, typeId));
    }

    private static EffectState ResolveMaxState(int typeId, DogmaContext dogmaContext)
    {
        var maxState = EffectState.Passive;
        if (typeId > 0)
        {
            var typeDogma = dogmaContext.GetTypeDogma(typeId);
            if (typeDogma?.DogmaEffects is not null)
            {
                foreach (var typeEffect in typeDogma.DogmaEffects)
                {
                    var effect = dogmaContext.GetEffect(typeEffect.EffectId);
                    var effectState = ToEffectCategoryState(effect?.EffectCategoryId ?? 0);
                    if (effectState > maxState && effectState <= EffectState.Overload)
                    {
                        maxState = effectState;
                    }
                }
            }

            if (maxState < EffectState.Active &&
                dogmaContext.GetTypeAttributeValue(typeId, "capacitorNeed", 0d) > 0d)
            {
                maxState = EffectState.Active;
            }
        }

        return maxState;
    }

    private static EffectState ToEffectState(FittingItemState state) =>
        state switch
        {
            FittingItemState.Overload => EffectState.Overload,
            FittingItemState.Active => EffectState.Active,
            FittingItemState.Online => EffectState.Online,
            _ => EffectState.Passive
        };

    private static EffectState ToEffectCategoryState(int effectCategoryId) =>
        effectCategoryId switch
        {
            0 => EffectState.Passive,
            1 => EffectState.Active,
            2 => EffectState.Target,
            3 => EffectState.Area,
            4 => EffectState.Online,
            5 => EffectState.Overload,
            6 => EffectState.Dungeon,
            7 => EffectState.System,
            _ => EffectState.Passive
        };

    private static FittingItemState ToFittingItemState(EffectState state) =>
        state switch
        {
            EffectState.Overload => FittingItemState.Overload,
            EffectState.Active => FittingItemState.Active,
            EffectState.Online => FittingItemState.Online,
            _ => FittingItemState.Passive
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record DogmaFitComputationResult(
    FitSnapshot Snapshot,
    FitAttributeView Attributes,
    IReadOnlyList<FitValidationIssue> Issues,
    IReadOnlyList<string> Warnings);

internal enum EffectState
{
    Passive = 0,
    Online = 1,
    Active = 2,
    Overload = 3,
    Target = 4,
    Area = 5,
    Dungeon = 6,
    System = 7
}

internal enum HardpointKind
{
    Turret,
    Launcher
}


