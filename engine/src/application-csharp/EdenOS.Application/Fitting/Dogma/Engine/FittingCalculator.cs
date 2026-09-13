using EdenOS.Application.Fitting.Dogma.Models;
using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting.Dogma.Engine;

public sealed class FittingCalculator
{
    private const double PenaltyFactor = 0.8691199808003974d;
    private const int SkillLevelAttributeId = 280;
    private const int CharacterTypeId = 1373;
    private static readonly HashSet<int> ExemptPenaltyCategoryIds = new() { 6, 8, 16, 20, 32 };
    private static readonly List<TargetModifierCandidate> EmptyModifierCandidates = new();

    public FittingCalculationOutput Calculate(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext,
        bool includeAttributeSnapshot = false)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(dogmaContext);

        var warnings = new List<string>();
        var pass1 = RunPass1(fit, skills, dogmaContext);
        var traces = new Dictionary<string, FitAttributeExecutionTrace>();
        var pass2 = RunPass2(pass1, fit, skills, dogmaContext, traces);
        var pass3 = RunPass3(pass2, fit, skills, dogmaContext, warnings);
        var output = RunPass4(pass3, warnings, dogmaContext, includeAttributeSnapshot);
        foreach (var trace in traces.Values)
        {
            var final = pass3.ShipAttributes.GetValueOrDefault(dogmaContext.TryGetAttributeId(trace.Attribute));
            trace.IsComplete = Math.Abs(final - trace.FinalValue) < 1e-8;
            trace.FinalValue = final;
        }
        output.Summary.AttributeTraces = traces;
        return output;
    }

    public IReadOnlyList<FittingAttributeModifierTrace> TraceModuleAttribute(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext,
        int moduleIndex,
        string attributeName)
    {
        return TraceFittedAttribute(
            fit,
            skills,
            dogmaContext,
            TargetObjectKind.Item,
            moduleIndex,
            attributeName,
            fit.Modules[moduleIndex].TypeId);
    }

    public IReadOnlyList<FittingAttributeModifierTrace> TraceChargeAttribute(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext,
        int moduleIndex,
        string attributeName)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(dogmaContext);
        ArgumentException.ThrowIfNullOrEmpty(attributeName);

        if (moduleIndex < 0 || moduleIndex >= fit.Modules.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleIndex));
        }

        var module = fit.Modules[moduleIndex];
        if (module.ChargeTypeId is not int chargeTypeId || chargeTypeId <= 0)
        {
            return [];
        }

        return TraceFittedAttribute(
            fit,
            skills,
            dogmaContext,
            TargetObjectKind.Charge,
            moduleIndex,
            attributeName,
            chargeTypeId);
    }

    internal IReadOnlyList<DroneStack> ProjectDroneMetadata(
        FitDocument fit, IReadOnlyList<DroneStack> drones, IReadOnlyDictionary<int, int> skills, DogmaContext ctx)
    {
        var sources = BuildSources(fit, skills, ctx).ToList();
        foreach (var type in fit.Drones.Select(d => d.TypeId).Distinct())
            if (!sources.Any(s => s.Kind == SourceKind.Drone && s.TypeId == type))
                sources.Add(new SourceEntity(SourceKind.Drone, type, EffectState.Active, 1, -1));
        var cache = new TracedAttributeCache();
        var pending = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifiers = BuildTargetModifierIndex(ctx, sources);
        var requirements = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        return drones.Select(drone =>
        {
            if (drone.DogmaTypeId is not int type || type <= 0) return drone;
            var values = new Dictionary<string, decimal>();
            foreach (var name in new[] { "speed", "duration", "damageMultiplier", "emDamage", "thermalDamage", "kineticDamage", "explosiveDamage", "maxRange", "falloff", "trackingSpeed", "signatureResolution", "maxVelocity", "shieldCapacity", "armorHP", "hp", "signatureRadius", "droneBandwidthUsed", "volume", "miningAmount",
                "shieldEmDamageResonance", "shieldThermalDamageResonance", "shieldKineticDamageResonance", "shieldExplosiveDamageResonance",
                "armorEmDamageResonance", "armorThermalDamageResonance", "armorKineticDamageResonance", "armorExplosiveDamageResonance",
                "emDamageResonance", "thermalDamageResonance", "kineticDamageResonance", "explosiveDamageResonance" })
                values[name] = RoundToDecimal(GetEffectiveTypeAttributeValue(TargetObjectKind.Drone, 0, type, name, skills, ctx, sources, cache, pending, modifiers, requirements));
            decimal Value(string n) => values.GetValueOrDefault(n);
            var cycle = (Value("speed") > 0 ? Value("speed") : Value("duration")) / 1000m;
            var multiplier = Value("damageMultiplier") > 0 ? Value("damageMultiplier") : 1m;
            var volley = new DamageProfile { Em = Value("emDamage") * multiplier, Thermal = Value("thermalDamage") * multiplier, Kinetic = Value("kineticDamage") * multiplier, Explosive = Value("explosiveDamage") * multiplier };
            var alpha = volley.Em + volley.Thermal + volley.Kinetic + volley.Explosive;
            return drone with {
                AttributeSnapshot = values,
                AttributeTraces = cache.Traces.Where(e => e.Key.Kind == TargetObjectKind.Drone && e.Key.TypeId == type).ToDictionary(e => e.Value.Attribute, e => e.Value),
                CycleTimeSeconds = cycle, VolleyDamage = alpha, VolleyDamageProfile = volley,
                DamagePerSecond = cycle > 0 ? alpha / cycle : 0,
                DamageProfilePerSecond = cycle > 0 ? new DamageProfile { Em = volley.Em / cycle, Thermal = volley.Thermal / cycle, Kinetic = volley.Kinetic / cycle, Explosive = volley.Explosive / cycle } : new DamageProfile(),
                OptimalRangeMeters = Value("maxRange"), FalloffRangeMeters = Value("falloff"), TrackingSpeed = Value("trackingSpeed"),
                SignatureResolutionMeters = Value("signatureResolution"), BandwidthPerUnit = Value("droneBandwidthUsed"), VolumePerUnit = Value("volume")
            };
        }).ToArray();
    }

    internal IReadOnlyList<FittingModuleCombatProjection> ProjectCombatModules(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(dogmaContext);

        var sources = BuildSources(fit, skills, dogmaContext);
        var effectCache = new TracedAttributeCache();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(dogmaContext, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        var semantics = dogmaContext.SemanticIds;
        var useLegacyCharacterMissileDamageMultiplier =
            skills.Count > 0 &&
            !HasAnyEffectId(dogmaContext, CharacterTypeId, semantics.MissileDamageEffect);
        var characterMissileDamageMultiplier = useLegacyCharacterMissileDamageMultiplier
            ? GetCharacterAttributeValue("missileDamageMultiplier", dogmaContext, sources)
            : 1d;
        var projections = new List<FittingModuleCombatProjection>(fit.Modules.Count);

        for (var moduleIndex = 0; moduleIndex < fit.Modules.Count; moduleIndex++)
        {
            var module = fit.Modules[moduleIndex];
            var moduleTypeId = module.TypeId;
            var chargeTypeId = module.ChargeTypeId ?? 0;
            var applicationKind = ResolveProjectedWeaponApplicationKind(moduleTypeId, chargeTypeId, dogmaContext);
            if (moduleTypeId <= 0)
            {
                projections.Add(new FittingModuleCombatProjection
                {
                    ModuleIndex = moduleIndex,
                    ApplicationKind = applicationKind
                });
                continue;
            }

            var cycleTimeMs = ResolveModuleCycleMilliseconds(
                moduleIndex,
                moduleTypeId,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);

            var damageMultiplier = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "damageMultiplier",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache), 1d);
            var missileDamageMultiplier = chargeTypeId > 0 && IsMissileCharge(chargeTypeId, dogmaContext)
                ? GetPositive(characterMissileDamageMultiplier, 1d)
                : 1d;
            var damageScale = damageMultiplier * missileDamageMultiplier;

            var moduleVolleyProfile = ScaleDamageVector(
                SumDamageAttributesByType(
                    TargetObjectKind.Item,
                    moduleIndex,
                    moduleTypeId,
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache),
                damageScale);
            var chargeVolleyProfile = chargeTypeId > 0
                ? ScaleDamageVector(
                    SumDamageAttributesByType(
                        TargetObjectKind.Charge,
                        moduleIndex,
                        chargeTypeId,
                        skills,
                        dogmaContext,
                        sources,
                        effectCache,
                        inProgress,
                        modifierCandidateCache,
                        requiredSkillCache),
                    damageScale)
                : ZeroDamageVector();
            var moduleDamageProfilePerSecond = cycleTimeMs > 0
                ? ScaleDamageVector(moduleVolleyProfile, 1000d / cycleTimeMs)
                : ZeroDamageVector();
            var chargeDamageProfilePerSecond = cycleTimeMs > 0
                ? ScaleDamageVector(chargeVolleyProfile, 1000d / cycleTimeMs)
                : ZeroDamageVector();

            var ammunitionState = ResolveModuleAmmunitionState(
                fit,
                moduleIndex,
                module,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var capacitorNeed = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "capacitorNeed",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var capacitorUsagePerSecond = cycleTimeMs > 0
                ? capacitorNeed * 1000d / cycleTimeMs
                : 0d;
            var optimalRangeMeters = ResolveModuleOptimalRangeMeters(
                moduleIndex,
                moduleTypeId,
                chargeTypeId,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var falloffRangeMeters = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "falloff",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (falloffRangeMeters <= 0d && chargeTypeId > 0)
            {
                falloffRangeMeters = ResolvePositiveEffectiveAttribute(
                    TargetObjectKind.Charge,
                    moduleIndex,
                    chargeTypeId,
                    "falloff",
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
            }

            var trackingSpeed = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "trackingSpeed",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var signatureResolutionMeters = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "signatureResolution",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (signatureResolutionMeters <= 0d && chargeTypeId > 0)
            {
                signatureResolutionMeters = ResolvePositiveEffectiveAttribute(
                    TargetObjectKind.Charge,
                    moduleIndex,
                    chargeTypeId,
                    "signatureResolution",
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
            }

            var shieldRepairRate = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                semantics.ShieldBoostRate,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (shieldRepairRate <= 0d)
            {
                shieldRepairRate = ResolvePositiveEffectiveAttribute(
                    TargetObjectKind.Item,
                    moduleIndex,
                    moduleTypeId,
                    "shieldBonus",
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
            }
            var armorRepairRate = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                semantics.ArmorRepairRate,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (armorRepairRate <= 0d)
            {
                armorRepairRate = ResolvePositiveEffectiveAttribute(
                    TargetObjectKind.Item,
                    moduleIndex,
                    moduleTypeId,
                    "armorDamageAmount",
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
            }
            var structureRepairRate = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                semantics.HullRepairRate,
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (structureRepairRate <= 0d)
            {
                structureRepairRate = ResolvePositiveEffectiveAttribute(
                    TargetObjectKind.Item,
                    moduleIndex,
                    moduleTypeId,
                    "structureDamageAmount",
                    skills,
                    dogmaContext,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
            }
            var capacitorTransferRate = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Item,
                moduleIndex,
                moduleTypeId,
                "powerTransferAmount",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            foreach (var extraName in new[] { "damageMultiplierBonusPerCycle", "damageMultiplierBonusMax", "energyNeutralizerAmount", "miningAmount", "specialisationAsteroidYieldMultiplier", "speedFactor", "warpScrambleStrength", "scanResolutionBonus", "maxTargetRangeBonus", "signatureRadiusBonus", "trackingSpeedBonus", "scanRadarStrengthBonus", "scanMagnetometricStrengthBonus", "scanGravimetricStrengthBonus", "scanLadarStrengthBonus" })
                ResolvePositiveEffectiveAttribute(TargetObjectKind.Item, moduleIndex, moduleTypeId, extraName,
                    skills, dogmaContext, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache);
            var missileExplosionRadiusMeters = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Charge,
                moduleIndex,
                chargeTypeId,
                "aoeCloudSize",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var missileExplosionVelocityMetersPerSecond = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Charge,
                moduleIndex,
                chargeTypeId,
                "aoeVelocity",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var missileDamageReductionFactor = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Charge,
                moduleIndex,
                chargeTypeId,
                "aoeDamageReductionFactor",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            var missileDamageReductionSensitivity = ResolvePositiveEffectiveAttribute(
                TargetObjectKind.Charge,
                moduleIndex,
                chargeTypeId,
                "aoeDamageReductionSensitivity",
                skills,
                dogmaContext,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);

            projections.Add(new FittingModuleCombatProjection
            {
                ModuleIndex = moduleIndex,
                ApplicationKind = applicationKind,
                DamagePerSecond = RoundToDecimal(SumDamageVector(moduleDamageProfilePerSecond)),
                DamageProfilePerSecond = ToProjectionProfile(moduleDamageProfilePerSecond),
                VolleyDamage = RoundToDecimal(SumDamageVector(moduleVolleyProfile)),
                VolleyDamageProfile = ToProjectionProfile(moduleVolleyProfile),
                ChargeDamagePerSecond = RoundToDecimal(SumDamageVector(chargeDamageProfilePerSecond)),
                ChargeDamageProfilePerSecond = ToProjectionProfile(chargeDamageProfilePerSecond),
                ChargeVolleyDamage = RoundToDecimal(SumDamageVector(chargeVolleyProfile)),
                ChargeVolleyDamageProfile = ToProjectionProfile(chargeVolleyProfile),
                CpuUsage = string.Equals(module.State, "offline", StringComparison.OrdinalIgnoreCase) ? 0 : RoundToDecimal(GetEffectiveTypeAttributeValue(TargetObjectKind.Item, moduleIndex, moduleTypeId, "cpu", skills, dogmaContext, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)),
                PowergridUsage = string.Equals(module.State, "offline", StringComparison.OrdinalIgnoreCase) ? 0 : RoundToDecimal(GetEffectiveTypeAttributeValue(TargetObjectKind.Item, moduleIndex, moduleTypeId, "power", skills, dogmaContext, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)),
                CycleTimeSeconds = RoundToDecimal(cycleTimeMs / 1000d),
                ReloadTimeSeconds = RoundToDecimal(ammunitionState.ReloadTimeMs / 1000d),
                MagazineCapacity = ammunitionState.MagazineCapacity,
                ChargeUnitsPerCycle = ammunitionState.ChargeUnitsPerCycle,
                ShieldRepairPerSecond = RoundToDecimal(cycleTimeMs > 0d ? shieldRepairRate * 1000d / cycleTimeMs : shieldRepairRate),
                ArmorRepairPerSecond = RoundToDecimal(cycleTimeMs > 0d ? armorRepairRate * 1000d / cycleTimeMs : armorRepairRate),
                StructureRepairPerSecond = RoundToDecimal(cycleTimeMs > 0d ? structureRepairRate * 1000d / cycleTimeMs : structureRepairRate),
                CapacitorTransferPerSecond = RoundToDecimal(cycleTimeMs > 0d ? capacitorTransferRate * 1000d / cycleTimeMs : capacitorTransferRate),
                CapacitorUsagePerSecond = RoundToDecimal(capacitorUsagePerSecond),
                OptimalRangeMeters = RoundToDecimal(optimalRangeMeters),
                FalloffRangeMeters = RoundToDecimal(falloffRangeMeters),
                TrackingSpeed = RoundToDecimal(trackingSpeed),
                SignatureResolutionMeters = RoundToDecimal(signatureResolutionMeters),
                MissileExplosionRadiusMeters = RoundToDecimal(missileExplosionRadiusMeters),
                MissileExplosionVelocityMetersPerSecond = RoundToDecimal(missileExplosionVelocityMetersPerSecond),
                MissileDamageReductionFactor = RoundToDecimal(missileDamageReductionFactor),
                MissileDamageReductionSensitivity = RoundToDecimal(missileDamageReductionSensitivity)
            });
        }

        for (var i = 0; i < projections.Count; i++)
            projections[i] = projections[i] with { AttributeTraces = effectCache.Traces
                .Where(e => e.Key.Kind == TargetObjectKind.Item && e.Key.RefIndex == projections[i].ModuleIndex)
                .ToDictionary(e => e.Value.Attribute, e => e.Value),
                ChargeAttributeTraces = effectCache.Traces.Where(e => e.Key.Kind == TargetObjectKind.Charge && e.Key.RefIndex == projections[i].ModuleIndex).ToDictionary(e => e.Value.Attribute, e => e.Value) };
        return projections;
    }

    private static IReadOnlyList<FittingAttributeModifierTrace> TraceFittedAttribute(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext dogmaContext,
        TargetObjectKind targetKind,
        int targetRefIndex,
        string attributeName,
        int targetTypeId)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(dogmaContext);
        ArgumentException.ThrowIfNullOrEmpty(attributeName);

        var attributeId = dogmaContext.TryGetAttributeId(attributeName);
        if (attributeId == 0)
        {
            return [];
        }

        var sources = BuildSources(fit, skills, dogmaContext);
        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(dogmaContext, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        var targetGroupId = dogmaContext.GetTypeGroupId(targetTypeId);
        var traces = new List<FittingAttributeModifierTrace>();

        if (!modifierCandidateCache.TryGetValue(attributeId, out var candidates))
        {
            return traces;
        }

        foreach (var candidate in candidates)
        {
            var applies = false;
            string? skipReason = null;

            if (!IsStateAllowed(candidate.Source.State, candidate.RequiredState))
            {
                skipReason = "state_not_allowed";
            }
            else if (string.Equals(candidate.Func, "ItemModifier", StringComparison.OrdinalIgnoreCase))
            {
                applies = IsItemModifierTargetMatch(targetKind, targetRefIndex, candidate.Source, candidate.Domain);
                if (!applies)
                {
                    skipReason = "item_domain_mismatch";
                }
            }
            else if (string.Equals(candidate.Func, "LocationModifier", StringComparison.OrdinalIgnoreCase))
            {
                applies = true;
            }
            else if (string.Equals(candidate.Func, "LocationGroupModifier", StringComparison.OrdinalIgnoreCase))
            {
                applies = candidate.GroupId == targetGroupId;
                if (!applies)
                {
                    skipReason = "group_mismatch";
                }
            }
            else
            {
                var isRequiredSkillFunc =
                    string.Equals(candidate.Func, "OwnerRequiredSkillModifier", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Func, "LocationRequiredSkillModifier", StringComparison.OrdinalIgnoreCase);
                if (!isRequiredSkillFunc)
                {
                    skipReason = "unsupported_modifier_func";
                }
                else
                {
                    var requiredSkillTypeId = candidate.SkillTypeId == -1
                        ? candidate.Source.TypeId
                        : candidate.SkillTypeId;
                    applies = TargetUsesRequiredSkill(targetTypeId, requiredSkillTypeId, dogmaContext, requiredSkillCache);
                    if (!applies)
                    {
                        skipReason = "required_skill_not_matched";
                    }
                }
            }

            double? sourceValue = null;
            double? opValue = null;
            if (applies)
            {
                sourceValue = GetSourceAttributeValue(
                    candidate.Source,
                    candidate.ModifyingAttributeId,
                    dogmaContext,
                    skills,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
                opValue = ToOpValue(candidate.Operator, sourceValue.Value);
            }

            traces.Add(new FittingAttributeModifierTrace(
                SourceKind: candidate.Source.Kind.ToString(),
                SourceTypeId: candidate.Source.TypeId,
                SourceRefIndex: candidate.Source.RefIndex,
                SourceState: candidate.Source.State.ToString(),
                Func: candidate.Func,
                Domain: candidate.Domain ?? string.Empty,
                GroupId: candidate.GroupId,
                SkillTypeId: candidate.SkillTypeId,
                ModifyingAttributeId: candidate.ModifyingAttributeId,
                ModifyingAttributeName: dogmaContext.GetAttribute(candidate.ModifyingAttributeId)?.Name ?? string.Empty,
                Operator: candidate.Operator.ToString(),
                RequiredState: candidate.RequiredState.ToString(),
                SourceCategoryId: candidate.SourceCategoryId,
                Applies: applies,
                SkipReason: skipReason,
                SourceValue: sourceValue,
                OperatorValue: opValue));
        }

        return traces;
    }

    private static Pass1State RunPass1(FitDocument fit, IReadOnlyDictionary<int, int> skills, DogmaContext ctx)
    {
        var shipAttributes = CollectShipBaseAttributes(fit.ShipTypeId, ctx);
        var sources = BuildSources(fit, skills, ctx);
        return new Pass1State(fit.ShipTypeId, shipAttributes, sources);
    }

    private static Pass2State RunPass2(
        Pass1State pass1,
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx, Dictionary<string, FitAttributeExecutionTrace> traces)
    {
        ApplyShipEffects(fit, skills, ctx, pass1.ShipAttributes, pass1.Sources, traces);
        ApplyPropulsionVelocityEffects(fit, skills, ctx, pass1.ShipAttributes, pass1.Sources);
        return new Pass2State(pass1.ShipTypeId, pass1.ShipAttributes, pass1.Sources);
    }

    private static Pass3State RunPass3(
        Pass2State pass2,
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        List<string> warnings)
    {
        var moduleStats = CollectModuleStats(fit, skills, ctx, warnings, pass2.Sources);
        return new Pass3State(pass2.ShipTypeId, pass2.ShipAttributes, pass2.Sources, skills, moduleStats);
    }

    private static FittingCalculationOutput RunPass4(
        Pass3State pass3,
        List<string> warnings,
        DogmaContext ctx,
        bool includeAttributeSnapshot)
    {
        var summary = BuildSummary(pass3.ShipTypeId, pass3.ShipAttributes, pass3.Sources, pass3.Skills, pass3.ModuleStats, warnings, ctx, includeAttributeSnapshot);
        var breakdown = new Dictionary<string, decimal>(pass3.ModuleStats.Breakdown, StringComparer.OrdinalIgnoreCase)
        {
            ["volley"] = summary.Volley,
            ["dps"] = summary.Dps
        };

        return new FittingCalculationOutput(summary, breakdown, warnings);
    }

    private static Dictionary<int, double> CollectShipBaseAttributes(int shipTypeId, DogmaContext ctx)
    {
        var map = new Dictionary<int, double>();
        var typeDogma = ctx.GetTypeDogma(shipTypeId);
        if (typeDogma?.DogmaAttributes is not null)
        {
            foreach (var attr in typeDogma.DogmaAttributes)
            {
                map[attr.AttributeId] = attr.Value;
            }
        }

        // Patched SDE may keep hull mass in types.jsonl instead of typeDogma.
        // Fill only when dogma mass is absent.
        var massAttrId = ctx.TryGetAttributeId("mass");
        if (massAttrId > 0 &&
            (!map.TryGetValue(massAttrId, out var existingMass) || existingMass <= 0))
        {
            var typeMass = ctx.GetTypeMass(shipTypeId);
            if (typeMass > 0)
            {
                map[massAttrId] = typeMass;
            }
        }

        CopyTypeAttributeIfExists(ctx, shipTypeId, map, "capacity");
        CopyTypeAttributeIfExists(ctx, shipTypeId, map, "volume");
        CopyTypeAttributeIfExists(ctx, shipTypeId, map, "radius");
        return map;
    }

    private static void CopyTypeAttributeIfExists(DogmaContext ctx, int typeId, Dictionary<int, double> map, string attributeName)
    {
        var attrId = ctx.TryGetAttributeId(attributeName);
        if (attrId <= 0)
        {
            return;
        }

        var value = ctx.GetTypeAttributeValue(typeId, attrId, double.NaN);
        if (!double.IsNaN(value))
        {
            map[attrId] = value;
        }
    }

    private static readonly int[] RequiredSkillAttributeIds = { 182, 183, 184, 1285, 1289, 1290 };

    private static ModulePassStats CollectModuleStats(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        List<string> warnings,
        IReadOnlyList<SourceEntity>? sourcesOverride = null)
    {
        decimal volley = 0;
        decimal dps = 0;
        decimal dpsWithReload = 0;
        decimal droneDps = 0;
        decimal droneBandwidthLoad = 0;
        decimal droneCapacityLoad = 0;
        decimal capUsePerSecond = 0;
        decimal weaponCapUsePerSecond = 0;
        decimal activeTankCapUsePerSecond = 0;
        decimal optimalRange = 0;
        decimal tracking = 0;
        double turretDamageMultiplierWeight = 0;
        double turretDamageMultiplierWeightedTotal = 0;
        var volleyProfile = ZeroDamageVector();
        var dpsProfile = ZeroDamageVector();
        var dpsWithReloadProfile = ZeroDamageVector();
        var droneDpsProfile = ZeroDamageVector();
        var turretDpsProfile = ZeroDamageVector();
        var sources = sourcesOverride ?? BuildSources(fit, skills, ctx);
        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(ctx, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        // EVEShipFit parity:
        // no-skills mode should not apply character-derived missile multiplier bonuses.
        // When an explicit skill profile is present, keep char-domain multiplier evaluation.
        // When shipfit-style typeDogma patches are present, CharacterType already carries
        // the synthetic "missileDamage" effect that applies missileDamageMultiplier through
        // OwnerRequiredSkillModifier onto the missile damage attributes themselves.
        // Keep the old multiplier path only for unpatched data sources.
        var semantics = ctx.SemanticIds;
        var useLegacyCharacterMissileDamageMultiplier =
            skills.Count > 0 &&
            !HasAnyEffectId(ctx, CharacterTypeId, semantics.MissileDamageEffect);
        var characterMissileDamageMultiplier = useLegacyCharacterMissileDamageMultiplier
            ? GetCharacterAttributeValue("missileDamageMultiplier", ctx, sources)
            : 1d;

        for (var moduleIndex = 0; moduleIndex < fit.Modules.Count; moduleIndex++)
        {
            var module = fit.Modules[moduleIndex];
            if (!IsStatModuleSlotGroup(module.SlotGroup))
            {
                continue;
            }

            if (!IsActive(module.State))
            {
                continue;
            }

            var moduleTypeId = module.TypeId;
            if (moduleTypeId <= 0)
            {
                continue;
            }

            var moduleCycleMs = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, "speed", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (moduleCycleMs <= 0)
            {
                moduleCycleMs = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, moduleTypeId, "duration", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            }

            var damageMultiplier = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, "damageMultiplier", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache), 1);
            var missileDamageMultiplier = 1d;
            if (module.ChargeTypeId is int loadedChargeId &&
                loadedChargeId > 0 &&
                IsMissileCharge(loadedChargeId, ctx))
            {
                missileDamageMultiplier = GetPositive(characterMissileDamageMultiplier, 1d);
            }

            var moduleDamageProfile = SumDamageAttributesByType(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache);
            var chargeDamageProfile = module.ChargeTypeId is int chargeTypeId && chargeTypeId > 0
                ? SumDamageAttributesByType(TargetObjectKind.Charge, moduleIndex, chargeTypeId, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)
                : ZeroDamageVector();
            var baseDamageProfile = AddDamageVectors(moduleDamageProfile, chargeDamageProfile);
            var baseRawDamage = SumDamageVector(baseDamageProfile);
            var totalRawDamageProfile = ScaleDamageVector(baseDamageProfile, damageMultiplier * missileDamageMultiplier);
            var totalRawDamage = SumDamageVector(totalRawDamageProfile);
            var isTurretWeapon = module.ChargeTypeId is not int weaponChargeTypeId ||
                                 weaponChargeTypeId <= 0 ||
                                 !IsMissileCharge(weaponChargeTypeId, ctx);

            if (isTurretWeapon && baseRawDamage > 0)
            {
                turretDamageMultiplierWeight += baseRawDamage;
                turretDamageMultiplierWeightedTotal += baseRawDamage * damageMultiplier;
            }

            if (totalRawDamage > 0 && moduleCycleMs > 0)
            {
                var moduleDpsProfileNoReload = ScaleDamageVector(totalRawDamageProfile, 1000d / moduleCycleMs);
                var moduleDpsNoReload = totalRawDamage * 1000d / moduleCycleMs;
                volley += (decimal)totalRawDamage;
                dps += (decimal)moduleDpsNoReload;
                volleyProfile = AddDamageVectors(volleyProfile, totalRawDamageProfile);
                dpsProfile = AddDamageVectors(dpsProfile, moduleDpsProfileNoReload);
                if (isTurretWeapon)
                {
                    turretDpsProfile = AddDamageVectors(turretDpsProfile, moduleDpsProfileNoReload);
                }
                var moduleDpsSustained = ComputeModuleDpsWithReload(
                    fit,
                    moduleIndex,
                    module,
                    totalRawDamage,
                    moduleCycleMs,
                    moduleDpsNoReload,
                    skills,
                    ctx,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
                dpsWithReload += (decimal)moduleDpsSustained;
                dpsWithReloadProfile = AddDamageVectors(
                    dpsWithReloadProfile,
                    moduleDpsNoReload > 0
                        ? ScaleDamageVector(moduleDpsProfileNoReload, moduleDpsSustained / moduleDpsNoReload)
                        : ZeroDamageVector());
            }
            else if (totalRawDamage > 0 && moduleCycleMs <= 0)
            {
                warnings.Add($"Module {module.TypeName} has damage but no valid cycle time.");
            }

            var capNeed = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, "capacitorNeed", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            var moduleCapUsePerSecond = 0m;
            if (capNeed > 0 && moduleCycleMs > 0)
            {
                moduleCapUsePerSecond = (decimal)(capNeed * 1000d / moduleCycleMs);
                capUsePerSecond += moduleCapUsePerSecond;
            }

            if (moduleCapUsePerSecond > 0m)
            {
                if (totalRawDamage > 0)
                {
                    weaponCapUsePerSecond += moduleCapUsePerSecond;
                }

                var moduleShieldBoost = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, moduleTypeId, semantics.ShieldBoostRate, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
                var moduleArmorRepair = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, moduleTypeId, semantics.ArmorRepairRate, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
                var moduleHullRepair = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, moduleTypeId, semantics.HullRepairRate, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
                if (moduleShieldBoost > 0 || moduleArmorRepair > 0 || moduleHullRepair > 0)
                {
                    activeTankCapUsePerSecond += moduleCapUsePerSecond;
                }
            }

            var range = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, "maxRange", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (range <= 0 && module.ChargeTypeId is int loadedChargeTypeId && loadedChargeTypeId > 0)
            {
                range = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Charge, moduleIndex, loadedChargeTypeId, "maxRange", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
                if (range <= 0 && IsMissileCharge(loadedChargeTypeId, ctx))
                {
                    range = GetPositive(ComputeMissileFlightRange(
                        moduleIndex,
                        loadedChargeTypeId,
                        skills,
                        ctx,
                        sources,
                        effectCache,
                        inProgress,
                        modifierCandidateCache,
                        requiredSkillCache));
                }
            }

            if (range > 0)
            {
                optimalRange = Math.Max(optimalRange, (decimal)range);
            }

            var moduleTracking = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, moduleTypeId, "trackingSpeed", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (moduleTracking > 0)
            {
                tracking = Math.Max(tracking, (decimal)moduleTracking);
            }
        }

        // Count active drones into offense DPS/volley.
        foreach (var drone in fit.Drones)
        {
            if (drone.TypeId <= 0)
            {
                continue;
            }

            var launchedQty = ResolveLaunchedDroneQuantity(drone);
            var bayQty = ResolveDroneBayQuantity(drone);
            var droneBandwidthPerUnit = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Drone, 0, drone.TypeId, "droneBandwidthUsed", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (droneBandwidthPerUnit <= 0)
            {
                droneBandwidthPerUnit = GetPositive(ctx.GetTypeAttributeValue(drone.TypeId, "droneBandwidthUsed", 0d));
            }

            var droneCapacityPerUnit = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Drone, 0, drone.TypeId, "droneCapacityLoad", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (droneCapacityPerUnit <= 0)
            {
                droneCapacityPerUnit = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Drone, 0, drone.TypeId, "volume", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            }
            if (droneCapacityPerUnit <= 0)
            {
                droneCapacityPerUnit = GetPositive(ctx.GetTypeAttributeValue(drone.TypeId, "droneCapacityLoad", 0d));
            }
            if (droneCapacityPerUnit <= 0)
            {
                droneCapacityPerUnit = GetPositive(ctx.GetTypeAttributeValue(drone.TypeId, "volume", 0d));
            }

            droneBandwidthLoad += (decimal)(droneBandwidthPerUnit * launchedQty);
            droneCapacityLoad += (decimal)(droneCapacityPerUnit * bayQty);

            if (launchedQty <= 0)
            {
                continue;
            }

            var droneCycleMs = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Drone, 0, drone.TypeId, "speed", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            if (droneCycleMs <= 0)
            {
                droneCycleMs = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Drone, 0, drone.TypeId, "duration", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
            }

            var droneDamageMultiplier = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Drone, 0, drone.TypeId, "damageMultiplier", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache), 1);
            var droneDamageProfilePerShot = ScaleDamageVector(
                SumDamageAttributesByType(
                    TargetObjectKind.Drone,
                    0,
                    drone.TypeId,
                    skills,
                    ctx,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache),
                droneDamageMultiplier);
            var droneDamage = SumDamageVector(droneDamageProfilePerShot);

            if (droneDamage > 0 && droneCycleMs > 0)
            {
                var perSecond = (decimal)(droneDamage * launchedQty * 1000d / droneCycleMs);
                var dronePerSecondProfile = ScaleDamageVector(droneDamageProfilePerShot, launchedQty * 1000d / droneCycleMs);
                dps += perSecond;
                dpsWithReload += perSecond;
                droneDps += perSecond;
                dpsProfile = AddDamageVectors(dpsProfile, dronePerSecondProfile);
                dpsWithReloadProfile = AddDamageVectors(dpsWithReloadProfile, dronePerSecondProfile);
                droneDpsProfile = AddDamageVectors(droneDpsProfile, dronePerSecondProfile);
            }
        }

        var breakdown = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["volley"] = decimal.Round(volley, 3),
            ["dps"] = decimal.Round(dps, 3),
            ["dpsWithReload"] = decimal.Round(dpsWithReload, 3),
            ["droneDps"] = decimal.Round(droneDps, 3),
            ["turretDamageMultiplier"] = decimal.Round((decimal)(turretDamageMultiplierWeight > 0d
                ? turretDamageMultiplierWeightedTotal / turretDamageMultiplierWeight
                : 1d), 6),
            ["droneBandwidthLoad"] = decimal.Round(droneBandwidthLoad, 3),
            ["droneCapacityLoad"] = decimal.Round(droneCapacityLoad, 3),
            ["capacitorUsePerSecond"] = decimal.Round(capUsePerSecond, 3),
            ["weaponCapacitorUsePerSecond"] = decimal.Round(weaponCapUsePerSecond, 3),
            ["activeTankCapacitorUsePerSecond"] = decimal.Round(activeTankCapUsePerSecond, 3),
            ["weaponOptimalRange"] = decimal.Round(optimalRange, 3),
            ["weaponTracking"] = decimal.Round(tracking, 6)
        };

        return new ModulePassStats(
            Volley: volley,
            Dps: dps,
            DpsWithReload: dpsWithReload,
            DroneDps: droneDps,
            DroneBandwidthLoad: droneBandwidthLoad,
            DroneCapacityLoad: droneCapacityLoad,
            CapacitorUsePerSecond: capUsePerSecond,
            WeaponCapacitorUsePerSecond: weaponCapUsePerSecond,
            ActiveTankCapacitorUsePerSecond: activeTankCapUsePerSecond,
            WeaponOptimalRange: optimalRange,
            WeaponTracking: tracking,
            TurretDamageMultiplier: turretDamageMultiplierWeight > 0d
                ? (decimal)(turretDamageMultiplierWeightedTotal / turretDamageMultiplierWeight)
                : 1m,
            VolleyProfile: volleyProfile,
            DpsProfile: dpsProfile,
            DpsWithReloadProfile: dpsWithReloadProfile,
            DroneDpsProfile: droneDpsProfile,
            TurretDpsProfile: turretDpsProfile,
            Breakdown: breakdown);
    }

    private static double ComputeModuleDpsWithReload(
        FitDocument fit,
        int moduleIndex,
        FitModuleSlot module,
        double totalRawDamage,
        double moduleCycleMs,
        double moduleDpsNoReload,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache)
    {
        if (module.ChargeTypeId is not int chargeTypeId || chargeTypeId <= 0)
        {
            return moduleDpsNoReload;
        }

        // Fit summary stays on paper launcher output for missiles. Reload windows belong in the
        // combat kernel's time-based firing state, not the static fit-layer attribute rollup.
        if (IsMissileCharge(chargeTypeId, ctx))
        {
            return moduleDpsNoReload;
        }

        var semantics = ctx.SemanticIds;
        var patchedDpsWithReload = GetPositive(GetEffectiveTypeAttributeValue(
            TargetObjectKind.Item,
            moduleIndex,
            module.TypeId,
            semantics.DamagePerSecondWithReload,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache));
        if (patchedDpsWithReload > 0)
        {
            return patchedDpsWithReload;
        }

        var ammunitionState = ResolveModuleAmmunitionState(
            fit,
            moduleIndex,
            module,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        if (moduleCycleMs <= 0 || ammunitionState.ReloadTimeMs <= 0 || ammunitionState.ShotsPerMagazine <= 0d)
        {
            return moduleDpsNoReload;
        }

        var activeDurationMs = ammunitionState.ShotsPerMagazine * moduleCycleMs;
        var sustainedDurationMs = activeDurationMs + ammunitionState.ReloadTimeMs;
        if (sustainedDurationMs <= 0)
        {
            return moduleDpsNoReload;
        }

        return totalRawDamage * ammunitionState.ShotsPerMagazine * 1000d / sustainedDurationMs;
    }

    private static double ResolveModuleCycleMilliseconds(
        int moduleIndex,
        int moduleTypeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache)
    {
        var cycleTimeMs = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            moduleTypeId,
            "speed",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        if (cycleTimeMs > 0d)
        {
            return cycleTimeMs;
        }

        return ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            moduleTypeId,
            "duration",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
    }

    private static double ResolveModuleOptimalRangeMeters(
        int moduleIndex,
        int moduleTypeId,
        int chargeTypeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache)
    {
        var rangeMeters = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            moduleTypeId,
            "maxRange",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        if (rangeMeters > 0d || chargeTypeId <= 0)
        {
            return rangeMeters;
        }

        rangeMeters = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Charge,
            moduleIndex,
            chargeTypeId,
            "maxRange",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        if (rangeMeters > 0d || !IsMissileCharge(chargeTypeId, ctx))
        {
            return rangeMeters;
        }

        return GetPositive(ComputeMissileFlightRange(
            moduleIndex,
            chargeTypeId,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache));
    }

    private static ModuleAmmunitionState ResolveModuleAmmunitionState(
        FitDocument fit,
        int moduleIndex,
        FitModuleSlot module,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache)
    {
        if (module.ChargeTypeId is not int chargeTypeId || chargeTypeId <= 0)
        {
            return ModuleAmmunitionState.Empty;
        }

        var reloadTimeMs = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            module.TypeId,
            "reloadTime",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        var moduleCapacity = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            module.TypeId,
            "capacity",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        var chargeRate = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Item,
            moduleIndex,
            module.TypeId,
            "chargeRate",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache,
            1d);
        if (chargeRate <= 0d)
        {
            chargeRate = 1d;
        }

        var chargeVolume = ResolvePositiveEffectiveAttribute(
            TargetObjectKind.Charge,
            moduleIndex,
            chargeTypeId,
            "volume",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache);
        var shotsPerMagazine = GetPositive(GetEffectiveTypeAttributeValue(
            TargetObjectKind.Item,
            moduleIndex,
            module.TypeId,
            ctx.SemanticIds.ChargeAmount,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache));
        if (shotsPerMagazine <= 0d)
        {
            shotsPerMagazine = chargeVolume > 0d
                ? Math.Floor(moduleCapacity / chargeVolume / chargeRate)
                : Math.Floor(moduleCapacity / chargeRate);
        }

        if (shotsPerMagazine < 1d && chargeVolume <= 0d)
        {
            shotsPerMagazine = Math.Floor(moduleCapacity / chargeRate);
        }

        if (shotsPerMagazine < 1d)
        {
            return new ModuleAmmunitionState(reloadTimeMs, 0d, 0, 1);
        }

        var chargeUnitsPerCycle = Math.Max(1, (int)Math.Round(chargeRate, MidpointRounding.AwayFromZero));
        var magazineCapacity = Math.Max(1, (int)Math.Floor(shotsPerMagazine + 0.000001d)) * chargeUnitsPerCycle;
        return new ModuleAmmunitionState(reloadTimeMs, shotsPerMagazine, magazineCapacity, chargeUnitsPerCycle);
    }

    private static double ResolvePositiveEffectiveAttribute(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        string attributeName,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache,
        double fallback = 0d)
    {
        var value = GetPositive(GetEffectiveTypeAttributeValue(
            targetKind,
            targetRefIndex,
            targetTypeId,
            attributeName,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache), fallback);
        if (value > 0d || targetTypeId <= 0)
        {
            return value;
        }

        return GetPositive(ctx.GetTypeAttributeValue(targetTypeId, attributeName, fallback), fallback);
    }

    private static double ResolvePositiveEffectiveAttribute(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        int attributeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache,
        double fallback = 0d)
    {
        if (attributeId <= 0)
        {
            return fallback;
        }

        var value = GetPositive(GetEffectiveTypeAttributeValue(
            targetKind,
            targetRefIndex,
            targetTypeId,
            attributeId,
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache), fallback);
        if (value > 0d || targetTypeId <= 0)
        {
            return value;
        }

        return GetPositive(ctx.GetTypeAttributeValue(targetTypeId, attributeId, fallback), fallback);
    }

    private static WeaponApplicationKind ResolveProjectedWeaponApplicationKind(int moduleTypeId, int chargeTypeId, DogmaContext ctx)
    {
        if (moduleTypeId <= 0)
        {
            return WeaponApplicationKind.Direct;
        }

        var typeDogma = ctx.GetTypeDogma(moduleTypeId);
        if (typeDogma?.DogmaEffects is null || typeDogma.DogmaEffects.Count == 0)
        {
            return WeaponApplicationKind.Direct;
        }

        var applicationKind = ctx.RuleSet.ResolveWeaponApplicationKind(typeDogma.DogmaEffects.Select(typeEffect =>
            new Rules.DogmaResolvedEffect(typeEffect.EffectId, ctx.GetEffect(typeEffect.EffectId)?.Name)));
        if (applicationKind == WeaponApplicationKind.Turret || applicationKind == WeaponApplicationKind.Missile)
        {
            return applicationKind;
        }

        if (LooksLikeTurretWeapon(moduleTypeId, chargeTypeId, ctx))
        {
            return WeaponApplicationKind.Turret;
        }

        if (applicationKind == WeaponApplicationKind.Drone && !IsDroneWeaponType(moduleTypeId, ctx))
        {
            return WeaponApplicationKind.Direct;
        }

        return applicationKind;
    }

    private static bool LooksLikeTurretWeapon(int moduleTypeId, int chargeTypeId, DogmaContext ctx)
    {
        if (moduleTypeId <= 0)
        {
            return false;
        }

        if (ctx.GetTypeAttributeValue(moduleTypeId, "trackingSpeed", 0d) <= 0d)
        {
            return false;
        }

        if (ctx.GetTypeAttributeValue(moduleTypeId, "maxRange", 0d) > 0d)
        {
            return true;
        }

        if (ctx.GetTypeAttributeValue(moduleTypeId, "falloff", 0d) > 0d)
        {
            return true;
        }

        if (ctx.GetTypeAttributeValue(moduleTypeId, "signatureResolution", 0d) > 0d)
        {
            return true;
        }

        if (chargeTypeId > 0 &&
            (ctx.GetTypeAttributeValue(chargeTypeId, "maxRange", 0d) > 0d ||
             ctx.GetTypeAttributeValue(chargeTypeId, "falloff", 0d) > 0d ||
             ctx.GetTypeAttributeValue(chargeTypeId, "signatureResolution", 0d) > 0d))
        {
            return true;
        }

        return false;
    }

    private static bool IsDroneWeaponType(int typeId, DogmaContext ctx)
    {
        if (typeId <= 0)
        {
            return false;
        }

        return ctx.GetTypeCategoryId(typeId) == 18;
    }

    private static double GetEffectiveTypeAttributeValue(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        string targetAttributeName,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>? inProgress = null,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache = null,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache = null)
    {
        var targetAttributeId = ctx.TryGetAttributeId(targetAttributeName);
        if (targetAttributeId == 0)
        {
            return ctx.GetTypeAttributeValue(targetTypeId, targetAttributeName);
        }

        var progress = inProgress ?? new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        return GetEffectiveTypeAttributeValueById(
            targetKind,
            targetRefIndex,
            targetTypeId,
            targetAttributeId,
            skills,
            ctx,
            sources,
            effectCache,
            progress,
            modifierCandidateCache,
            requiredSkillCache);
    }

    private static double GetEffectiveTypeAttributeValue(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        int targetAttributeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>? inProgress = null,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache = null,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache = null)
    {
        if (targetAttributeId == 0)
        {
            return 0d;
        }

        var progress = inProgress ?? new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        return GetEffectiveTypeAttributeValueById(
            targetKind,
            targetRefIndex,
            targetTypeId,
            targetAttributeId,
            skills,
            ctx,
            sources,
            effectCache,
            progress,
            modifierCandidateCache,
            requiredSkillCache);
    }

    private static double GetEffectiveTypeAttributeValueById(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        int targetAttributeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache)
    {
        var cacheKey = (targetKind, targetRefIndex, targetTypeId, targetAttributeId);
        if (effectCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var meta = ctx.GetAttribute(targetAttributeId);
        var baseValue = ResolveBaseAttributeValue(targetKind, targetTypeId, targetAttributeId, ctx, meta?.DefaultValue ?? 0d);
        if (inProgress.Contains(cacheKey))
        {
            return baseValue;
        }

        inProgress.Add(cacheKey);
        var effects = CollectEffectsForTarget(targetKind, targetRefIndex, targetTypeId, targetAttributeId, skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache);
        var trace = effectCache is TracedAttributeCache ? new FitAttributeExecutionTrace
            { Attribute = meta?.Name ?? targetAttributeId.ToString(), BaseValue = baseValue, FinalValue = baseValue } : null;
        if (trace is not null) ((TracedAttributeCache)effectCache).Traces[cacheKey] = trace;
        if (effects.Count == 0)
        {
            effectCache[cacheKey] = baseValue;
            inProgress.Remove(cacheKey);
            return baseValue;
        }

        var value = ApplyEffects(baseValue, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false, trace?.Steps);
        if (trace is not null) trace.FinalValue = value;
        effectCache[cacheKey] = value;
        inProgress.Remove(cacheKey);
        return value;
    }

    private static List<PendingEffect> CollectEffectsForTarget(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int targetTypeId,
        int targetAttributeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache)
    {
        var results = new List<PendingEffect>();
        List<TargetModifierCandidate> candidates;
        if (modifierCandidateCache is not null)
        {
            candidates = modifierCandidateCache.TryGetValue(targetAttributeId, out var cachedCandidates)
                ? cachedCandidates
                : EmptyModifierCandidates;
        }
        else
        {
            var fallbackIndex = BuildTargetModifierIndex(ctx, sources);
            candidates = fallbackIndex.TryGetValue(targetAttributeId, out var fallbackCandidates)
                ? fallbackCandidates
                : EmptyModifierCandidates;
        }

        foreach (var candidate in candidates)
        {
            if (!IsStateAllowed(candidate.Source.State, candidate.RequiredState))
            {
                continue;
            }

            if (string.Equals(candidate.Func, "ItemModifier", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsItemModifierTargetMatch(targetKind, targetRefIndex, candidate.Source, candidate.Domain))
                {
                    continue;
                }

                var itemSourceValue = GetSourceAttributeValue(
                    candidate.Source,
                    candidate.ModifyingAttributeId,
                    ctx,
                    skills,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache);
                results.Add(new PendingEffect(candidate.Operator, itemSourceValue, candidate.SourceCategoryId, CaptureModifierSource(candidate, ctx)));
                continue;
            }

            if (string.Equals(candidate.Func, "LocationModifier", StringComparison.OrdinalIgnoreCase))
            {
                var locationSourceValue = GetSourceAttributeValue(
                    candidate.Source,
                    candidate.ModifyingAttributeId,
                    ctx,
                    skills,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache);
                results.Add(new PendingEffect(candidate.Operator, locationSourceValue, candidate.SourceCategoryId, CaptureModifierSource(candidate, ctx)));
                continue;
            }

            if (string.Equals(candidate.Func, "LocationGroupModifier", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.GroupId != ctx.GetTypeGroupId(targetTypeId))
                {
                    continue;
                }

                var locationGroupSourceValue = GetSourceAttributeValue(
                    candidate.Source,
                    candidate.ModifyingAttributeId,
                    ctx,
                    skills,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache);
                results.Add(new PendingEffect(candidate.Operator, locationGroupSourceValue, candidate.SourceCategoryId, CaptureModifierSource(candidate, ctx)));
                continue;
            }

            var isRequiredSkillFunc =
                string.Equals(candidate.Func, "OwnerRequiredSkillModifier", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Func, "LocationRequiredSkillModifier", StringComparison.OrdinalIgnoreCase);
            if (!isRequiredSkillFunc)
            {
                continue;
            }

            var requiredSkillTypeId = candidate.SkillTypeId;
            if (requiredSkillTypeId == -1)
            {
                requiredSkillTypeId = candidate.Source.TypeId;
            }

            if (!TargetUsesRequiredSkill(targetTypeId, requiredSkillTypeId, ctx, requiredSkillCache))
            {
                continue;
            }

            var requiredSkillSourceValue = GetSourceAttributeValue(
                candidate.Source,
                candidate.ModifyingAttributeId,
                ctx,
                skills,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            results.Add(new PendingEffect(candidate.Operator, requiredSkillSourceValue, candidate.SourceCategoryId, CaptureModifierSource(candidate, ctx)));
        }

        return results;
    }

    private static Dictionary<int, List<TargetModifierCandidate>> BuildTargetModifierIndex(
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var map = new Dictionary<int, List<TargetModifierCandidate>>();
        foreach (var source in sources)
        {
            var typeDogma = ctx.GetTypeDogma(source.TypeId);
            if (typeDogma?.DogmaEffects is null)
            {
                continue;
            }

            var sourceCategoryId = ctx.GetTypeCategoryId(source.TypeId);
            foreach (var typeEffect in typeDogma.DogmaEffects)
            {
                if (!IsSourceEffectEnabled(source, typeEffect.EffectId))
                {
                    continue;
                }

                var effect = ctx.GetEffect(typeEffect.EffectId);
                if (effect?.ModifierInfo is null || effect.ModifierInfo.Count == 0)
                {
                    continue;
                }

                var requiredState = ToEffectState(effect.EffectCategoryId ?? 0);
                foreach (var modifier in effect.ModifierInfo)
                {
                    if (modifier.ModifiedAttributeId is null || modifier.ModifyingAttributeId is null || modifier.Operation is null)
                    {
                        continue;
                    }

                    var op = ToOperator(modifier.Operation.Value);
                    if (op is null)
                    {
                        continue;
                    }

                    var targetAttributeId = modifier.ModifiedAttributeId.Value;
                    if (!map.TryGetValue(targetAttributeId, out var list))
                    {
                        list = new List<TargetModifierCandidate>();
                        map[targetAttributeId] = list;
                    }

                    list.Add(new TargetModifierCandidate(
                        Source: source,
                        Func: modifier.Func ?? string.Empty,
                        Domain: modifier.Domain,
                        GroupId: modifier.GroupId.GetValueOrDefault(),
                        SkillTypeId: modifier.SkillTypeId.GetValueOrDefault(),
                        ModifyingAttributeId: modifier.ModifyingAttributeId.Value,
                        Operator: op.Value,
                        RequiredState: requiredState,
                        SourceCategoryId: sourceCategoryId, EffectId: typeEffect.EffectId));
                }
            }
        }

        return map;
    }

    private static bool TargetUsesRequiredSkill(
        int targetTypeId,
        int requiredSkillTypeId,
        DogmaContext ctx,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? cache = null)
    {
        if (requiredSkillTypeId <= 0)
        {
            return false;
        }
        var key = (targetTypeId, requiredSkillTypeId);
        if (cache is not null && cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var matched = false;
        foreach (var attrId in RequiredSkillAttributeIds)
        {
            var value = ctx.GetTypeAttributeValue(targetTypeId, attrId, 0d);
            if (Math.Abs(value - requiredSkillTypeId) < 0.5d)
            {
                matched = true;
                break;
            }
        }

        if (cache is not null)
        {
            cache[key] = matched;
        }

        return matched;
    }

    private static bool IsItemModifierTargetMatch(
        TargetObjectKind targetKind,
        int targetRefIndex,
        SourceEntity source,
        string? domain)
    {
        var d = domain ?? string.Empty;
        if (string.Equals(d, "shipID", StringComparison.OrdinalIgnoreCase))
        {
            return targetKind == TargetObjectKind.Ship;
        }

        if (string.Equals(d, "itemID", StringComparison.OrdinalIgnoreCase))
        {
            return source.Kind switch
            {
                SourceKind.Ship => targetKind == TargetObjectKind.Ship,
                SourceKind.Char => targetKind == TargetObjectKind.Char,
                SourceKind.Item => targetKind == TargetObjectKind.Item && source.RefIndex == targetRefIndex,
                SourceKind.Charge => targetKind == TargetObjectKind.Charge && source.RefIndex == targetRefIndex,
                SourceKind.Drone => targetKind == TargetObjectKind.Drone,
                SourceKind.Implant => targetKind == TargetObjectKind.Implant && source.RefIndex == targetRefIndex,
                SourceKind.Booster => targetKind == TargetObjectKind.Booster && source.RefIndex == targetRefIndex,
                _ => false
            };
        }

        if (string.Equals(d, "charID", StringComparison.OrdinalIgnoreCase))
        {
            return targetKind == TargetObjectKind.Char;
        }

        if (string.Equals(d, "structureID", StringComparison.OrdinalIgnoreCase))
        {
            return targetKind == TargetObjectKind.Structure;
        }

        if (string.Equals(d, "targetID", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d, "target", StringComparison.OrdinalIgnoreCase))
        {
            return targetKind == TargetObjectKind.Target;
        }

        if (string.Equals(d, "otherID", StringComparison.OrdinalIgnoreCase))
        {
            return (source.Kind == SourceKind.Item && targetKind == TargetObjectKind.Charge && source.RefIndex == targetRefIndex) ||
                   (source.Kind == SourceKind.Charge && targetKind == TargetObjectKind.Item && source.RefIndex == targetRefIndex);
        }

        return false;
    }

    private static void ApplyShipEffects(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        Dictionary<int, double> shipAttributes,
        IReadOnlyList<SourceEntity>? sourcesOverride = null,
        Dictionary<string, FitAttributeExecutionTrace>? traces = null)
    {
        var pending = new Dictionary<int, List<PendingEffect>>();
        var sources = sourcesOverride ?? BuildSources(fit, skills, ctx);
        var shipGroupId = ctx.GetTypeGroupId(fit.ShipTypeId);
        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();

        foreach (var source in sources)
        {
            var typeDogma = ctx.GetTypeDogma(source.TypeId);
            if (typeDogma?.DogmaEffects is null)
            {
                continue;
            }

            var sourceCategoryId = ctx.GetTypeCategoryId(source.TypeId);
            foreach (var typeEffect in typeDogma.DogmaEffects)
            {
                if (!IsSourceEffectEnabled(source, typeEffect.EffectId))
                {
                    continue;
                }

                var effect = ctx.GetEffect(typeEffect.EffectId);
                if (effect?.ModifierInfo is null || effect.ModifierInfo.Count == 0)
                {
                    continue;
                }

                var requiredState = ToEffectState(effect.EffectCategoryId ?? 0);
                if (!IsStateAllowed(source.State, requiredState))
                {
                    continue;
                }

                foreach (var modifier in effect.ModifierInfo)
                {
                    if (modifier.ModifiedAttributeId is null || modifier.ModifyingAttributeId is null || modifier.Operation is null)
                    {
                        continue;
                    }

                    var op = ToOperator(modifier.Operation.Value);
                    if (op is null)
                    {
                        continue;
                    }

                    if (string.Equals(modifier.Func, "ItemModifier", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsShipTarget(source, modifier.Domain))
                        {
                            continue;
                        }

                        AddPending(shipAttributes, pending, ctx, fit.ShipTypeId, modifier.ModifiedAttributeId.Value, new PendingEffect(
                            Operator: op.Value,
                            SourceValue: GetSourceAttributeValue(
                                source,
                                modifier.ModifyingAttributeId.Value,
                                ctx,
                                skills,
                                sources,
                                effectCache,
                                inProgress),
                            SourceCategoryId: sourceCategoryId, TraceSource: new FitModifierExecutionStep
                        {
                            SourceTypeId = source.TypeId, SourceKind = source.Kind.ToString(), SourceIndex = source.RefIndex,
                            SourceState = source.State.ToString(), SkillLevel = source.SkillLevel, EffectId = typeEffect.EffectId,
                            ModifyingAttributeId = modifier.ModifyingAttributeId.Value,
                            SourceBaseValue = GetTypeAttributeValue(source.TypeId, modifier.ModifyingAttributeId.Value, ctx)
                        }));
                        continue;
                    }

                    if (string.Equals(modifier.Func, "LocationModifier", StringComparison.OrdinalIgnoreCase))
                    {
                        AddPending(shipAttributes, pending, ctx, fit.ShipTypeId, modifier.ModifiedAttributeId.Value, new PendingEffect(
                            Operator: op.Value,
                            SourceValue: GetSourceAttributeValue(
                                source,
                                modifier.ModifyingAttributeId.Value,
                                ctx,
                                skills,
                                sources,
                                effectCache,
                                inProgress),
                            SourceCategoryId: sourceCategoryId, TraceSource: new FitModifierExecutionStep
                        {
                            SourceTypeId = source.TypeId, SourceKind = source.Kind.ToString(), SourceIndex = source.RefIndex,
                            SourceState = source.State.ToString(), SkillLevel = source.SkillLevel, EffectId = typeEffect.EffectId,
                            ModifyingAttributeId = modifier.ModifyingAttributeId.Value,
                            SourceBaseValue = GetTypeAttributeValue(source.TypeId, modifier.ModifyingAttributeId.Value, ctx)
                        }));
                        continue;
                    }

                    if (string.Equals(modifier.Func, "LocationGroupModifier", StringComparison.OrdinalIgnoreCase))
                    {
                        if (modifier.GroupId.GetValueOrDefault() != shipGroupId)
                        {
                            continue;
                        }

                        AddPending(shipAttributes, pending, ctx, fit.ShipTypeId, modifier.ModifiedAttributeId.Value, new PendingEffect(
                            Operator: op.Value,
                            SourceValue: GetSourceAttributeValue(
                                source,
                                modifier.ModifyingAttributeId.Value,
                                ctx,
                                skills,
                                sources,
                                effectCache,
                                inProgress),
                            SourceCategoryId: sourceCategoryId, TraceSource: new FitModifierExecutionStep
                        {
                            SourceTypeId = source.TypeId, SourceKind = source.Kind.ToString(), SourceIndex = source.RefIndex,
                            SourceState = source.State.ToString(), SkillLevel = source.SkillLevel, EffectId = typeEffect.EffectId,
                            ModifyingAttributeId = modifier.ModifyingAttributeId.Value,
                            SourceBaseValue = GetTypeAttributeValue(source.TypeId, modifier.ModifyingAttributeId.Value, ctx)
                        }));
                        continue;
                    }

                    var isRequiredSkillFunc =
                        string.Equals(modifier.Func, "OwnerRequiredSkillModifier", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(modifier.Func, "LocationRequiredSkillModifier", StringComparison.OrdinalIgnoreCase);
                    if (!isRequiredSkillFunc)
                    {
                        continue;
                    }

                    var requiredSkillTypeId = modifier.SkillTypeId.GetValueOrDefault();
                    if (requiredSkillTypeId == -1)
                    {
                        requiredSkillTypeId = source.TypeId;
                    }

                    if (!TargetUsesRequiredSkill(fit.ShipTypeId, requiredSkillTypeId, ctx))
                    {
                        continue;
                    }

                    AddPending(shipAttributes, pending, ctx, fit.ShipTypeId, modifier.ModifiedAttributeId.Value, new PendingEffect(
                        Operator: op.Value,
                        SourceValue: GetSourceAttributeValue(
                            source,
                            modifier.ModifyingAttributeId.Value,
                            ctx,
                            skills,
                            sources,
                            effectCache,
                            inProgress),
                        SourceCategoryId: sourceCategoryId, TraceSource: new FitModifierExecutionStep
                        {
                            SourceTypeId = source.TypeId, SourceKind = source.Kind.ToString(), SourceIndex = source.RefIndex,
                            SourceState = source.State.ToString(), SkillLevel = source.SkillLevel, EffectId = typeEffect.EffectId,
                            ModifyingAttributeId = modifier.ModifyingAttributeId.Value,
                            SourceBaseValue = GetTypeAttributeValue(source.TypeId, modifier.ModifyingAttributeId.Value, ctx)
                        }));
                }
            }
        }

        if (traces is not null)
            foreach (var entry in shipAttributes)
            {
                var name = ctx.GetAttribute(entry.Key)?.Name ?? entry.Key.ToString();
                traces[name] = new FitAttributeExecutionTrace { Attribute=name, BaseValue=entry.Value, FinalValue=entry.Value };
            }
        foreach (var (attributeId, effects) in pending)
        {
            var meta = ctx.GetAttribute(attributeId);
            var current = shipAttributes.TryGetValue(attributeId, out var currentValue)
                ? currentValue
                : ctx.GetTypeAttributeValue(fit.ShipTypeId, attributeId, meta?.DefaultValue ?? 0);

            var trace = new FitAttributeExecutionTrace { Attribute = meta?.Name ?? attributeId.ToString(), BaseValue = current };
            shipAttributes[attributeId] = ApplyEffects(current, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false, trace.Steps);
            trace.FinalValue = shipAttributes[attributeId];
            if (traces is not null) traces[trace.Attribute] = trace;
        }
    }

    private static double GetCharacterAttributeValue(
        string targetAttributeName,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var targetAttributeId = ctx.TryGetAttributeId(targetAttributeName);
        if (targetAttributeId == 0)
        {
            return 1d;
        }

        var effects = new List<PendingEffect>();
        foreach (var source in sources)
        {
            var typeDogma = ctx.GetTypeDogma(source.TypeId);
            if (typeDogma?.DogmaEffects is null)
            {
                continue;
            }

            foreach (var typeEffect in typeDogma.DogmaEffects)
            {
                if (!IsSourceEffectEnabled(source, typeEffect.EffectId))
                {
                    continue;
                }

                var effect = ctx.GetEffect(typeEffect.EffectId);
                if (effect?.ModifierInfo is null || effect.ModifierInfo.Count == 0)
                {
                    continue;
                }

                var requiredState = ToEffectState(effect.EffectCategoryId ?? 0);
                if (!IsStateAllowed(source.State, requiredState))
                {
                    continue;
                }

                foreach (var modifier in effect.ModifierInfo)
                {
                    if (!string.Equals(modifier.Func, "ItemModifier", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.Equals(modifier.Domain, "charID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (modifier.ModifiedAttributeId != targetAttributeId || modifier.ModifyingAttributeId is null || modifier.Operation is null)
                    {
                        continue;
                    }

                    var op = ToOperator(modifier.Operation.Value);
                    if (op is null)
                    {
                        continue;
                    }

                    effects.Add(new PendingEffect(
                        op.Value,
                        GetSourceAttributeValue(source, modifier.ModifyingAttributeId.Value, ctx),
                        ctx.GetTypeCategoryId(source.TypeId)));
                }
            }
        }

        var meta = ctx.GetAttribute(targetAttributeId);
        var baseValue = meta?.DefaultValue ?? 1d;
        return effects.Count == 0 ? baseValue : ApplyEffects(baseValue, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false);
    }

    private static bool IsMissileCharge(int chargeTypeId, DogmaContext ctx)
    {
        if (chargeTypeId <= 0)
        {
            return false;
        }

        // Missile charges carry explosion/aoe style attributes.
        if (ctx.GetTypeAttributeValue(chargeTypeId, "aoeVelocity", 0d) > 0d)
        {
            return true;
        }

        if (ctx.GetTypeAttributeValue(chargeTypeId, "aoeCloudSize", 0d) > 0d)
        {
            return true;
        }

        if (ctx.GetTypeAttributeValue(chargeTypeId, "explosionDelay", 0d) > 0d)
        {
            return true;
        }

        // launcherGroup is also present on hybrid/projectile charges; it is not
        // evidence that a charge uses missile damage application.

        return false;
    }

    private static double ComputeMissileFlightRange(
        int moduleIndex,
        int chargeTypeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>> modifierCandidateCache,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool> requiredSkillCache)
    {
        var missileVelocity = GetPositive(GetEffectiveTypeAttributeValue(
            TargetObjectKind.Charge,
            moduleIndex,
            chargeTypeId,
            "maxVelocity",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache));
        if (missileVelocity <= 0)
        {
            missileVelocity = GetPositive(ctx.GetTypeAttributeValue(chargeTypeId, "maxVelocity", 0d));
        }

        var flightTimeMs = GetPositive(GetEffectiveTypeAttributeValue(
            TargetObjectKind.Charge,
            moduleIndex,
            chargeTypeId,
            "explosionDelay",
            skills,
            ctx,
            sources,
            effectCache,
            inProgress,
            modifierCandidateCache,
            requiredSkillCache));
        if (flightTimeMs <= 0)
        {
            flightTimeMs = GetPositive(ctx.GetTypeAttributeValue(chargeTypeId, "explosionDelay", 0d));
        }

        if (missileVelocity <= 0 || flightTimeMs <= 0)
        {
            return 0d;
        }

        return missileVelocity * flightTimeMs / 1000d;
    }

    private static DamageVector SumDamageAttributesByType(int typeId, DogmaContext ctx)
    {
        return new DamageVector(
            GetPositive(ctx.GetTypeAttributeValue(typeId, "emDamage", 0d)),
            GetPositive(ctx.GetTypeAttributeValue(typeId, "thermalDamage", 0d)),
            GetPositive(ctx.GetTypeAttributeValue(typeId, "kineticDamage", 0d)),
            GetPositive(ctx.GetTypeAttributeValue(typeId, "explosiveDamage", 0d)));
    }

    private static void ApplyPropulsionVelocityEffects(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        Dictionary<int, double> shipAttributes,
        IReadOnlyList<SourceEntity>? sourcesOverride = null)
    {
        var semantics = ctx.SemanticIds;
        // EVEShipFit patch model:
        // propulsionModules.yaml injects attribute/effect "velocityBoost" and applies it via dogma modifiers.
        // If present, maxVelocity has already been derived by ApplyShipEffects and must not be recalculated here.
        if (HasAnyEffectId(ctx, fit.ShipTypeId, semantics.VelocityBoostEffect))
        {
            return;
        }

        var maxVelocityId = ctx.TryGetAttributeId("maxVelocity");
        if (maxVelocityId <= 0 || !shipAttributes.TryGetValue(maxVelocityId, out var baseVelocity) || baseVelocity <= 0)
        {
            return;
        }

        var massId = ctx.TryGetAttributeId("mass");
        var shipMass = 0d;
        if (massId > 0)
        {
            if (shipAttributes.TryGetValue(massId, out var massValue))
            {
                shipMass = GetPositive(massValue);
            }
            if (shipMass <= 0)
            {
                // Fall back to type mass when dogma mass is unavailable.
                shipMass = GetPositive(ctx.GetTypeAttributeValue(fit.ShipTypeId, massId, 0d));
            }
        }

        var sources = sourcesOverride ?? BuildSources(fit, skills, ctx);
        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();

        var velocityMultiplier = 1d;
        var totalMassAddition = 0d;
        var signatureRadiusMultiplier = 1d;
        for (var moduleIndex = 0; moduleIndex < fit.Modules.Count; moduleIndex++)
        {
            var module = fit.Modules[moduleIndex];
            if (!IsActive(module.State) || module.TypeId <= 0)
            {
                continue;
            }

            if (!HasAnyEffectId(ctx, module.TypeId, semantics.ModuleBonusAfterburnerEffect, semantics.ModuleBonusMicrowarpdriveEffect))
            {
                continue;
            }

            var speedFactor = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item, moduleIndex, module.TypeId, "speedFactor", skills, ctx, sources, effectCache));
            if (speedFactor > 0)
            {
                var massAddition = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, module.TypeId, "massAddition", skills, ctx, sources, effectCache));
                if (massAddition <= 0)
                {
                    massAddition = GetPositive(ctx.GetTypeAttributeValue(module.TypeId, "massAddition", 0d));
                }

                var speedBoostFactor = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, module.TypeId, "speedBoostFactor", skills, ctx, sources, effectCache));
                if (speedBoostFactor <= 0)
                {
                    speedBoostFactor = GetPositive(ctx.GetTypeAttributeValue(module.TypeId, "speedBoostFactor", 0d));
                }
                var signatureRadiusBonus = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item, moduleIndex, module.TypeId, "signatureRadiusBonus", skills, ctx, sources, effectCache));
                if (signatureRadiusBonus <= 0)
                {
                    signatureRadiusBonus = GetPositive(ctx.GetTypeAttributeValue(module.TypeId, "signatureRadiusBonus", 0d));
                }

                var perModuleMultiplier = 1d + speedFactor / 100d;
                var totalMass = shipMass + massAddition;
                if (speedBoostFactor > 0 && totalMass > 0)
                {
                    var massFactor = Clamp(speedBoostFactor / totalMass, 0d, 1d);
                    perModuleMultiplier = 1d + (speedFactor / 100d) * massFactor;
                }

                velocityMultiplier *= perModuleMultiplier;
                totalMassAddition += massAddition;
                if (signatureRadiusBonus > 0)
                {
                    signatureRadiusMultiplier *= 1d + signatureRadiusBonus / 100d;
                }
                continue;
            }

            // No heuristic fallback here; keep propulsion behavior data-driven.
        }

        shipAttributes[maxVelocityId] = baseVelocity * velocityMultiplier;
        if (massId > 0 && shipMass > 0)
        {
            shipAttributes[massId] = shipMass + totalMassAddition;
        }

        var signatureRadiusId = ctx.TryGetAttributeId("signatureRadius");
        if (signatureRadiusId > 0 && signatureRadiusMultiplier > 1d)
        {
            var baseSignature = shipAttributes.TryGetValue(signatureRadiusId, out var sig)
                ? sig
                : ctx.GetTypeAttributeValue(fit.ShipTypeId, signatureRadiusId, 0d);
            if (baseSignature > 0)
            {
                shipAttributes[signatureRadiusId] = baseSignature * signatureRadiusMultiplier;
            }
        }
    }

    private static bool HasAnyEffectName(DogmaContext ctx, int typeId, params string[] names)
    {
        var typeDogma = ctx.GetTypeDogma(typeId);
        if (typeDogma?.DogmaEffects is null || typeDogma.DogmaEffects.Count == 0)
        {
            return false;
        }

        foreach (var effectRef in typeDogma.DogmaEffects)
        {
            var effect = ctx.GetEffect(effectRef.EffectId);
            if (effect is null || string.IsNullOrWhiteSpace(effect.Name))
            {
                continue;
            }

            foreach (var name in names)
            {
                if (string.Equals(effect.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnyEffectId(DogmaContext ctx, int typeId, params int[] effectIds)
    {
        if (effectIds.Length == 0 || effectIds.All(effectId => effectId == 0))
        {
            return false;
        }

        var typeDogma = ctx.GetTypeDogma(typeId);
        if (typeDogma?.DogmaEffects is null || typeDogma.DogmaEffects.Count == 0)
        {
            return false;
        }

        foreach (var effectRef in typeDogma.DogmaEffects)
        {
            if (effectIds.Contains(effectRef.EffectId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShipTarget(SourceEntity source, string domain)
    {
        if (string.Equals(domain, "shipID", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(domain, "itemID", StringComparison.OrdinalIgnoreCase) && source.Kind == SourceKind.Ship)
        {
            return true;
        }

        return false;
    }

    private static double GetTypeAttributeValue(int typeId, int attributeId, DogmaContext ctx)
    {
        return ctx.GetTypeAttributeValue(typeId, attributeId, ctx.GetAttribute(attributeId)?.DefaultValue ?? 0d);
    }

    private static double ResolveBaseAttributeValue(
        TargetObjectKind targetKind,
        int targetTypeId,
        int targetAttributeId,
        DogmaContext ctx,
        double fallback)
    {
        var baseValue = ctx.GetTypeAttributeValue(targetTypeId, targetAttributeId, fallback);
        if (targetKind != TargetObjectKind.Ship || baseValue > 0)
        {
            return baseValue;
        }

        var massAttributeId = ctx.TryGetAttributeId("mass");
        if (massAttributeId > 0 && targetAttributeId == massAttributeId)
        {
            var typeMass = ctx.GetTypeMass(targetTypeId);
            if (typeMass > 0)
            {
                return typeMass;
            }
        }

        return baseValue;
    }

    private static double GetSourceAttributeValue(SourceEntity source, int attributeId, DogmaContext ctx)
    {
        var value = GetTypeAttributeValue(source.TypeId, attributeId, ctx);
        if (source.Kind == SourceKind.Skill)
        {
            if (attributeId == SkillLevelAttributeId)
            {
                return source.SkillLevel;
            }

            // EVEShipFit parity: skill modifiers are generally "per level" values
            // carried by the skill type attribute and scaled by current skill level.
            return value * source.SkillLevel;
        }

        return value;
    }

    private static double GetSourceAttributeValue(
        SourceEntity source,
        int attributeId,
        DogmaContext ctx,
        IReadOnlyDictionary<int, int> skills,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)> inProgress,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache = null,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache = null)
    {
        var value = source.Kind switch
        {
            SourceKind.Ship => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Ship,
                0,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Char => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Char,
                0,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Structure => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Structure,
                0,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Target => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Target,
                0,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Item when source.RefIndex >= 0 => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Item,
                source.RefIndex,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Charge when source.RefIndex >= 0 => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Charge,
                source.RefIndex,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Drone => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Drone,
                0,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Implant => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Implant,
                source.RefIndex,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            SourceKind.Booster => GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Booster,
                source.RefIndex,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache),
            _ => GetTypeAttributeValue(source.TypeId, attributeId, ctx)
        };

        if (source.Kind == SourceKind.Skill)
        {
            if (attributeId == SkillLevelAttributeId)
            {
                return source.SkillLevel;
            }

            return value * source.SkillLevel;
        }

        return value;
    }

    private static void AddPending(
        Dictionary<int, double> shipAttributes,
        Dictionary<int, List<PendingEffect>> pending,
        DogmaContext ctx,
        int shipTypeId,
        int attributeId,
        PendingEffect effect)
    {
        if (!shipAttributes.ContainsKey(attributeId))
        {
            shipAttributes[attributeId] = ctx.GetTypeAttributeValue(shipTypeId, attributeId, ctx.GetAttribute(attributeId)?.DefaultValue ?? 0d);
        }

        if (!pending.TryGetValue(attributeId, out var list))
        {
            list = new List<PendingEffect>();
            pending[attributeId] = list;
        }

        list.Add(effect);
    }

    private static List<SourceEntity> BuildSources(
        FitDocument fit,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx)
    {
        var result = new List<SourceEntity>
        {
            new(SourceKind.Ship, fit.ShipTypeId, EffectState.Active, 1, -1),
            new(SourceKind.Char, CharacterTypeId, EffectState.Active, 1, -1),
            new(SourceKind.Structure, 0, EffectState.Active, 1, -1),
            new(SourceKind.Target, 0, EffectState.Active, 1, -1)
        };

        for (var i = 0; i < fit.Modules.Count; i++)
        {
            var module = fit.Modules[i];
            if (module.TypeId <= 0)
            {
                continue;
            }

            if (string.Equals(module.State, "offline", StringComparison.OrdinalIgnoreCase)) continue;
            var state = ParseState(module.State);
            result.Add(new SourceEntity(SourceKind.Item, module.TypeId, state, 1, i));

            if (module.ChargeTypeId is int chargeTypeId && chargeTypeId > 0)
            {
                result.Add(new SourceEntity(SourceKind.Charge, chargeTypeId, state, 1, i));
            }
        }

        foreach (var drone in fit.Drones)
        {
            if (drone.TypeId <= 0 || ResolveLaunchedDroneQuantity(drone) <= 0)
            {
                continue;
            }

            result.Add(new SourceEntity(SourceKind.Drone, drone.TypeId, EffectState.Active, 1, -1));
        }

        foreach (var implant in fit.Implants)
        {
            if (implant.TypeId <= 0)
            {
                continue;
            }

            result.Add(new SourceEntity(SourceKind.Implant, implant.TypeId, ParseState(implant.State), 1, implant.SlotIndex ?? -1));
        }

        foreach (var booster in fit.Boosters)
        {
            if (booster.TypeId <= 0)
            {
                continue;
            }

            var excludedSideEffectIds = booster.SideEffects
                .Where(sideEffect => sideEffect.EffectId > 0)
                .Select(sideEffect => sideEffect.EffectId)
                .ToHashSet();
            result.Add(new SourceEntity(
                SourceKind.Booster,
                booster.TypeId,
                ParseState(booster.State),
                1,
                booster.BoosterSlot ?? -1,
                excludedSideEffectIds));

            foreach (var sideEffect in booster.SideEffects.Where(sideEffect => sideEffect.Active && sideEffect.EffectId > 0))
            {
                result.Add(new SourceEntity(
                    SourceKind.Booster,
                    booster.TypeId,
                    ParseState(booster.State),
                    1,
                    booster.BoosterSlot ?? -1,
                    null,
                    sideEffect.EffectId));
            }
        }

        var skillLevels = new Dictionary<int, int>();
        CollectRequiredSkillTypeIds(fit.ShipTypeId, ctx, skillLevels);

        foreach (var module in fit.Modules)
        {
            if (module.TypeId > 0)
            {
                CollectRequiredSkillTypeIds(module.TypeId, ctx, skillLevels);
            }

            if (module.ChargeTypeId is int chargeTypeId && chargeTypeId > 0)
            {
                CollectRequiredSkillTypeIds(chargeTypeId, ctx, skillLevels);
            }
        }

        foreach (var drone in fit.Drones)
        {
            if (drone.TypeId > 0)
            {
                CollectRequiredSkillTypeIds(drone.TypeId, ctx, skillLevels);
            }
        }

        foreach (var implant in fit.Implants)
        {
            if (implant.TypeId > 0)
            {
                CollectRequiredSkillTypeIds(implant.TypeId, ctx, skillLevels);
            }
        }

        foreach (var booster in fit.Boosters)
        {
            if (booster.TypeId > 0)
            {
                CollectRequiredSkillTypeIds(booster.TypeId, ctx, skillLevels);
            }
        }

        foreach (var (skillTypeId, level) in skills)
        {
            if (skillTypeId <= 0 || level < 0)
            {
                continue;
            }

            skillLevels[skillTypeId] = Math.Clamp(level, 0, 5);
        }

        foreach (var (skillTypeId, level) in skillLevels.OrderBy(entry => entry.Key))
        {
            result.Add(new SourceEntity(SourceKind.Skill, skillTypeId, EffectState.Active, level, -1));
        }

        return result;
    }

    private static void CollectRequiredSkillTypeIds(
        int typeId,
        DogmaContext ctx,
        IDictionary<int, int> skillLevels)
    {
        if (typeId <= 0)
        {
            return;
        }

        foreach (var attributeId in RequiredSkillAttributeIds)
        {
            var requiredSkillTypeId = (int)Math.Round(ctx.GetTypeAttributeValue(typeId, attributeId, 0d));
            if (requiredSkillTypeId <= 0 || skillLevels.ContainsKey(requiredSkillTypeId))
            {
                continue;
            }

            skillLevels[requiredSkillTypeId] = 0;
        }
    }

    private static double ApplyEffects(double baseValue, List<PendingEffect> effects, bool highIsGood, bool stackable, List<FitModifierExecutionStep>? trace = null)
    {
        var current = baseValue;
        void Record(PendingEffect e, double before, double applied, double penalty = 1)
        {
            if (trace is not null) trace.Add((e.TraceSource ?? new FitModifierExecutionStep()) with
            { Order=trace.Count+1, Operation=e.Operator.ToString(), SourceValue=e.SourceValue,
              Before=before, After=current, AppliedValue=applied, PenaltyMultiplier=penalty });
        }
        var effectsByOperator = new Dictionary<EffectOperator, List<PendingEffect>>();
        foreach (var effect in effects)
        {
            if (!effectsByOperator.TryGetValue(effect.Operator, out var list))
            {
                list = new List<PendingEffect>();
                effectsByOperator[effect.Operator] = list;
            }

            list.Add(effect);
        }

        foreach (var op in OrderedOperators)
        {
            if (!effectsByOperator.TryGetValue(op, out var opEffects) || opEffects.Count == 0)
            {
                continue;
            }

            if (op == EffectOperator.PreAssign || op == EffectOperator.PostAssign)
            {
                var selectedEffect = opEffects[0];
                var selected = selectedEffect.SourceValue;
                var selectedAbs = Math.Abs(selected);
                for (var i = 1; i < opEffects.Count; i++)
                {
                    var v = opEffects[i].SourceValue;
                    var abs = Math.Abs(v);
                    if (highIsGood ? abs > selectedAbs : abs < selectedAbs)
                    {
                        selectedEffect = opEffects[i];
                        selected = v;
                        selectedAbs = abs;
                    }
                }
                var before = current;
                current = selected;
                Record(selectedEffect, before, selected);
                continue;
            }

            if (op == EffectOperator.ModAdd || op == EffectOperator.ModSub)
            {
                foreach (var e in opEffects)
                {
                    var before = current;
                    current += ToOpValue(op, e.SourceValue);
                    Record(e, before, ToOpValue(op, e.SourceValue));
                }
                continue;
            }

            var nonPenalty = new List<(double Value, PendingEffect Effect)>();
            var penaltyPositive = new List<(double Value, PendingEffect Effect)>();
            var penaltyNegative = new List<(double Value, PendingEffect Effect)>();

            foreach (var e in opEffects)
            {
                var value = ToOpValue(op, e.SourceValue);
                var penalty = !stackable && HasPenalty(op) && !ExemptPenaltyCategoryIds.Contains(e.SourceCategoryId);
                if (!penalty)
                {
                    nonPenalty.Add((value,e));
                }
                else if (value < 0)
                {
                    penaltyNegative.Add((value,e));
                }
                else
                {
                    penaltyPositive.Add((value,e));
                }
            }

            foreach (var value in nonPenalty)
            {
                var before = current;
                current *= 1d + value.Value;
                Record(value.Effect, before, 1d + value.Value);
            }

            penaltyPositive.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            penaltyNegative.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));

            for (var i = 0; i < penaltyPositive.Count; i++)
            {
                var before = current;
                var penalty = Math.Pow(PenaltyFactor, i * i);
                var factor = 1d + penaltyPositive[i].Value * penalty;
                current *= factor;
                Record(penaltyPositive[i].Effect, before, factor, penalty);
            }

            for (var i = 0; i < penaltyNegative.Count; i++)
            {
                var before = current;
                var penalty = Math.Pow(PenaltyFactor, i * i);
                var factor = 1d + penaltyNegative[i].Value * penalty;
                current *= factor;
                Record(penaltyNegative[i].Effect, before, factor, penalty);
            }
        }

        return current;
    }

    private static bool HasPenalty(EffectOperator op)
    {
        return op == EffectOperator.PreMul ||
               op == EffectOperator.PostMul ||
               op == EffectOperator.PostPercent ||
               op == EffectOperator.PreDiv ||
               op == EffectOperator.PostDiv;
    }

    private static bool IsSourceEffectEnabled(SourceEntity source, int effectId)
    {
        if (source.OnlyEffectId is int onlyEffectId && effectId != onlyEffectId)
        {
            return false;
        }

        return !source.ExcludedEffectIds.Contains(effectId);
    }

    private static double ToOpValue(EffectOperator op, double sourceValue)
    {
        return op switch
        {
            EffectOperator.PreAssign => sourceValue,
            EffectOperator.PreMul => sourceValue - 1d,
            EffectOperator.PreDiv => sourceValue == 0 ? 0 : (1d / sourceValue) - 1d,
            EffectOperator.ModAdd => sourceValue,
            EffectOperator.ModSub => -sourceValue,
            EffectOperator.PostMul => sourceValue - 1d,
            EffectOperator.PostDiv => sourceValue == 0 ? 0 : (1d / sourceValue) - 1d,
            EffectOperator.PostPercent => sourceValue / 100d,
            EffectOperator.PostAssign => sourceValue,
            _ => 0d
        };
    }

    private static readonly EffectOperator[] OrderedOperators =
    {
        EffectOperator.PreAssign,
        EffectOperator.PreMul,
        EffectOperator.PreDiv,
        EffectOperator.ModAdd,
        EffectOperator.ModSub,
        EffectOperator.PostMul,
        EffectOperator.PostDiv,
        EffectOperator.PostPercent,
        EffectOperator.PostAssign
    };

    private static EffectState ParseState(string? state)
    {
        if (string.Equals(state, "overload", StringComparison.OrdinalIgnoreCase)) return EffectState.Overload;
        if (string.Equals(state, "active", StringComparison.OrdinalIgnoreCase)) return EffectState.Active;
        if (string.Equals(state, "online", StringComparison.OrdinalIgnoreCase)) return EffectState.Online;
        return EffectState.Passive;
    }

    private static bool IsActive(string? state)
    {
        return string.Equals(state, "active", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "overload", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatModuleSlotGroup(string? slotGroup)
    {
        if (string.IsNullOrWhiteSpace(slotGroup))
        {
            return true;
        }

        return slotGroup.Trim().ToLowerInvariant() switch
        {
            "high" => true,
            "mid" => true,
            "low" => true,
            "rig" => true,
            "subsystem" => true,
            "service" => true,
            _ => false
        };
    }

    private static EffectState ToEffectState(int effectCategoryId)
    {
        return effectCategoryId switch
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
    }

    private static bool IsStateAllowed(EffectState sourceState, EffectState requiredState)
    {
        return sourceState >= requiredState;
    }

    private static FittingSummary BuildSummary(
        int shipTypeId,
        Dictionary<int, double> shipAttrs,
        IReadOnlyList<SourceEntity> sources,
        IReadOnlyDictionary<int, int> skills,
        ModulePassStats moduleStats,
        List<string> warnings,
        DogmaContext ctx,
        bool includeAttributeSnapshot)
    {
        var semantics = ctx.SemanticIds;
        var shieldHp = GetAttr(shipAttrs, ctx, "shieldCapacity");
        var armorHp = GetAttr(shipAttrs, ctx, "armorHP");
        var structureHp = GetAttr(shipAttrs, ctx, "hp");

        var shieldEmRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "shieldEmDamageResonance"), 1d);
        var shieldThermRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "shieldThermalDamageResonance"), 1d);
        var shieldKinRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "shieldKineticDamageResonance"), 1d);
        var shieldExpRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "shieldExplosiveDamageResonance"), 1d);

        var armorEmRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "armorEmDamageResonance"), 1d);
        var armorThermRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "armorThermalDamageResonance"), 1d);
        var armorKinRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "armorKineticDamageResonance"), 1d);
        var armorExpRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "armorExplosiveDamageResonance"), 1d);

        var hullEmRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "emDamageResonance"), 1d);
        var hullThermRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "thermalDamageResonance"), 1d);
        var hullKinRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "kineticDamageResonance"), 1d);
        var hullExpRes = GetResonanceOrDefault(GetAttr(shipAttrs, ctx, "explosiveDamageResonance"), 1d);

        var shieldEhp = ComputeLayerEhp(shieldHp, shieldEmRes, shieldThermRes, shieldKinRes, shieldExpRes);
        var armorEhp = ComputeLayerEhp(armorHp, armorEmRes, armorThermRes, armorKinRes, armorExpRes);
        var structureEhp = ComputeLayerEhp(structureHp, hullEmRes, hullThermRes, hullKinRes, hullExpRes);
        var totalEhp = shieldEhp + armorEhp + structureEhp;

        var maxVelocity = GetAttr(shipAttrs, ctx, "maxVelocity");
        var mass = GetAttr(shipAttrs, ctx, "mass");
        var capacitorCapacity = GetAttr(shipAttrs, ctx, "capacitorCapacity");
        var capacitorRechargeMs = GetAttr(shipAttrs, ctx, "rechargeRate");
        var shieldRechargeMs = GetAttr(shipAttrs, ctx, "shieldRechargeRate");
        var shipDps = GetAttr(shipAttrs, semantics.DamagePerSecondWithoutReload);
        var shipDpsWithReload = GetAttr(shipAttrs, semantics.DamagePerSecondWithReload);
        var shipVolley = GetAttr(shipAttrs, semantics.DamageAlpha);
        var shipDroneDps = GetAttr(shipAttrs, semantics.DroneDamagePerSecond);
        var shipShieldEhp = GetAttr(shipAttrs, semantics.ShieldEhp);
        var shipArmorEhp = GetAttr(shipAttrs, semantics.ArmorEhp);
        var shipHullEhp = GetAttr(shipAttrs, semantics.HullEhp);
        var shipTotalEhp = GetAttr(shipAttrs, semantics.Ehp);
        var hasShipDps = HasAttr(shipAttrs, semantics.DamagePerSecondWithoutReload);
        var hasShipDpsWithReload = HasAttr(shipAttrs, semantics.DamagePerSecondWithReload);
        var hasShipVolley = HasAttr(shipAttrs, semantics.DamageAlpha);
        var hasShipShieldEhp = HasAttr(shipAttrs, semantics.ShieldEhp);
        var hasShipArmorEhp = HasAttr(shipAttrs, semantics.ArmorEhp);
        var hasShipHullEhp = HasAttr(shipAttrs, semantics.HullEhp);
        var hasShipTotalEhp = HasAttr(shipAttrs, semantics.Ehp);
        var hasCapUsePerSecond = HasAttr(shipAttrs, semantics.CapacitorUsePerSecond);
        var hasCapPeakDelta = HasAttr(shipAttrs, semantics.CapacitorPeakDelta);
        var hasCapPeakDeltaPct = HasAttr(shipAttrs, semantics.CapacitorPeakDeltaPercentage);
        var hasCapDepletesIn = HasAttr(shipAttrs, semantics.CapacitorDepletesIn);
        var capacitorDepletesIn = GetAttr(shipAttrs, semantics.CapacitorDepletesIn);
        var capacitorPeakDelta = GetAttr(shipAttrs, semantics.CapacitorPeakDelta);
        var capacitorPeakDeltaPercentage = GetAttr(shipAttrs, semantics.CapacitorPeakDeltaPercentage);
        var capacitorUsePerSecondAttr = GetAttr(shipAttrs, semantics.CapacitorUsePerSecond);
        var passiveShieldRechargeRate = GetAttr(shipAttrs, semantics.PassiveShieldRechargeRate);
        var shieldBoostRate = GetAttr(shipAttrs, semantics.ShieldBoostRate);
        var armorRepairRate = GetAttr(shipAttrs, semantics.ArmorRepairRate);
        var hullRepairRate = GetAttr(shipAttrs, semantics.HullRepairRate);
        var maxTargetRange = GetAttr(shipAttrs, ctx, "maxTargetRange");
        var scanResolution = GetAttr(shipAttrs, ctx, "scanResolution");
        var maxLockedTargets = GetAttr(shipAttrs, ctx, "maxLockedTargets");
        var signatureRadius = GetAttr(shipAttrs, ctx, "signatureRadius");
        var warpSpeedMultiplier = GetAttr(shipAttrs, ctx, "warpSpeedMultiplier");
        var inertiaModifier = GetAttr(shipAttrs, ctx, "agility");
        var patchedAlignTime = GetAttr(shipAttrs, semantics.AlignTime);
        var cpuOutput = GetAttr(shipAttrs, ctx, "cpuOutput");
        var cpuLoad = GetAttr(shipAttrs, ctx, "cpuLoad");
        var powerOutput = GetAttr(shipAttrs, ctx, "powerOutput");
        var powerLoad = GetAttr(shipAttrs, ctx, "powerLoad");
        var calibration = GetAttr(shipAttrs, ctx, "upgradeCapacity");
        var calibrationLoad = GetAttr(shipAttrs, ctx, "upgradeLoad");
        var droneBandwidth = GetAttr(shipAttrs, ctx, "droneBandwidth");
        var droneCapacity = GetAttr(shipAttrs, ctx, "droneCapacity");
        var droneBandwidthLoad = GetAttr(shipAttrs, ctx, "droneBandwidthLoad");
        var droneCapacityLoad = GetAttr(shipAttrs, ctx, "droneCapacityLoad");
        if (cpuLoad <= 0)
        {
            cpuLoad = ComputeAggregatedItemAttribute("cpu", skills, ctx, sources);
        }

        if (powerLoad <= 0)
        {
            powerLoad = ComputeAggregatedItemAttribute("power", skills, ctx, sources);
        }

        if (calibrationLoad <= 0)
        {
            calibrationLoad = ComputeAggregatedItemAttribute("upgradeCost", skills, ctx, sources);
        }

        if (droneBandwidthLoad <= 0)
        {
            droneBandwidthLoad = (double)moduleStats.DroneBandwidthLoad;
        }
        else if (moduleStats.DroneBandwidthLoad > 0)
        {
            droneBandwidthLoad = Math.Max(droneBandwidthLoad, (double)moduleStats.DroneBandwidthLoad);
        }

        if (droneCapacityLoad <= 0)
        {
            droneCapacityLoad = (double)moduleStats.DroneCapacityLoad;
        }
        else if (moduleStats.DroneCapacityLoad > 0)
        {
            droneCapacityLoad = Math.Max(droneCapacityLoad, (double)moduleStats.DroneCapacityLoad);
        }

        var shipCategoryId = ctx.GetTypeCategoryId(shipTypeId);
        var isStructure = shipCategoryId == 65;

        var rechargeSeconds = capacitorRechargeMs / 1000d;
        var peakRegen = rechargeSeconds > 0 ? (2.5d * capacitorCapacity / rechargeSeconds) : 0;
        var hasPatchedCapModel =
            HasAttr(shipAttrs, semantics.CapacitorPeakDelta) ||
            HasAttr(shipAttrs, semantics.CapacitorPeakDeltaPercentage) ||
            HasAttr(shipAttrs, semantics.CapacitorDepletesIn);
        var moduleCapacitorUsePerSecond = (double)moduleStats.CapsuleCapUsePerSecond();
        var capacitorUsePerSecond = hasPatchedCapModel
            ? GetPositive(capacitorUsePerSecondAttr)
            : moduleCapacitorUsePerSecond;
        if (capacitorUsePerSecond <= 0 && moduleCapacitorUsePerSecond > 0)
        {
            capacitorUsePerSecond = moduleCapacitorUsePerSecond;
        }
        var effectiveCapacitorPeakDelta = capacitorPeakDelta;
        var effectiveCapacitorPeakDeltaPercentage = capacitorPeakDeltaPercentage;
        if (Math.Abs(effectiveCapacitorPeakDelta) < 0.000001d &&
            Math.Abs(effectiveCapacitorPeakDeltaPercentage) < 0.000001d &&
            peakRegen > 0 &&
            capacitorUsePerSecond > 0)
        {
            effectiveCapacitorPeakDelta = peakRegen - capacitorUsePerSecond;
            effectiveCapacitorPeakDeltaPercentage = effectiveCapacitorPeakDelta / peakRegen * 100d;
        }

        var netCapacitorDrain = capacitorUsePerSecond - peakRegen;
        if (capacitorDepletesIn <= 0 &&
            capacitorCapacity > 0 &&
            (effectiveCapacitorPeakDelta < 0 || netCapacitorDrain > 0))
        {
            capacitorDepletesIn = SimulateCapacitorDepletionSeconds(
                capacitorCapacity,
                capacitorRechargeMs,
                skills,
                ctx,
                sources);
        }

        if (passiveShieldRechargeRate <= 0 && shieldHp > 0 && shieldRechargeMs > 0)
        {
            passiveShieldRechargeRate = 2.5d * shieldHp / (shieldRechargeMs / 1000d);
        }

        var capStable = hasPatchedCapModel
            ? capacitorDepletesIn < 0 || effectiveCapacitorPeakDelta >= 0
            : (decimal)peakRegen >= (decimal)capacitorUsePerSecond;
        var alignTime = patchedAlignTime > 0
            ? patchedAlignTime
            : inertiaModifier > 0 && mass > 0
                ? inertiaModifier * mass / 500000d * Math.Log(2d)
                : 0d;
        // DPS should include whichever side is stronger (ship patched summary vs module/drone aggregation).
        var effectiveDps = Math.Max(shipDps, (double)moduleStats.Dps);
        var effectiveVolley = hasShipVolley
            ? shipVolley
            : ((double)moduleStats.Volley > 0 ? (double)moduleStats.Volley : shipVolley);
        var effectiveDpsWithReload = hasShipDpsWithReload
            ? Math.Max(shipDpsWithReload, (double)moduleStats.DpsWithReload)
            : ((double)moduleStats.DpsWithReload > 0
                ? (double)moduleStats.DpsWithReload
                : effectiveDps);
        // Guardrail: sustained DPS should not collapse far below no-reload DPS for normal turret/missile fits.
        // This avoids pathological magazine-size interpretation when source data is inconsistent.
        if (effectiveDps > 0 && effectiveDpsWithReload > 0 && effectiveDpsWithReload < effectiveDps * 0.8d)
        {
            effectiveDpsWithReload = effectiveDps;
        }
        var effectiveDroneDps = Math.Max(GetPositive(shipDroneDps), (double)moduleStats.DroneDps);
        var effectiveDpsProfile = ScaleDamageVectorToTotal(moduleStats.DpsProfile, effectiveDps);
        var sustainedProfileSource =
            effectiveDps > 0 && Math.Abs(effectiveDpsWithReload - effectiveDps) < 0.000001d
                ? moduleStats.DpsProfile
                : moduleStats.DpsWithReloadProfile;
        var effectiveDpsWithReloadProfile = ScaleDamageVectorToTotal(sustainedProfileSource, effectiveDpsWithReload);
        var effectiveDroneDpsProfile = ScaleDamageVectorToTotal(moduleStats.DroneDpsProfile, effectiveDroneDps);
        var effectiveTurretDpsProfile = moduleStats.TurretDpsProfile;
        var effectiveVolleyProfile = ScaleDamageVectorToTotal(moduleStats.VolleyProfile, effectiveVolley);
        var effectiveShieldEhp = hasShipShieldEhp ? shipShieldEhp : shieldEhp;
        var effectiveArmorEhp = hasShipArmorEhp ? shipArmorEhp : armorEhp;
        var effectiveStructureEhp = hasShipHullEhp ? shipHullEhp : structureEhp;
        var effectiveTotalEhp = hasShipTotalEhp
            ? shipTotalEhp
            : (hasShipShieldEhp || hasShipArmorEhp || hasShipHullEhp
                ? effectiveShieldEhp + effectiveArmorEhp + effectiveStructureEhp
                : totalEhp);
        if (isStructure)
        {
            // Align with EVEShipFit structure outputs:
            // structure offense is not surfaced through the fit summary, and structure capacitor
            // output does not use the ship-style "stable" presentation.
            // Structure capacitor output in EVEShipFit does not use the ship-style "stable" presentation.
            effectiveDps = 0d;
            effectiveDpsWithReload = 0d;
            effectiveVolley = 0d;
            effectiveDroneDps = 0d;
            effectiveDpsProfile = ZeroDamageVector();
            effectiveDpsWithReloadProfile = ZeroDamageVector();
            effectiveDroneDpsProfile = ZeroDamageVector();
            effectiveTurretDpsProfile = ZeroDamageVector();
            effectiveVolleyProfile = ZeroDamageVector();
            effectiveTotalEhp = 0d;
            if (!hasCapUsePerSecond)
            {
                capacitorUsePerSecond = 0d;
            }
            if (!hasCapPeakDelta)
            {
                effectiveCapacitorPeakDelta = 0d;
            }
            if (!hasCapPeakDeltaPct)
            {
                effectiveCapacitorPeakDeltaPercentage = 100d;
            }
            capStable = false;
            capacitorDepletesIn = 0d;
            if (!HasAttr(shipAttrs, semantics.PassiveShieldRechargeRate))
            {
                passiveShieldRechargeRate = GetAttrDefault(ctx, semantics.PassiveShieldRechargeRate, 2500d);
            }
            if (!HasAttr(shipAttrs, ctx, "cpuLoad") && cpuOutput > 0)
            {
                cpuLoad = cpuOutput;
            }
            if (!HasAttr(shipAttrs, ctx, "powerLoad") && powerOutput > 0)
            {
                powerLoad = powerOutput;
            }
            if (!HasAttr(shipAttrs, ctx, "warpSpeedMultiplier"))
            {
                warpSpeedMultiplier = 3d;
            }
            if (alignTime <= 0)
            {
                alignTime = Math.Log(4d);
            }

        }

        if (capacitorCapacity <= 0)
        {
            warnings.Add("Ship capacitorCapacity is missing in dogma data.");
        }

        var summary = new FittingSummary
        {
            Volley = decimal.Round((decimal)effectiveVolley, 3),
            Dps = decimal.Round((decimal)effectiveDps, 3),
            EffectiveHitPoints = decimal.Round((decimal)effectiveTotalEhp, 3),
            ShieldHitPoints = decimal.Round((decimal)shieldHp, 3),
            ArmorHitPoints = decimal.Round((decimal)armorHp, 3),
            StructureHitPoints = decimal.Round((decimal)structureHp, 3),
            MaxVelocity = decimal.Round((decimal)maxVelocity, 3),
            CapacitorCapacity = decimal.Round((decimal)capacitorCapacity, 3),
            CapacitorRechargeSeconds = decimal.Round((decimal)rechargeSeconds, 3),
            CapacitorUsePerSecond = decimal.Round((decimal)capacitorUsePerSecond, 3),
            CapacitorStable = capStable,
            WeaponOptimalRange = decimal.Round(moduleStats.WeaponOptimalRange, 3),
            WeaponTracking = decimal.Round(moduleStats.WeaponTracking, 6),
            Offense = new FittingOffenseSummary
            {
                Dps = decimal.Round((decimal)effectiveDps, 3),
                DpsWithReload = decimal.Round((decimal)effectiveDpsWithReload, 3),
                Alpha = decimal.Round((decimal)effectiveVolley, 3),
                DroneDps = decimal.Round((decimal)effectiveDroneDps, 3),
                TurretDamageMultiplier = decimal.Round(Math.Max(0m, moduleStats.TurretDamageMultiplier), 6),
                DpsProfile = ToDamageProfileSummary(effectiveDpsProfile),
                DpsWithReloadProfile = ToDamageProfileSummary(effectiveDpsWithReloadProfile),
                AlphaProfile = ToDamageProfileSummary(effectiveVolleyProfile),
                DroneDpsProfile = ToDamageProfileSummary(effectiveDroneDpsProfile),
                TurretDpsProfile = ToDamageProfileSummary(effectiveTurretDpsProfile)
            },
            Defense = new FittingDefenseSummary
            {
                EffectiveHitPoints = decimal.Round((decimal)effectiveTotalEhp, 3),
                ShieldHitPoints = decimal.Round((decimal)shieldHp, 3),
                ArmorHitPoints = decimal.Round((decimal)armorHp, 3),
                StructureHitPoints = decimal.Round((decimal)structureHp, 3),
                ShieldEmResistPct = decimal.Round((decimal)ToResistPct(shieldEmRes), 3),
                ShieldThermalResistPct = decimal.Round((decimal)ToResistPct(shieldThermRes), 3),
                ShieldKineticResistPct = decimal.Round((decimal)ToResistPct(shieldKinRes), 3),
                ShieldExplosiveResistPct = decimal.Round((decimal)ToResistPct(shieldExpRes), 3),
                ArmorEmResistPct = decimal.Round((decimal)ToResistPct(armorEmRes), 3),
                ArmorThermalResistPct = decimal.Round((decimal)ToResistPct(armorThermRes), 3),
                ArmorKineticResistPct = decimal.Round((decimal)ToResistPct(armorKinRes), 3),
                ArmorExplosiveResistPct = decimal.Round((decimal)ToResistPct(armorExpRes), 3),
                HullEmResistPct = decimal.Round((decimal)ToResistPct(hullEmRes), 3),
                HullThermalResistPct = decimal.Round((decimal)ToResistPct(hullThermRes), 3),
                HullKineticResistPct = decimal.Round((decimal)ToResistPct(hullKinRes), 3),
                HullExplosiveResistPct = decimal.Round((decimal)ToResistPct(hullExpRes), 3),
                PassiveShieldRechargeRate = decimal.Round((decimal)GetPositive(passiveShieldRechargeRate), 3),
                ShieldBoostRate = decimal.Round((decimal)GetPositive(shieldBoostRate), 3),
                ArmorRepairRate = decimal.Round((decimal)GetPositive(armorRepairRate), 3),
                HullRepairRate = decimal.Round((decimal)GetPositive(hullRepairRate), 3)
            },
            Capacitor = new FittingCapacitorSummary
            {
                Stable = capStable || capacitorDepletesIn < 0,
                DepletesInSeconds = decimal.Round((decimal)capacitorDepletesIn, 3),
                Capacity = decimal.Round((decimal)capacitorCapacity, 3),
                RechargeSeconds = decimal.Round((decimal)rechargeSeconds, 3),
                PeakDelta = decimal.Round((decimal)effectiveCapacitorPeakDelta, 3),
                PeakDeltaPercentage = decimal.Round((decimal)effectiveCapacitorPeakDeltaPercentage, 3),
                UsePerSecond = decimal.Round((decimal)capacitorUsePerSecond, 3),
                WeaponUsePerSecond = decimal.Round(moduleStats.WeaponCapacitorUsePerSecond, 3),
                ActiveTankUsePerSecond = decimal.Round(moduleStats.ActiveTankCapacitorUsePerSecond, 3)
            },
            Mobility = new FittingMobilitySummary
            {
                MaxVelocity = decimal.Round((decimal)maxVelocity, 3),
                WarpSpeedMultiplier = decimal.Round((decimal)GetPositive(warpSpeedMultiplier), 3),
                InertiaModifier = decimal.Round((decimal)GetPositive(inertiaModifier), 6),
                AlignTimeSeconds = decimal.Round((decimal)GetPositive(alignTime), 3)
            },
            Targeting = new FittingTargetingSummary
            {
                MaxTargetRange = decimal.Round((decimal)GetPositive(maxTargetRange), 3),
                ScanResolution = decimal.Round((decimal)GetPositive(scanResolution), 3),
                MaxLockedTargets = decimal.Round((decimal)GetPositive(maxLockedTargets), 3),
                SignatureRadius = decimal.Round((decimal)GetPositive(signatureRadius), 3)
            },
            Fitting = new FittingFittingSummary
            {
                CpuOutput = decimal.Round((decimal)GetPositive(cpuOutput), 3),
                CpuLoad = decimal.Round((decimal)GetPositive(cpuLoad), 3),
                PowerOutput = decimal.Round((decimal)GetPositive(powerOutput), 3),
                PowerLoad = decimal.Round((decimal)GetPositive(powerLoad), 3),
                Calibration = decimal.Round((decimal)GetPositive(calibration), 3),
                CalibrationLoad = decimal.Round((decimal)GetPositive(calibrationLoad), 3)
            },
            Drones = new FittingDroneSummary
            {
                Bandwidth = decimal.Round((decimal)GetPositive(droneBandwidth), 3),
                BandwidthLoad = decimal.Round((decimal)GetPositive(droneBandwidthLoad), 3),
                Capacity = decimal.Round((decimal)GetPositive(droneCapacity), 3),
                CapacityLoad = decimal.Round((decimal)GetPositive(droneCapacityLoad), 3)
            },
            AttributeSnapshot = includeAttributeSnapshot
                ? BuildAttributeSnapshot(shipAttrs, skills, ctx, sources)
                : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        };

        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "droneBandwidth", summary.Drones.Bandwidth);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "droneBandwidthLoad", summary.Drones.BandwidthLoad);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "droneCapacity", summary.Drones.Capacity);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "droneCapacityLoad", summary.Drones.CapacityLoad);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "damagePerSecondWithoutReload", summary.Offense.Dps);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "damagePerSecondWithReload", summary.Offense.DpsWithReload);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "damageAlpha", summary.Offense.Alpha);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "droneDamagePerSecond", summary.Offense.DroneDps);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "turretDamageMultiplier", summary.Offense.TurretDamageMultiplier);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "weaponCapacitorUsePerSecond", summary.Capacitor.WeaponUsePerSecond);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "activeTankCapacitorUsePerSecond", summary.Capacitor.ActiveTankUsePerSecond);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "passiveShieldRechargeRate", summary.Defense.PassiveShieldRechargeRate);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "weaponOptimalRange", summary.WeaponOptimalRange);
        ApplyComputedAttributeSnapshotValue(summary.AttributeSnapshot, "weaponTracking", summary.WeaponTracking);

        return summary;
    }

    private static double GetAttr(Dictionary<int, double> attrs, int attributeId)
    {
        if (attributeId != 0 && attrs.TryGetValue(attributeId, out var dynamicValue))
        {
            return dynamicValue;
        }

        return 0d;
    }

    private static double GetAttr(Dictionary<int, double> attrs, DogmaContext ctx, string name)
    {
        return GetAttr(attrs, ctx.TryGetAttributeId(name));
    }

    private static bool HasAttr(Dictionary<int, double> attrs, int attributeId)
    {
        return attributeId != 0 && attrs.ContainsKey(attributeId);
    }

    private static bool HasAttr(Dictionary<int, double> attrs, DogmaContext ctx, string name)
    {
        return HasAttr(attrs, ctx.TryGetAttributeId(name));
    }

    private static double GetAttrDefault(DogmaContext ctx, int attributeId, double fallback = 0d)
    {
        if (attributeId != 0)
        {
            var meta = ctx.GetAttribute(attributeId);
            if (meta is not null)
            {
                return meta.DefaultValue ?? fallback;
            }
        }

        return fallback;
    }

    private static double GetAttrDefault(DogmaContext ctx, string name, double fallback = 0d)
    {
        return GetAttrDefault(ctx, ctx.TryGetAttributeId(name), fallback);
    }

    private static double ComputeLayerEhp(double hp, params double[] resonances)
    {
        if (hp <= 0 || resonances.Length == 0)
        {
            return 0;
        }

        var avgResonance = resonances.Average(value => Clamp(value, 0.01, 1));
        return hp / avgResonance;
    }

    private static double SumDamageAttributes(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int typeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>? inProgress = null,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache = null,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache = null)
    {
        return GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "emDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)) +
               GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "thermalDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)) +
               GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "kineticDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)) +
               GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "explosiveDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache));
    }

    private static DamageVector SumDamageAttributesByType(
        TargetObjectKind targetKind,
        int targetRefIndex,
        int typeId,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double> effectCache,
        HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>? inProgress = null,
        Dictionary<int, List<TargetModifierCandidate>>? modifierCandidateCache = null,
        Dictionary<(int TargetTypeId, int SkillTypeId), bool>? requiredSkillCache = null)
    {
        return new DamageVector(
            Em: GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "emDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)),
            Thermal: GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "thermalDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)),
            Kinetic: GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "kineticDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)),
            Explosive: GetPositive(GetEffectiveTypeAttributeValue(targetKind, targetRefIndex, typeId, "explosiveDamage", skills, ctx, sources, effectCache, inProgress, modifierCandidateCache, requiredSkillCache)));
    }

    private static double GetResonanceOrDefault(double value, double fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Clamp(value, 0.01, 1);
    }

    private static double GetPositive(double value, double fallback = 0)
    {
        return value > 0 ? value : fallback;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static double ToResistPct(double resonance)
    {
        return (1d - Clamp(resonance, 0d, 1d)) * 100d;
    }

    private static DamageVector ZeroDamageVector() => new(0d, 0d, 0d, 0d);

    private static DamageVector AddDamageVectors(DamageVector left, DamageVector right) =>
        new(left.Em + right.Em, left.Thermal + right.Thermal, left.Kinetic + right.Kinetic, left.Explosive + right.Explosive);

    private static DamageVector SubtractDamageVectors(DamageVector left, DamageVector right) =>
        new(
            Math.Max(0d, left.Em - right.Em),
            Math.Max(0d, left.Thermal - right.Thermal),
            Math.Max(0d, left.Kinetic - right.Kinetic),
            Math.Max(0d, left.Explosive - right.Explosive));

    private static DamageVector ScaleDamageVector(DamageVector profile, double factor)
    {
        if (factor <= 0d)
        {
            return ZeroDamageVector();
        }

        return new DamageVector(
            profile.Em * factor,
            profile.Thermal * factor,
            profile.Kinetic * factor,
            profile.Explosive * factor);
    }

    private static DamageVector ScaleDamageVectorToTotal(DamageVector profile, double total)
    {
        var currentTotal = SumDamageVector(profile);
        if (total <= 0d)
        {
            return ZeroDamageVector();
        }

        if (currentTotal <= 0d)
        {
            var component = total / 4d;
            return new DamageVector(component, component, component, component);
        }

        return ScaleDamageVector(profile, total / currentTotal);
    }

    private static double SumDamageVector(DamageVector profile) =>
        GetPositive(profile.Em) + GetPositive(profile.Thermal) + GetPositive(profile.Kinetic) + GetPositive(profile.Explosive);

    private static FittingDamageProfileSummary ToDamageProfileSummary(DamageVector profile) =>
        new()
        {
            Em = decimal.Round((decimal)profile.Em, 6),
            Thermal = decimal.Round((decimal)profile.Thermal, 6),
            Kinetic = decimal.Round((decimal)profile.Kinetic, 6),
            Explosive = decimal.Round((decimal)profile.Explosive, 6)
        };

    private static FittingDamageProfileSummary ToProjectionProfile(DamageVector profile) =>
        ToDamageProfileSummary(profile);

    private static decimal RoundToDecimal(double value, int digits = 6)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0m;
        }

        return decimal.Round((decimal)value, digits);
    }

    private static Dictionary<string, decimal> BuildAttributeSnapshot(
        Dictionary<int, double> shipAttrs,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var names = new[]
        {
            "damagePerSecondWithoutReload", "damagePerSecondWithReload", "damageAlpha", "droneDamagePerSecond",
            "droneBandwidth", "droneBandwidthLoad", "droneCapacity", "droneCapacityLoad", "droneBandwidthUsed",
            "capacitorCapacity", "rechargeRate", "shieldRechargeRate", "capacitorUsePerSecond", "capacitorPeakDelta", "capacitorPeakDeltaPercentage", "capacitorDepletesIn",
            "mass", "maxVelocity", "warpSpeedMultiplier", "agility",
            "energyWarfareResistance", "capacity", "scanRadarStrength", "scanMagnetometricStrength", "scanGravimetricStrength", "scanLadarStrength",
            "droneControlDistance", "maxActiveDrones", "fighterCapacity", "fighterSquadronMaxActive",
            "fleetHangarCapacity", "shipMaintenanceBayCapacity", "specialFuelBayCapacity", "specialOreHoldCapacity",
            "generalMiningHoldCapacity", "specialIceHoldCapacity", "specialGasHoldCapacity", "specialAmmoHoldCapacity",
            "specialMineralHoldCapacity", "specialPlanetaryCommoditiesHoldCapacity", "specialCommandCenterHoldCapacity",
            "maxTargetRange", "scanResolution", "maxLockedTargets", "signatureRadius",
            "hiSlots", "medSlots", "lowSlots", "rigSlots", "launcherSlotsLeft", "turretSlotsLeft",
            "hiSlotModifier", "medSlotModifier", "lowSlotModifier", "launcherHardPointModifier", "turretHardPointModifier",
            "shieldCapacity", "armorHP", "hp",
            "shipProjectileDamageBonus", "shipVelocityBonus",
            "implantVelocityBonus", "boosterProjectileDamageBonus", "boosterVelocityPenalty",
            "shieldEmDamageResonance", "shieldThermalDamageResonance", "shieldKineticDamageResonance", "shieldExplosiveDamageResonance",
            "armorEmDamageResonance", "armorThermalDamageResonance", "armorKineticDamageResonance", "armorExplosiveDamageResonance",
            "emDamageResonance", "thermalDamageResonance", "kineticDamageResonance", "explosiveDamageResonance",
            "cpuOutput", "cpuLoad", "powerOutput", "powerLoad", "upgradeCapacity", "upgradeLoad",
            "passiveShieldRechargeRate", "shieldBoostRate", "armorRepairRate", "hullRepairRate"
        };

        var snapshot = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var value = GetAttr(shipAttrs, ctx, name);
            if (Math.Abs(value) < 0.0000001d)
            {
                continue;
            }

            snapshot[name] = decimal.Round((decimal)value, 6);
        }

        var characterCache = new TracedAttributeCache();
        var characterPending = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var characterModifiers = BuildTargetModifierIndex(ctx, sources);
        var characterRequirements = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        foreach (var name in new[] { "droneControlDistance", "maxActiveDrones" })
        {
            var value = GetEffectiveTypeAttributeValue(TargetObjectKind.Char, -1, CharacterTypeId, name, skills, ctx, sources,
                characterCache, characterPending, characterModifiers, characterRequirements);
            snapshot[name] = RoundToDecimal(value);
        }

        AppendAggregatedItemAttributes(
            snapshot,
            skills,
            ctx,
            sources,
            "hiSlotModifier",
            "medSlotModifier",
            "lowSlotModifier",
            "launcherHardPointModifier",
            "turretHardPointModifier");
        AppendAggregatedSourceAttributes(
            snapshot,
            ctx,
            sources,
            "implantVelocityBonus",
            "boosterProjectileDamageBonus",
            "boosterVelocityPenalty");
        AppendSyntheticObjectSnapshot(snapshot, "structure", TargetObjectKind.Structure, 0, names, skills, ctx, sources);
        AppendSyntheticObjectSnapshot(snapshot, "target", TargetObjectKind.Target, 0, names, skills, ctx, sources);

        return snapshot;
    }

    private static void ApplyComputedAttributeSnapshotValue(
        IDictionary<string, decimal> snapshot,
        string name,
        decimal value)
    {
        if (value == 0m)
        {
            snapshot.Remove(name);
            return;
        }

        snapshot[name] = value;
    }

    private static int ResolveLaunchedDroneQuantity(FitDroneEntry drone)
    {
        if (drone.State is not ("active" or "overload"))
        {
            return 0;
        }

        return Math.Max(0, drone.Quantity);
    }

    private static int ResolveDroneBayQuantity(FitDroneEntry drone)
    {
        return Math.Max(0, drone.BayQuantity ?? drone.Quantity);
    }

    private static void AppendAggregatedItemAttributes(
        Dictionary<string, decimal> snapshot,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        params string[] names)
    {
        if (names.Length == 0)
        {
            return;
        }

        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(ctx, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();

        foreach (var name in names)
        {
            var attributeId = ctx.TryGetAttributeId(name);
            if (attributeId == 0)
            {
                continue;
            }

            decimal total = 0m;
            foreach (var source in sources)
            {
                if (source.Kind != SourceKind.Item)
                {
                    continue;
                }

                var value = GetEffectiveTypeAttributeValueById(
                    TargetObjectKind.Item,
                    source.RefIndex,
                    source.TypeId,
                    attributeId,
                    skills,
                    ctx,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache);
                if (Math.Abs(value) < 0.0000001d)
                {
                    continue;
                }

                total += decimal.Round((decimal)value, 6);
            }

            if (total == 0m)
            {
                continue;
            }

            snapshot[name] = snapshot.TryGetValue(name, out var existing)
                ? decimal.Round(existing + total, 6)
                : decimal.Round(total, 6);
        }
    }

    private static void AppendAggregatedSourceAttributes(
        Dictionary<string, decimal> snapshot,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources,
        params string[] names)
    {
        foreach (var name in names)
        {
            var attributeId = ctx.TryGetAttributeId(name);
            if (attributeId == 0)
            {
                continue;
            }

            decimal total = 0m;
            foreach (var source in sources)
            {
                if (source.TypeId <= 0)
                {
                    continue;
                }

                if (!SourceCanExposeAttribute(source, attributeId, ctx))
                {
                    continue;
                }

                var value = GetTypeAttributeValue(source.TypeId, attributeId, ctx);
                if (Math.Abs(value) < 0.0000001d)
                {
                    continue;
                }

                total += decimal.Round((decimal)value, 6);
            }

            if (total == 0m)
            {
                continue;
            }

            snapshot[name] = snapshot.TryGetValue(name, out var existing)
                ? decimal.Round(existing + total, 6)
                : decimal.Round(total, 6);
        }
    }

    private static bool SourceCanExposeAttribute(SourceEntity source, int attributeId, DogmaContext ctx)
    {
        var typeDogma = ctx.GetTypeDogma(source.TypeId);
        if (typeDogma?.DogmaEffects is null)
        {
            return false;
        }

        foreach (var typeEffect in typeDogma.DogmaEffects)
        {
            if (!IsSourceEffectEnabled(source, typeEffect.EffectId))
            {
                continue;
            }

            var effect = ctx.GetEffect(typeEffect.EffectId);
            if (effect?.ModifierInfo is null)
            {
                continue;
            }

            if (effect.ModifierInfo.Any(modifier => modifier.ModifyingAttributeId == attributeId))
            {
                return true;
            }
        }

        return false;
    }

    private static double ComputeAggregatedItemAttribute(
        string attributeName,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var attributeId = ctx.TryGetAttributeId(attributeName);
        if (attributeId == 0)
        {
            return 0d;
        }

        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(ctx, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        var total = 0d;

        foreach (var source in sources)
        {
            if (source.Kind != SourceKind.Item || source.RefIndex < 0 || source.TypeId <= 0)
            {
                continue;
            }

            total += GetPositive(GetEffectiveTypeAttributeValueById(
                TargetObjectKind.Item,
                source.RefIndex,
                source.TypeId,
                attributeId,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache));
        }

        return total;
    }

    private static double SimulateCapacitorDepletionSeconds(
        double capacitorCapacity,
        double rechargeRateMs,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var semantics = ctx.SemanticIds;
        if (capacitorCapacity <= 0 || rechargeRateMs <= 0)
        {
            return 0d;
        }

        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(ctx, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();
        var modules = new List<CapacitorSimulationModule>();

        foreach (var source in sources)
        {
            if (source.Kind != SourceKind.Item ||
                source.RefIndex < 0 ||
                source.TypeId <= 0 ||
                (source.State != EffectState.Active && source.State != EffectState.Overload))
            {
                continue;
            }

            var capacitorNeed = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item,
                source.RefIndex,
                source.TypeId,
                "capacitorNeed",
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache));
            if (capacitorNeed <= 0)
            {
                continue;
            }

            var durationMs = GetPositive(GetEffectiveTypeAttributeValue(
                TargetObjectKind.Item,
                source.RefIndex,
                source.TypeId,
                semantics.CycleTime,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache));
            if (durationMs <= 0)
            {
                durationMs = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item,
                    source.RefIndex,
                    source.TypeId,
                    "speed",
                    skills,
                    ctx,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache));
            }
            if (durationMs <= 0)
            {
                durationMs = GetPositive(GetEffectiveTypeAttributeValue(
                    TargetObjectKind.Item,
                    source.RefIndex,
                    source.TypeId,
                    "duration",
                    skills,
                    ctx,
                    sources,
                    effectCache,
                    inProgress,
                    modifierCandidateCache,
                    requiredSkillCache));
            }

            if (durationMs <= 0)
            {
                continue;
            }

            modules.Add(new CapacitorSimulationModule(capacitorNeed, durationMs, 0d));
        }

        if (modules.Count == 0)
        {
            return 0d;
        }

        var capacitor = capacitorCapacity;
        var timeLast = 0d;
        var timeNext = 0d;
        const double maxSimulationMs = 7d * 24d * 60d * 60d * 1000d;

        while (capacitor > 0d && timeLast <= maxSimulationMs)
        {
            capacitor = Math.Pow(
                1d + (Math.Sqrt(Math.Max(0d, capacitor) / capacitorCapacity) - 1d) * Math.Exp(5d * (timeLast - timeNext) / rechargeRateMs),
                2d) * capacitorCapacity;

            timeLast = timeNext;
            timeNext = double.PositiveInfinity;

            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module.TimeNextMs <= timeLast + 0.000001d)
                {
                    module = module with { TimeNextMs = module.TimeNextMs + module.DurationMs };
                    capacitor -= module.CapacitorNeed;
                    modules[i] = module;
                }

                if (module.TimeNextMs < timeNext)
                {
                    timeNext = module.TimeNextMs;
                }
            }

            if (double.IsInfinity(timeNext))
            {
                break;
            }
        }

        return capacitor <= 0d ? timeLast / 1000d : 0d;
    }

    private static void AppendSyntheticObjectSnapshot(
        Dictionary<string, decimal> snapshot,
        string prefix,
        TargetObjectKind targetKind,
        int targetTypeId,
        IReadOnlyList<string> names,
        IReadOnlyDictionary<int, int> skills,
        DogmaContext ctx,
        IReadOnlyList<SourceEntity> sources)
    {
        var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();
        var inProgress = new HashSet<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId)>();
        var modifierCandidateCache = BuildTargetModifierIndex(ctx, sources);
        var requiredSkillCache = new Dictionary<(int TargetTypeId, int SkillTypeId), bool>();

        foreach (var name in names)
        {
            var value = GetEffectiveTypeAttributeValue(
                targetKind,
                0,
                targetTypeId,
                name,
                skills,
                ctx,
                sources,
                effectCache,
                inProgress,
                modifierCandidateCache,
                requiredSkillCache);
            if (Math.Abs(value) < 0.0000001d)
            {
                continue;
            }

            snapshot[$"{prefix}.{name}"] = decimal.Round((decimal)value, 6);
        }
    }

    private sealed record Pass1State(
        int ShipTypeId,
        Dictionary<int, double> ShipAttributes,
        IReadOnlyList<SourceEntity> Sources);

    private sealed record Pass2State(
        int ShipTypeId,
        Dictionary<int, double> ShipAttributes,
        IReadOnlyList<SourceEntity> Sources);

    private sealed record Pass3State(
        int ShipTypeId,
        Dictionary<int, double> ShipAttributes,
        IReadOnlyList<SourceEntity> Sources,
        IReadOnlyDictionary<int, int> Skills,
        ModulePassStats ModuleStats);

    private enum SourceKind
    {
        Ship,
        Char,
        Structure,
        Target,
        Item,
        Charge,
        Drone,
        Implant,
        Booster,
        Skill
    }

    private enum TargetObjectKind
    {
        Ship,
        Char,
        Structure,
        Target,
        Item,
        Charge,
        Drone,
        Implant,
        Booster
    }

    private enum EffectState
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

    private enum EffectOperator
    {
        PreAssign,
        PreMul,
        PreDiv,
        ModAdd,
        ModSub,
        PostMul,
        PostDiv,
        PostPercent,
        PostAssign
    }

    private static EffectOperator? ToOperator(int operation)
    {
        return operation switch
        {
            -1 => EffectOperator.PreAssign,
            0 => EffectOperator.PreMul,
            1 => EffectOperator.PreDiv,
            2 => EffectOperator.ModAdd,
            3 => EffectOperator.ModSub,
            4 => EffectOperator.PostMul,
            5 => EffectOperator.PostDiv,
            6 => EffectOperator.PostPercent,
            7 => EffectOperator.PostAssign,
            9 => null,
            _ => null
        };
    }

    private sealed record SourceEntity
    {
        public SourceEntity(
            SourceKind kind,
            int typeId,
            EffectState state,
            int skillLevel,
            int refIndex,
            IReadOnlySet<int>? excludedEffectIds = null,
            int? onlyEffectId = null)
        {
            Kind = kind;
            TypeId = typeId;
            State = state;
            SkillLevel = skillLevel;
            RefIndex = refIndex;
            ExcludedEffectIds = excludedEffectIds ?? EmptyEffectIds;
            OnlyEffectId = onlyEffectId;
        }

        public SourceKind Kind { get; }

        public int TypeId { get; }

        public EffectState State { get; }

        public int SkillLevel { get; }

        public int RefIndex { get; }

        public IReadOnlySet<int> ExcludedEffectIds { get; }

        public int? OnlyEffectId { get; }
    }

    private static readonly IReadOnlySet<int> EmptyEffectIds = new HashSet<int>();
    private sealed record TargetModifierCandidate(
        SourceEntity Source,
        string Func,
        string? Domain,
        int GroupId,
        int SkillTypeId,
        int ModifyingAttributeId,
        EffectOperator Operator,
        EffectState RequiredState,
        int SourceCategoryId, int EffectId = 0);

    private sealed class TracedAttributeCache : Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>
    {
        public Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), FitAttributeExecutionTrace> Traces { get; } = new();
    }
    private static FitModifierExecutionStep CaptureModifierSource(TargetModifierCandidate candidate, DogmaContext ctx) => new()
    {
        SourceTypeId=candidate.Source.TypeId, SourceKind=candidate.Source.Kind.ToString(), SourceIndex=candidate.Source.RefIndex,
        SourceState=candidate.Source.State.ToString(), SkillLevel=candidate.Source.SkillLevel,
        EffectId=candidate.EffectId, ModifyingAttributeId=candidate.ModifyingAttributeId,
        SourceBaseValue=GetTypeAttributeValue(candidate.Source.TypeId,candidate.ModifyingAttributeId,ctx)
    };
    private sealed record PendingEffect(EffectOperator Operator, double SourceValue, int SourceCategoryId, FitModifierExecutionStep? TraceSource = null);

    private sealed record ModulePassStats(
        decimal Volley,
        decimal Dps,
        decimal DpsWithReload,
        decimal DroneDps,
        decimal DroneBandwidthLoad,
        decimal DroneCapacityLoad,
        decimal CapacitorUsePerSecond,
        decimal WeaponCapacitorUsePerSecond,
        decimal ActiveTankCapacitorUsePerSecond,
        decimal WeaponOptimalRange,
        decimal WeaponTracking,
        decimal TurretDamageMultiplier,
        DamageVector VolleyProfile,
        DamageVector DpsProfile,
        DamageVector DpsWithReloadProfile,
        DamageVector DroneDpsProfile,
        DamageVector TurretDpsProfile,
        IReadOnlyDictionary<string, decimal> Breakdown)
    {
        public decimal CapsuleCapUsePerSecond() => CapacitorUsePerSecond;
    }

    private sealed record CapacitorSimulationModule(
        double CapacitorNeed,
        double DurationMs,
        double TimeNextMs);

    private sealed record ModuleAmmunitionState(
        double ReloadTimeMs,
        double ShotsPerMagazine,
        int MagazineCapacity,
        int ChargeUnitsPerCycle)
    {
        public static ModuleAmmunitionState Empty { get; } = new(0d, 0d, 0, 1);
    }

    private readonly record struct DamageVector(
        double Em,
        double Thermal,
        double Kinetic,
        double Explosive);
}

