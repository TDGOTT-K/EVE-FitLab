using System.Globalization;
using EdenOS.Application.Market;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal sealed class IndustrialRecipeResolver(MetadataBootstrapCatalog metadata)
{
    private static readonly string[] FactionKeywords = ["amarr", "caldari", "gallente", "minmatar", "ore", "guristas", "sansha", "angel", "blood", "serpentis"];
    private static readonly string[] HullKeywords = ["frigate", "destroyer", "cruiser", "battlecruiser", "battleship", "industrial", "hauler", "freighter", "carrier", "dreadnought", "force auxiliary", "fax", "titan", "supercarrier", "marauder"];

    public RecipeActivityResolution? ResolveRecipeActivity(PlanNode node)
    {
        return node.Details switch
        {
            ProductionNodeDetails production => ResolveProductionRecipe(node, production),
            ReactionNodeDetails reaction => ResolveReactionRecipe(node, reaction),
            CopyOrInventionNodeDetails copyOrInvention => ResolveCopyOrInventionRecipe(copyOrInvention),
            _ => null
        };
    }

    public string ResolveTypeName(long typeId)
    {
        return metadata.TryGetType(typeId, out var type)
            ? type.Name
            : typeId.ToString(CultureInfo.InvariantCulture);
    }

    public StaticIndustryRequirementProfile ResolveStaticRequirements(PlanNode node)
    {
        var recipe = ResolveRecipeActivity(node);
        if (recipe is null)
        {
            return new StaticIndustryRequirementProfile(
                Array.Empty<CharacterIndustrySkillLevel>(),
                InferSpecialtyTagsWithoutRecipe(node),
                InferFacilityTags(node, productTypeId: null));
        }

        var requiredSkills = metadata.GetBlueprintSkillRequirements(recipe.BlueprintTypeId, recipe.ActivityKind)
            .Select(requirement => new CharacterIndustrySkillLevel
            {
                SkillKey = NormalizeSkillKey(requirement.SkillName),
                Level = requirement.RequiredLevel
            })
            .GroupBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CharacterIndustrySkillLevel
            {
                SkillKey = group.Key,
                Level = group.Max(item => item.Level)
            })
            .OrderBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var specialtyTags = InferSpecialtyTags(node, recipe);
        var facilityTags = InferFacilityTags(node, recipe.ProductTypeId);

        return new StaticIndustryRequirementProfile(requiredSkills, specialtyTags, facilityTags);
    }

    public bool HasStaticSkillRequirementData => metadata.HasBlueprintSkillRequirements;

    public IReadOnlyList<string> ResolveSpecialtyPreferenceTags(PlanNode node)
    {
        var recipe = ResolveRecipeActivity(node);
        return recipe is null
            ? InferSpecialtyTagsWithoutRecipe(node)
            : InferSpecialtyTags(node, recipe);
    }

    public decimal ApplyMaterialEfficiency(IndustryPlanComputationContext context, PlanNode node, decimal baseQuantity)
    {
        return ApplyMaterialEfficiency(context, node, baseQuantity, 1m);
    }

    public decimal ApplyMaterialEfficiency(IndustryPlanComputationContext context, PlanNode node, decimal baseQuantityPerRun, decimal plannedRuns)
    {
        if (baseQuantityPerRun <= 0m || plannedRuns <= 0m)
        {
            return 0m;
        }

        var efficiencyPercent = ResolveEffectiveEfficiencyPercent(context, node, isMaterial: true);
        var adjustedQuantityPerRun = Math.Ceiling(Math.Max(0m, baseQuantityPerRun * (1m - (efficiencyPercent / 100m))));
        return adjustedQuantityPerRun * plannedRuns;
    }

    public decimal ApplyTimeEfficiency(IndustryPlanComputationContext context, PlanNode node, decimal baseSeconds)
    {
        var blueprintEfficiencyPercent = ResolveEffectiveEfficiencyPercent(context, node, isMaterial: false);
        var characterEfficiencyPercent = IndustryPlanCharacterConstraintAnalyzer.ResolveTimeEfficiencyPercent(context, node);
        if (characterEfficiencyPercent != 0m)
        {
            context.CharacterAdjustedTimeNodeIds.Add(node.NodeId);
        }

        var efficiencyPercent = Math.Clamp(blueprintEfficiencyPercent + characterEfficiencyPercent, -100m, 99m);
        return Math.Max(0m, baseSeconds * (1m - (efficiencyPercent / 100m)));
    }

    public decimal ResolvePlannedRuns(IndustryPlanComputationContext context, PlanNode node, RecipeActivityResolution recipe)
    {
        if (node.Details is CopyOrInventionNodeDetails copyOrInvention && copyOrInvention.Activity == CopyOrInventionActivity.Copy)
        {
            var copyRuns = Math.Max(1m, copyOrInvention.CopyRuns ?? 1);
            if (recipe.MaxProductionLimit.HasValue && copyRuns > recipe.MaxProductionLimit.Value)
            {
                context.AddHardConflict(
                    $"Copy node '{node.Title}' requests {copyRuns:N0} runs, but blueprint '{recipe.BlueprintTypeId}' caps copies at {recipe.MaxProductionLimit.Value:N0} runs.",
                    node.NodeId);
            }

            return copyRuns;
        }

        var unitsPerRun = ResolveEffectiveProductQuantityPerRun(context, node, recipe);
        var plannedOutputQuantity = ResolvePlannedOutputQuantity(context, node, recipe);
        var expectedAttempts = Math.Ceiling(plannedOutputQuantity / unitsPerRun);

        if (recipe.ProductProbability.HasValue &&
            recipe.ProductProbability.Value > 0m &&
            recipe.ProductProbability.Value < 1m)
        {
            var effectiveProbability = ResolveEffectiveProductProbability(context, node, recipe.ProductProbability.Value);
            expectedAttempts = Math.Ceiling(expectedAttempts / effectiveProbability);
            context.ProbabilityAdjustedNodeIds.Add(node.NodeId);
        }

        return Math.Max(1m, expectedAttempts);
    }

    private RecipeActivityResolution? ResolveProductionRecipe(PlanNode node, ProductionNodeDetails production)
    {
        if (PlanningMetadataReader.TryReadLong(node.Metadata, "blueprint_type_id", out var blueprintTypeId))
        {
            return ResolveRecipeByBlueprint(blueprintTypeId, "manufacturing")
                ?? ResolveRecipeByBlueprint(blueprintTypeId, null);
        }

        if (!PlanningMetadataReader.TryParseTypeId(production.RecipeTypeId, out var productTypeId))
        {
            return null;
        }

        var preferredActivityKind = production.Activity switch
        {
            ProductionActivityKind.Manufacturing or ProductionActivityKind.Generic => "manufacturing",
            _ => null
        };

        return ResolveRecipeByProduct(productTypeId, preferredActivityKind)
            ?? ResolveRecipeByProduct(productTypeId, null);
    }

    private RecipeActivityResolution? ResolveReactionRecipe(PlanNode node, ReactionNodeDetails reaction)
    {
        if (PlanningMetadataReader.TryReadLong(node.Metadata, "blueprint_type_id", out var blueprintTypeId))
        {
            return ResolveRecipeByBlueprint(blueprintTypeId, "reaction")
                ?? ResolveRecipeByBlueprint(blueprintTypeId, null);
        }

        if (!PlanningMetadataReader.TryParseTypeId(reaction.ReactionTypeId, out var productTypeId))
        {
            return null;
        }

        return ResolveRecipeByProduct(productTypeId, "reaction")
            ?? ResolveRecipeByProduct(productTypeId, null);
    }

    private RecipeActivityResolution? ResolveCopyOrInventionRecipe(CopyOrInventionNodeDetails copyOrInvention)
    {
        if (!PlanningMetadataReader.TryParseTypeId(copyOrInvention.BlueprintTypeId, out var blueprintTypeId))
        {
            return null;
        }

        var activityKind = copyOrInvention.Activity == CopyOrInventionActivity.Invention
            ? "invention"
            : "copying";

        var activity = metadata.GetBlueprintActivities(blueprintTypeId)
            .FirstOrDefault(entry => string.Equals(entry.ActivityKind, activityKind, StringComparison.Ordinal));

        return activity is null
            ? null
            : BuildRecipeActivity(activity);
    }

    private RecipeActivityResolution? ResolveRecipeByBlueprint(long blueprintTypeId, string? preferredActivityKind)
    {
        var activities = metadata.GetBlueprintActivities(blueprintTypeId);
        if (activities.Count == 0)
        {
            return null;
        }

        MetadataBootstrapCatalog.MetadataBlueprintActivityEntry? activity = null;
        if (!string.IsNullOrWhiteSpace(preferredActivityKind))
        {
            activity = activities.FirstOrDefault(entry => string.Equals(entry.ActivityKind, preferredActivityKind, StringComparison.Ordinal));
        }

        activity ??= activities.FirstOrDefault();
        return activity is null
            ? null
            : BuildRecipeActivity(activity);
    }

    private RecipeActivityResolution? ResolveRecipeByProduct(long productTypeId, string? preferredActivityKind)
    {
        var activities = metadata.GetBlueprintActivitiesForProduct(productTypeId);
        if (activities.Count == 0)
        {
            return null;
        }

        MetadataBootstrapCatalog.MetadataBlueprintActivityEntry? activity = null;
        if (!string.IsNullOrWhiteSpace(preferredActivityKind))
        {
            activity = activities.FirstOrDefault(entry => string.Equals(entry.ActivityKind, preferredActivityKind, StringComparison.Ordinal));
        }

        activity ??= activities.FirstOrDefault();
        return activity is null
            ? null
            : BuildRecipeActivity(activity);
    }

    private RecipeActivityResolution BuildRecipeActivity(MetadataBootstrapCatalog.MetadataBlueprintActivityEntry activity)
    {
        var materials = metadata.GetBlueprintMaterials(activity.BlueprintTypeId, activity.ActivityKind)
            .Select(entry => new RecipeMaterialRequirement(entry.MaterialTypeId, entry.Quantity))
            .ToArray();

        return new RecipeActivityResolution(
            activity.BlueprintTypeId,
            activity.ActivityKind,
            activity.PlannerSemanticClass,
            activity.ProductTypeId,
            activity.ProductQuantity,
            activity.ProductProbability,
            activity.TimeSeconds,
            activity.MaxProductionLimit,
            materials);
    }

    private decimal ResolvePlannedOutputQuantity(IndustryPlanComputationContext context, PlanNode node, RecipeActivityResolution recipe)
    {
        if (recipe.ProductTypeId.HasValue)
        {
            var productTypeIdText = recipe.ProductTypeId.Value.ToString(CultureInfo.InvariantCulture);
            var outgoingQuantity = context.Plan.Links
                .Where(link => link.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase))
                .Where(link => string.Equals(link.ResourceTypeId, productTypeIdText, StringComparison.Ordinal))
                .Where(link => link.Quantity.HasValue && link.Quantity.Value > 0m)
                .Sum(link => link.Quantity!.Value);

            if (outgoingQuantity > 0m)
            {
                return outgoingQuantity;
            }

            if (context.Plan.Goal is not null &&
                string.Equals(context.Plan.Goal.TargetTypeId, productTypeIdText, StringComparison.Ordinal) &&
                context.Plan.Goal.Quantity > 0m)
            {
                return context.Plan.Goal.Quantity;
            }
        }

        return PlanningMetadataReader.ReadDecimal(node.Metadata, "planned_output_quantity")
            ?? PlanningMetadataReader.ReadDecimal(node.Metadata, "output_quantity")
            ?? Math.Max(1m, recipe.ProductQuantity ?? 1);
    }

    private decimal ResolveEffectiveProductQuantityPerRun(
        IndustryPlanComputationContext context,
        PlanNode node,
        RecipeActivityResolution recipe)
    {
        var baseQuantity = Math.Max(1m, recipe.ProductQuantity ?? 1);
        var yieldPercent = IndustryPlanCharacterConstraintAnalyzer.ResolveReprocessingYieldPercent(context, node);
        if (yieldPercent == 0m)
        {
            return baseQuantity;
        }

        context.CharacterAdjustedYieldNodeIds.Add(node.NodeId);
        return Math.Max(1m, baseQuantity * (1m + (yieldPercent / 100m)));
    }

    private decimal ResolveEffectiveProductProbability(
        IndustryPlanComputationContext context,
        PlanNode node,
        decimal baseProbability)
    {
        var bonusPercent = IndustryPlanCharacterConstraintAnalyzer.ResolveInventionSuccessBonusPercent(context, node);
        if (bonusPercent == 0m)
        {
            return baseProbability;
        }

        context.CharacterAdjustedProbabilityNodeIds.Add(node.NodeId);
        return Math.Clamp(baseProbability * (1m + (bonusPercent / 100m)), 0.0001m, 1m);
    }

    private decimal ResolveEffectiveEfficiencyPercent(IndustryPlanComputationContext context, PlanNode node, bool isMaterial)
    {
        var explicitKeys = isMaterial
            ? new[] { "material_efficiency_pct", "material_efficiency" }
            : new[] { "time_efficiency_pct", "time_efficiency" };

        if (TryResolveExplicitEfficiencyPercent(node, explicitKeys, out var explicitValue))
        {
            return explicitValue;
        }

        if (TryResolveInheritedBlueprintQuality(context, node, out var source))
        {
            context.InheritedBlueprintQualityNodeIds.Add(node.NodeId);
            var inheritedValue = isMaterial ? source.MaterialEfficiencyPercent : source.TimeEfficiencyPercent;
            return inheritedValue ?? 0m;
        }

        return 0m;
    }

    private static bool TryResolveExplicitEfficiencyPercent(PlanNode node, string[] keys, out decimal value)
    {
        foreach (var key in keys)
        {
            var candidate = PlanningMetadataReader.ReadDecimal(node.Metadata, key);
            if (candidate.HasValue)
            {
                value = Math.Clamp(candidate.Value, -100m, 100m);
                return true;
            }
        }

        value = 0m;
        return false;
    }

    private bool TryResolveInheritedBlueprintQuality(
        IndustryPlanComputationContext context,
        PlanNode node,
        out BlueprintQualitySource source)
    {
        source = default!;
        var targetBlueprintTypeId = ResolveBlueprintTypeIdForExecutionNode(node);
        if (!targetBlueprintTypeId.HasValue)
        {
            return false;
        }

        var candidates = context.Plan.Links
            .Where(link => link.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase))
            .Where(link => link.Kind is PlanLinkKind.Reservation or PlanLinkKind.Dependency)
            .Select(link => context.Plan.Nodes.FirstOrDefault(candidate => candidate.NodeId.Equals(link.FromNodeId, StringComparison.OrdinalIgnoreCase)))
            .OfType<PlanNode>()
            .Where(candidate => candidate.Kind == PlanNodeKind.CopyOrInvention)
            .Select(ResolveBlueprintQualitySource)
            .Where(candidate => candidate is not null && candidate.ProducedBlueprintTypeId == targetBlueprintTypeId.Value)
            .Cast<BlueprintQualitySource>()
            .ToArray();

        if (candidates.Length == 0)
        {
            return false;
        }

        var distinctProfiles = candidates
            .Select(candidate => $"{candidate.MaterialEfficiencyPercent}:{candidate.TimeEfficiencyPercent}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctProfiles.Length > 1)
        {
            context.AddSoftWarning(
                $"Industrial node '{node.Title}' receives conflicting upstream blueprint quality inputs, so the strongest quality profile was selected heuristically.",
                node.NodeId);
        }

        source = candidates
            .OrderByDescending(candidate => candidate.MaterialEfficiencyPercent ?? decimal.MinValue)
            .ThenByDescending(candidate => candidate.TimeEfficiencyPercent ?? decimal.MinValue)
            .First();
        return true;
    }

    private long? ResolveBlueprintTypeIdForExecutionNode(PlanNode node)
    {
        if (PlanningMetadataReader.TryReadLong(node.Metadata, "blueprint_type_id", out var blueprintTypeId))
        {
            return blueprintTypeId;
        }

        return ResolveRecipeActivity(node)?.BlueprintTypeId;
    }

    private BlueprintQualitySource? ResolveBlueprintQualitySource(PlanNode node)
    {
        if (node.Details is not CopyOrInventionNodeDetails copyOrInvention)
        {
            return null;
        }

        long? producedBlueprintTypeId = copyOrInvention.Activity switch
        {
            CopyOrInventionActivity.Copy when PlanningMetadataReader.TryParseTypeId(copyOrInvention.BlueprintTypeId, out var copiedBlueprintTypeId) => copiedBlueprintTypeId,
            CopyOrInventionActivity.Invention => ResolveRecipeActivity(node)?.ProductTypeId,
            _ => null
        };

        if (!producedBlueprintTypeId.HasValue)
        {
            return null;
        }

        return new BlueprintQualitySource(
            node.NodeId,
            producedBlueprintTypeId.Value,
            copyOrInvention.MaterialEfficiency,
            copyOrInvention.TimeEfficiency);
    }

    private IReadOnlyList<string> InferSpecialtyTags(PlanNode node, RecipeActivityResolution recipe)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in InferSpecialtyTagsWithoutRecipe(node))
        {
            tags.Add(tag);
        }

        if (metadata.TryGetType(recipe.BlueprintTypeId, out var blueprintType))
        {
            AddTypeTags(tags, blueprintType);
        }

        if (recipe.ProductTypeId.HasValue && metadata.TryGetType(recipe.ProductTypeId.Value, out var productType))
        {
            AddTypeTags(tags, productType);
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> InferSpecialtyTagsWithoutRecipe(PlanNode node)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        switch (node.Details)
        {
            case ReactionNodeDetails:
                tags.Add("reaction_chain");
                break;
            case CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Invention }:
                tags.Add("invention");
                break;
            case CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Copy }:
                tags.Add("copying");
                break;
            case ProductionNodeDetails { Activity: ProductionActivityKind.Reprocessing or ProductionActivityKind.SalvageReprocessing }:
                tags.Add("reprocessing");
                break;
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> InferFacilityTags(PlanNode node, long? productTypeId)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semanticClass = ResolvePlannerSemanticClass(node, productTypeId);
        if (string.Equals(semanticClass, "reaction_chain", StringComparison.Ordinal))
        {
            tags.Add("reaction_structure");
        }

        if (string.Equals(semanticClass, "invention_chain", StringComparison.Ordinal) ||
            string.Equals(semanticClass, "blueprint_copying", StringComparison.Ordinal) ||
            string.Equals(semanticClass, "blueprint_research", StringComparison.Ordinal))
        {
            tags.Add("research_lab");
        }

        if (string.Equals(semanticClass, "capital_manufacturing", StringComparison.Ordinal))
        {
            tags.Add("capital_yard");
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string ResolvePlannerSemanticClass(PlanNode node)
    {
        var recipe = ResolveRecipeActivity(node);
        if (recipe is not null)
        {
            return recipe.PlannerSemanticClass;
        }

        var productTypeId = node.Details switch
        {
            ProductionNodeDetails production when PlanningMetadataReader.TryParseTypeId(production.RecipeTypeId, out var productionProductTypeId) => productionProductTypeId,
            ReactionNodeDetails reaction when PlanningMetadataReader.TryParseTypeId(reaction.ReactionTypeId, out var reactionProductTypeId) => reactionProductTypeId,
            _ => (long?)null
        };

        return ResolvePlannerSemanticClass(node, productTypeId);
    }

    public string DescribePlannerSemanticClass(string plannerSemanticClass)
    {
        return plannerSemanticClass switch
        {
            "standard_manufacturing" => "standard manufacturing",
            "capital_manufacturing" => "capital manufacturing",
            "reaction_chain" => "reaction chain",
            "invention_chain" => "invention chain",
            "blueprint_copying" => "blueprint copying",
            "blueprint_research" => "blueprint research",
            _ => "industrial work"
        };
    }

    private string ResolvePlannerSemanticClass(PlanNode node, long? productTypeId)
    {
        return node.Details switch
        {
            ReactionNodeDetails => "reaction_chain",
            CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Invention } => "invention_chain",
            CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Copy } => "blueprint_copying",
            ProductionNodeDetails when productTypeId.HasValue && IsCapitalShip(productTypeId.Value) => "capital_manufacturing",
            ProductionNodeDetails => "standard_manufacturing",
            _ => "non_plannable_misc"
        };
    }

    private void AddTypeTags(ISet<string> tags, MetadataBootstrapCatalog.MetadataTypeEntry type)
    {
        if (!string.IsNullOrWhiteSpace(type.CategoryName))
        {
            tags.Add(NormalizeTag(type.CategoryName));
        }

        if (!string.IsNullOrWhiteSpace(type.GroupName))
        {
            tags.Add(NormalizeTag(type.GroupName));
        }

        foreach (var pathEntry in metadata.GetMarketGroupPath(type.MarketGroupId))
        {
            tags.Add(NormalizeTag(pathEntry.Name));
        }

        var lowerPath = metadata.GetMarketGroupPath(type.MarketGroupId)
            .Select(entry => entry.Name.ToLowerInvariant())
            .ToArray();
        foreach (var faction in FactionKeywords)
        {
            if (lowerPath.Any(entry => entry.Contains(faction, StringComparison.Ordinal)))
            {
                tags.Add($"{faction}_hulls");
            }
        }

        foreach (var hull in HullKeywords)
        {
            if (lowerPath.Any(entry => entry.Contains(hull, StringComparison.Ordinal)) ||
                type.GroupName.Contains(hull, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add($"{NormalizeTag(hull)}_hulls");
            }
        }

        if (type.GroupName.Contains("component", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("component_manufacturing");
        }
    }

    private bool IsShip(long typeId)
    {
        return metadata.TryGetType(typeId, out var type) &&
               string.Equals(type.CategoryName, "Ship", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCapitalShip(long typeId)
    {
        if (!metadata.TryGetType(typeId, out var type))
        {
            return false;
        }

        return type.GroupName.Contains("Carrier", StringComparison.OrdinalIgnoreCase)
            || type.GroupName.Contains("Dreadnought", StringComparison.OrdinalIgnoreCase)
            || type.GroupName.Contains("Titan", StringComparison.OrdinalIgnoreCase)
            || type.GroupName.Contains("Force Auxiliary", StringComparison.OrdinalIgnoreCase)
            || type.GroupName.Contains("Freighter", StringComparison.OrdinalIgnoreCase)
            || type.GroupName.Contains("Jump Freighter", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSkillKey(string skillName)
    {
        return NormalizeTag(skillName);
    }

    private static string NormalizeTag(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var buffer = new List<char>(normalized.Length);
        var lastWasSeparator = false;
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Add(character);
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
            {
                continue;
            }

            buffer.Add('_');
            lastWasSeparator = true;
        }

        return new string(buffer.ToArray()).Trim('_');
    }
}

internal sealed record RecipeActivityResolution(
    long BlueprintTypeId,
    string ActivityKind,
    string PlannerSemanticClass,
    long? ProductTypeId,
    long? ProductQuantity,
    decimal? ProductProbability,
    long? TimeSeconds,
    long? MaxProductionLimit,
    IReadOnlyList<RecipeMaterialRequirement> Materials);

internal sealed record RecipeMaterialRequirement(
    long MaterialTypeId,
    long QuantityPerRun);

internal sealed record BlueprintQualitySource(
    string NodeId,
    long ProducedBlueprintTypeId,
    decimal? MaterialEfficiencyPercent,
    decimal? TimeEfficiencyPercent);

internal sealed record StaticIndustryRequirementProfile(
    IReadOnlyList<CharacterIndustrySkillLevel> RequiredSkills,
    IReadOnlyList<string> RequiredSpecialtyTags,
    IReadOnlyList<string> RequiredFacilityTags);