public sealed record FittingCalculationOutput(
    FittingSummary Summary,
    IReadOnlyDictionary<string, decimal> Breakdown,
    IReadOnlyList<string> Warnings);

public sealed record FittingAttributeModifierTrace(
    string SourceKind,
    int SourceTypeId,
    int SourceRefIndex,
    string SourceState,
    string Func,
    string Domain,
    int GroupId,
    int SkillTypeId,
    int ModifyingAttributeId,
    string ModifyingAttributeName,
    string Operator,
    string RequiredState,
    int SourceCategoryId,
    bool Applies,
    string? SkipReason,
    double? SourceValue,
    double? OperatorValue);

internal sealed record FittingModuleCombatProjection
{
    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> ChargeAttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();
    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();
    public decimal CpuUsage { get; init; }
    public decimal PowergridUsage { get; init; }
    public int ModuleIndex { get; init; }
    public WeaponApplicationKind ApplicationKind { get; init; } = WeaponApplicationKind.Direct;
    public decimal DamagePerSecond { get; init; }
    public FittingDamageProfileSummary DamageProfilePerSecond { get; init; } = new();
    public decimal VolleyDamage { get; init; }
    public FittingDamageProfileSummary VolleyDamageProfile { get; init; } = new();
    public decimal ChargeDamagePerSecond { get; init; }
    public FittingDamageProfileSummary ChargeDamageProfilePerSecond { get; init; } = new();
    public decimal ChargeVolleyDamage { get; init; }
    public FittingDamageProfileSummary ChargeVolleyDamageProfile { get; init; } = new();
    public decimal CycleTimeSeconds { get; init; }
    public decimal ReloadTimeSeconds { get; init; }
    public int MagazineCapacity { get; init; }
    public int ChargeUnitsPerCycle { get; init; } = 1;
    public decimal ShieldRepairPerSecond { get; init; }
    public decimal ArmorRepairPerSecond { get; init; }
    public decimal StructureRepairPerSecond { get; init; }
    public decimal CapacitorTransferPerSecond { get; init; }
    public decimal CapacitorUsagePerSecond { get; init; }
    public decimal OptimalRangeMeters { get; init; }
    public decimal FalloffRangeMeters { get; init; }
    public decimal TrackingSpeed { get; init; }
    public decimal SignatureResolutionMeters { get; init; }
    public decimal MissileExplosionRadiusMeters { get; init; }
    public decimal MissileExplosionVelocityMetersPerSecond { get; init; }
    public decimal MissileDamageReductionFactor { get; init; }
    public decimal MissileDamageReductionSensitivity { get; init; }
}
