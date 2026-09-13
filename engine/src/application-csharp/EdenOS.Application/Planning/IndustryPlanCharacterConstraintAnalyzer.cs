using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.Planning;
using System.Globalization;

namespace EdenOS.Application.Planning;

internal static class IndustryPlanCharacterConstraintAnalyzer
{
    public static bool RequiresCharacterScheduling(PlanNode node)
    {
        return ResolveRequirement(node) is not null;
    }

    public static CharacterPoolEntry? ResolveSuggestedCharacter(
        IndustryPlanComputationContext context,
        PlanNode node)
    {
        return context.SuggestedCharactersByNodeId.TryGetValue(node.NodeId, out var character)
            ? character
            : null;
    }

    public static IReadOnlyList<CharacterPoolEntry> ResolveCandidateCharacters(
        IndustryPlanComputationContext context,
        PlanNode node)
    {
        return context.NodeCandidateCharacters.TryGetValue(node.NodeId, out var characters)
            ? characters
            : Array.Empty<CharacterPoolEntry>();
    }

    public static int ResolveRequestedActivitySlots(PlanNode node)
    {
        var requirement = ResolveRequirement(node);
        return requirement is null
            ? 0
            : Math.Max(1, requirement.GetRequestedActivitySlots(node));
    }

    public static int ResolveCharacterCapacity(PlanNode node, CharacterPoolEntry character)
    {
        var requirement = ResolveRequirement(node);
        return requirement is null
            ? 0
            : Math.Max(0, requirement.GetCapacity(character));
    }

    public static string? ResolveActivityKey(PlanNode node)
    {
        return node.Details switch
        {
            ProductionNodeDetails production when production.Activity is ProductionActivityKind.Reprocessing or ProductionActivityKind.SalvageReprocessing
                => "reprocessing",
            ProductionNodeDetails
                => "manufacturing",
            ReactionNodeDetails
                => "reaction",
            CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Copy }
                => "copy",
            CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Invention }
                => "invention",
            TransportNodeDetails
                => "logistics",
            TradeNodeDetails
                => "trade",
            _ => null
        };
    }

    public static decimal ResolveTimeEfficiencyPercent(
        IndustryPlanComputationContext context,
        PlanNode node)
    {
        var character = ResolveSuggestedCharacter(context, node);
        if (character is null)
        {
            return 0m;
        }

        return node.Details switch
        {
            ProductionNodeDetails production when production.Activity is ProductionActivityKind.Manufacturing or ProductionActivityKind.Generic
                => character.CapabilityProfile.Industry.ManufacturingTimeEfficiencyPercent,
            ReactionNodeDetails
                => character.CapabilityProfile.Industry.ReactionTimeEfficiencyPercent,
            _ => 0m
        };
    }

    public static decimal ResolveInventionSuccessBonusPercent(
        IndustryPlanComputationContext context,
        PlanNode node)
    {
        var character = ResolveSuggestedCharacter(context, node);
        return node.Details is CopyOrInventionNodeDetails { Activity: CopyOrInventionActivity.Invention } && character is not null
            ? character.CapabilityProfile.Industry.InventionSuccessBonusPercent
            : 0m;
    }

    public static decimal ResolveReprocessingYieldPercent(
        IndustryPlanComputationContext context,
        PlanNode node)
    {
        var character = ResolveSuggestedCharacter(context, node);
        return node.Details is ProductionNodeDetails
            {
                Activity: ProductionActivityKind.Reprocessing or ProductionActivityKind.SalvageReprocessing
            } && character is not null
            ? character.CapabilityProfile.Industry.ReprocessingYieldPercent
            : 0m;
    }

    public static void Analyze(
        IndustryPlanComputationContext context,
        ICharacterPoolService? characterPoolService,
        IOperatorPoolService? operatorPoolService,
        IndustrialRecipeResolver recipeResolver)
    {
        if (characterPoolService is null)
        {
            return;
        }

        var characterPoolResult = characterPoolService.List(new ListCharacterPoolRequest
        {
            WorkspaceId = context.Plan.WorkspaceId
        });

        if (!characterPoolResult.IsSuccess || characterPoolResult.Data is null)
        {
            context.AddUncertainFact("Character pool could not be loaded, so role assignment checks were skipped for this computation.");
            return;
        }

        var planningCharacters = characterPoolResult.Data.Characters
            .Where(character => character.IsSelectableForIndustryPlanning)
            .Where(character => character.CapabilityProfile.CanParticipateInIndustryPlanning)
            .ToArray();

        foreach (var character in planningCharacters)
        {
            context.PlanningCharactersById[character.CharacterId] = character;
        }

        if (operatorPoolService is not null)
        {
            var operatorPoolResult = operatorPoolService.ListOperators(new ListOperatorPoolRequest
            {
                WorkspaceId = context.Plan.WorkspaceId
            });

            if (!operatorPoolResult.IsSuccess || operatorPoolResult.Data is null)
            {
                context.AddUncertainFact("Operator pool could not be loaded, so operator ownership was omitted from this computation.");
            }
            else
            {
                foreach (var operatorEntry in operatorPoolResult.Data.Operators)
                {
                    context.OperatorsById[operatorEntry.OperatorId] = operatorEntry;
                }
            }
        }

        foreach (var node in context.Plan.Nodes)
        {
            var requirement = ResolveRequirement(node);
            if (requirement is null)
            {
                continue;
            }

            var activityQualifiedCharacters = planningCharacters
                .Where(character => requirement.IsQualified(character))
                .ToArray();
            var candidates = activityQualifiedCharacters
                .Where(character => SatisfiesDetailedEligibility(node, character, recipeResolver))
                .Where(character => SatisfiesOperatorEligibility(context, node, character, recipeResolver))
                .OrderByDescending(character => requirement.GetCapacity(character))
                .ThenByDescending(character => requirement.GetSpeedBonus(character))
                .ThenByDescending(character => CountSpecialtyMatches(node, character, recipeResolver))
                .ThenByDescending(character => CountOperatorResponsibilityMatches(context, node, character, recipeResolver))
                .ThenByDescending(character => character.IsPrimaryAccountCharacter)
                .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            context.NodeCandidateCharacters[node.NodeId] = candidates;

            if (candidates.Length > 0)
            {
                context.SuggestedCharactersByNodeId[node.NodeId] = candidates[0];
                context.SuggestedCharacterRationalesByNodeId[node.NodeId] =
                    BuildSuggestedCharacterRationale(node, candidates[0], recipeResolver);
            }

            if (candidates.Length == 0)
            {
                var eligibilityMessage = BuildDetailedEligibilityMessage(context, node, activityQualifiedCharacters, recipeResolver);
                var message = eligibilityMessage is null
                    ? $"Node '{node.Title}' requires {requirement.ActivityLabel}, but no selectable character in the workspace can cover that activity."
                    : $"Node '{node.Title}' requires {requirement.ActivityLabel}, but no selectable character satisfies {eligibilityMessage}.";
                if (requirement.MissingCandidateIsHardConflict)
                {
                    context.AddHardConflict(message, node.NodeId);
                }
                else
                {
                    context.AddSoftWarning(message, node.NodeId);
                }

                continue;
            }

            var requiredCharacters = Math.Max(node.ResourceProfile.CharacterSlots, 1);
            if (candidates.Length < requiredCharacters)
            {
                context.AddSoftWarning(
                    $"Node '{node.Title}' expects {requiredCharacters} character slot(s), but only {candidates.Length} qualified character(s) are currently available for {requirement.ActivityLabel}.",
                    node.NodeId);
            }

            var requiredActivitySlots = requirement.GetRequestedActivitySlots(node);
            if (requiredActivitySlots <= 0)
            {
                continue;
            }

            var totalActivityCapacity = candidates.Sum(requirement.GetCapacity);
            if (totalActivityCapacity < requiredActivitySlots)
            {
                context.AddSoftWarning(
                    $"Node '{node.Title}' requests {requiredActivitySlots} {requirement.CapacityLabel}, but the current candidate pool only exposes {totalActivityCapacity}.",
                    node.NodeId);
            }
        }
    }

    private static CharacterAssignmentRequirement? ResolveRequirement(PlanNode node)
    {
        return node.Details switch
        {
            ProductionNodeDetails production => ResolveProductionRequirement(production),
            ReactionNodeDetails => new CharacterAssignmentRequirement(
                "reaction work",
                "reaction slot(s)",
                true,
                character => character.CapabilityProfile.Industry.CanRunReactionJobs,
                character => character.CapabilityProfile.Industry.ReactionJobSlotCapacity,
                character => character.CapabilityProfile.Industry.ReactionTimeEfficiencyPercent,
                currentNode => Math.Max(currentNode.ResourceProfile.JobSlots, 1)),
            CopyOrInventionNodeDetails copyOrInvention when copyOrInvention.Activity == CopyOrInventionActivity.Copy => new CharacterAssignmentRequirement(
                "copy work",
                "copy slot(s)",
                true,
                character => character.CapabilityProfile.Industry.CanRunCopyJobs,
                character => character.CapabilityProfile.Industry.CopyJobSlotCapacity,
                _ => 0m,
                currentNode => Math.Max(currentNode.ResourceProfile.JobSlots, 1)),
            CopyOrInventionNodeDetails => new CharacterAssignmentRequirement(
                "invention work",
                "invention slot(s)",
                true,
                character => character.CapabilityProfile.Industry.CanRunInventionJobs,
                character => character.CapabilityProfile.Industry.InventionJobSlotCapacity,
                character => character.CapabilityProfile.Industry.InventionSuccessBonusPercent,
                currentNode => Math.Max(currentNode.ResourceProfile.JobSlots, 1)),
            TransportNodeDetails => new CharacterAssignmentRequirement(
                "transport coverage",
                "transport slot(s)",
                false,
                character => character.CapabilityProfile.CanCoverLogisticsTasks,
                _ => 1,
                _ => 0m,
                _ => 1),
            TradeNodeDetails trade => new CharacterAssignmentRequirement(
                trade.TradeMode == PlanTradeMode.Sale ? "market selling coverage" : "market purchasing coverage",
                "market coverage slot(s)",
                false,
                character => character.CapabilityProfile.CanProvideAuthenticatedMarketAccess || character.CapabilityProfile.CanCoverLogisticsTasks,
                _ => 1,
                _ => 0m,
                _ => 1),
            _ => null
        };
    }

    private static CharacterAssignmentRequirement ResolveProductionRequirement(ProductionNodeDetails production)
    {
        return production.Activity switch
        {
            ProductionActivityKind.Reprocessing or ProductionActivityKind.SalvageReprocessing => new CharacterAssignmentRequirement(
                "reprocessing work",
                "reprocessing slot(s)",
                true,
                character => character.CapabilityProfile.Industry.CanRunReprocessingJobs,
                character => character.CapabilityProfile.Industry.ReprocessingJobSlotCapacity,
                character => character.CapabilityProfile.Industry.ReprocessingYieldPercent,
                currentNode => Math.Max(currentNode.ResourceProfile.JobSlots, 1)),
            _ => new CharacterAssignmentRequirement(
                "manufacturing work",
                "manufacturing slot(s)",
                true,
                character => character.CapabilityProfile.Industry.CanRunManufacturingJobs,
                character => character.CapabilityProfile.Industry.ManufacturingJobSlotCapacity,
                character => character.CapabilityProfile.Industry.ManufacturingTimeEfficiencyPercent,
                currentNode => Math.Max(currentNode.ResourceProfile.JobSlots, 1))
        };
    }

    private static bool SatisfiesDetailedEligibility(PlanNode node, CharacterPoolEntry character, IndustrialRecipeResolver recipeResolver)
    {
        var industry = character.CapabilityProfile.Industry;
        var requirements = ResolveRequirementProfile(node, recipeResolver);

        var skillLevels = industry.SkillLevels.ToDictionary(
            skill => skill.SkillKey,
            skill => skill.Level,
            StringComparer.OrdinalIgnoreCase);
        var specialtyTags = industry.SpecialtyTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var facilityTags = industry.FacilityAccessTags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requirements.RequiredSkills.All(requirement => skillLevels.GetValueOrDefault(requirement.SkillKey, 0) >= requirement.Level)
            && requirements.RequiredSpecialtyTags.All(tag => specialtyTags.Contains(tag))
            && requirements.RequiredFacilityTags.All(tag => facilityTags.Contains(tag));
    }

    private static string? BuildDetailedEligibilityMessage(
        IndustryPlanComputationContext context,
        PlanNode node,
        IReadOnlyList<CharacterPoolEntry> activityQualifiedCharacters,
        IndustrialRecipeResolver recipeResolver)
    {
        var requirements = ResolveRequirementProfile(node, recipeResolver);
        var requiredSkills = requirements.RequiredSkills;
        var requiredSpecialtyTags = requirements.RequiredSpecialtyTags;
        var requiredFacilityTags = requirements.RequiredFacilityTags;
        var responsibilityTags = ResolveOperatorResponsibilityTags(node, recipeResolver);
        var requiredOperatorTags = ResolveRequiredOperatorTags(node);
        if (requiredSkills.Count == 0 &&
            requiredSpecialtyTags.Count == 0 &&
            requiredFacilityTags.Count == 0 &&
            responsibilityTags.Count == 0 &&
            requiredOperatorTags.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (requiredSkills.Count > 0)
        {
            var labels = requiredSkills
                .Select(skill => $"{skill.SkillKey} {skill.Level}")
                .ToArray();
            parts.Add($"skill gates ({string.Join(", ", labels)})");
        }

        if (requiredSpecialtyTags.Count > 0)
        {
            parts.Add($"specialty tags ({string.Join(", ", requiredSpecialtyTags)})");
        }

        if (requiredFacilityTags.Count > 0)
        {
            parts.Add($"facility tags ({string.Join(", ", requiredFacilityTags)})");
        }

        if (responsibilityTags.Count > 0 &&
            activityQualifiedCharacters.Any(character => character.OperatorId is not null))
        {
            parts.Add($"operator responsibility tags ({string.Join(", ", responsibilityTags)})");
        }

        if (requiredOperatorTags.Count > 0 &&
            activityQualifiedCharacters.Any(character => character.OperatorId is not null))
        {
            parts.Add($"required operator tags ({string.Join(", ", requiredOperatorTags)})");
        }

        if (activityQualifiedCharacters.Count == 0)
        {
            return string.Join(" and ", parts);
        }

        return $"{string.Join(" and ", parts)} on the currently available characters";
    }

    private static int CountSpecialtyMatches(PlanNode node, CharacterPoolEntry character, IndustrialRecipeResolver recipeResolver)
    {
        var requiredTags = ResolveSpecialtyPreferenceTags(node, recipeResolver);
        if (requiredTags.Count == 0)
        {
            return 0;
        }

        var characterTags = character.CapabilityProfile.Industry.SpecialtyTags
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requiredTags.Count(tag => characterTags.Contains(tag));
    }

    private static bool SatisfiesOperatorEligibility(
        IndustryPlanComputationContext context,
        PlanNode node,
        CharacterPoolEntry character,
        IndustrialRecipeResolver recipeResolver)
    {
        if (character.OperatorId is null)
        {
            return true;
        }

        if (!context.OperatorsById.TryGetValue(character.OperatorId, out var operatorEntry))
        {
            return true;
        }

        if (!operatorEntry.CanCoordinateIndustryPlanning)
        {
            return false;
        }

        var operatorTags = operatorEntry.ResponsibilityTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredOperatorTags = ResolveRequiredOperatorTags(node);
        if (requiredOperatorTags.Count > 0)
        {
            return requiredOperatorTags.All(tag => operatorTags.Contains(tag));
        }

        if (operatorEntry.ResponsibilityTags.Count == 0)
        {
            return true;
        }

        var requiredTags = ResolveOperatorResponsibilityTags(node, recipeResolver);
        return requiredTags.Count == 0 || requiredTags.Any(tag => operatorTags.Contains(tag));
    }

    private static int CountOperatorResponsibilityMatches(
        IndustryPlanComputationContext context,
        PlanNode node,
        CharacterPoolEntry character,
        IndustrialRecipeResolver recipeResolver)
    {
        if (character.OperatorId is null || !context.OperatorsById.TryGetValue(character.OperatorId, out var operatorEntry))
        {
            return 0;
        }

        if (operatorEntry.ResponsibilityTags.Count == 0)
        {
            return 0;
        }

        var operatorTags = operatorEntry.ResponsibilityTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ResolveOperatorResponsibilityTags(node, recipeResolver).Count(tag => operatorTags.Contains(tag))
            + ResolveRequiredOperatorTags(node).Count(tag => operatorTags.Contains(tag));
    }

    private static IReadOnlyList<string> ResolveOperatorResponsibilityTags(
        PlanNode node,
        IndustrialRecipeResolver recipeResolver)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activityKey = ResolveActivityKey(node);
        if (activityKey is not null)
        {
            tags.Add(activityKey);
            tags.Add($"{activityKey}_ops");
        }

        tags.Add("industry_planning");

        var plannerSemanticClass = recipeResolver.ResolvePlannerSemanticClass(node);
        tags.Add(plannerSemanticClass);
        if (string.Equals(plannerSemanticClass, "standard_manufacturing", StringComparison.Ordinal))
        {
            tags.Add("t1_manufacturing");
        }

        foreach (var specialtyTag in ResolveSpecialtyPreferenceTags(node, recipeResolver))
        {
            tags.Add(specialtyTag);
        }

        foreach (var explicitTag in ReadTagSet(node.Metadata, "operator_responsibility_tags"))
        {
            tags.Add(explicitTag);
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ResolveRequiredOperatorTags(PlanNode node)
    {
        return ReadTagSet(node.Metadata, "required_operator_tags");
    }

    private static string BuildSuggestedCharacterRationale(
        PlanNode node,
        CharacterPoolEntry character,
        IndustrialRecipeResolver recipeResolver)
    {
        var parts = new List<string>();
        var activityKey = ResolveActivityKey(node);
        var capacity = ResolveCharacterCapacity(node, character);
        var specialtyMatches = CountSpecialtyMatches(node, character, recipeResolver);

        switch (activityKey)
        {
            case "manufacturing":
                parts.Add($"offers {capacity} manufacturing slot(s)");
                AppendPercentPart(parts, character.CapabilityProfile.Industry.ManufacturingTimeEfficiencyPercent, "manufacturing time bonus");
                break;
            case "reaction":
                parts.Add($"offers {capacity} reaction slot(s)");
                AppendPercentPart(parts, character.CapabilityProfile.Industry.ReactionTimeEfficiencyPercent, "reaction time bonus");
                break;
            case "invention":
                parts.Add($"offers {capacity} invention slot(s)");
                AppendPercentPart(parts, character.CapabilityProfile.Industry.InventionSuccessBonusPercent, "invention success bonus");
                break;
            case "copy":
                parts.Add($"offers {capacity} copy slot(s)");
                break;
            case "reprocessing":
                parts.Add($"offers {capacity} reprocessing slot(s)");
                AppendPercentPart(parts, character.CapabilityProfile.Industry.ReprocessingYieldPercent, "reprocessing yield bonus");
                break;
            case "logistics":
                parts.Add("can cover logistics tasks");
                break;
            case "trade":
                parts.Add(character.CapabilityProfile.CanProvideAuthenticatedMarketAccess
                    ? "has market-authenticated access"
                    : "can at least cover trade execution operationally");
                break;
        }

        if (specialtyMatches > 0)
        {
            parts.Add($"matches {specialtyMatches} specialty tag(s)");
        }

        var requirements = ResolveRequirementProfile(node, recipeResolver);
        if (requirements.RequiredFacilityTags.Count > 0)
        {
            parts.Add($"covers facility tags {string.Join(", ", requirements.RequiredFacilityTags)}");
        }

        if (requirements.RequiredSkills.Count > 0)
        {
            parts.Add($"passes {requirements.RequiredSkills.Count} skill gate(s)");
        }

        if (character.IsPrimaryAccountCharacter)
        {
            parts.Add("is the primary account character");
        }

        if (character.OperatorId is not null)
        {
            parts.Add("already has an operator binding");
        }

        return $"Suggested '{character.DisplayName}' because it {string.Join(", ", parts)}.";
    }

    private static void AppendPercentPart(List<string> parts, decimal value, string label)
    {
        if (value > 0m)
        {
            parts.Add($"{value:N0}% {label}");
        }
    }

    private static ResolvedIndustryRequirementProfile ResolveRequirementProfile(PlanNode node, IndustrialRecipeResolver recipeResolver)
    {
        var explicitSkills = ReadRequiredSkills(node.Metadata);
        var explicitSpecialtyTags = ReadTagSet(node.Metadata, "industry_required_specialty_tags", "required_specialty_tags");
        var explicitFacilityTags = ReadTagSet(node.Metadata, "industry_required_facility_tags", "required_facility_tags");
        var staticRequirements = recipeResolver.ResolveStaticRequirements(node);

        return new ResolvedIndustryRequirementProfile(
            MergeSkills(staticRequirements.RequiredSkills, explicitSkills),
            explicitSpecialtyTags,
            MergeTags(staticRequirements.RequiredFacilityTags, explicitFacilityTags));
    }

    private static IReadOnlyList<string> ResolveSpecialtyPreferenceTags(PlanNode node, IndustrialRecipeResolver recipeResolver)
    {
        return MergeTags(
            recipeResolver.ResolveSpecialtyPreferenceTags(node),
            ReadTagSet(node.Metadata, "industry_required_specialty_tags", "required_specialty_tags"));
    }

    private static IReadOnlyList<CharacterIndustrySkillLevel> ReadRequiredSkills(IReadOnlyDictionary<string, string> metadata)
    {
        if (!PlanningMetadataReader.TryReadString(metadata, "industry_required_skills", out var rawValue) &&
            !PlanningMetadataReader.TryReadString(metadata, "required_skills", out rawValue))
        {
            return Array.Empty<CharacterIndustrySkillLevel>();
        }

        return rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(parts => new CharacterIndustrySkillLevel
            {
                SkillKey = parts[0],
                Level = int.Parse(parts[1], CultureInfo.InvariantCulture)
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ReadTagSet(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!PlanningMetadataReader.TryReadString(metadata, key, out var rawValue))
            {
                continue;
            }

            return rawValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<CharacterIndustrySkillLevel> MergeSkills(
        IReadOnlyList<CharacterIndustrySkillLevel> left,
        IReadOnlyList<CharacterIndustrySkillLevel> right)
    {
        return left
            .Concat(right)
            .GroupBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CharacterIndustrySkillLevel
            {
                SkillKey = group.Key,
                Level = group.Max(item => item.Level)
            })
            .OrderBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> MergeTags(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        return left
            .Concat(right)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record CharacterAssignmentRequirement(
        string ActivityLabel,
        string CapacityLabel,
        bool MissingCandidateIsHardConflict,
        Func<CharacterPoolEntry, bool> IsQualified,
        Func<CharacterPoolEntry, int> GetCapacity,
        Func<CharacterPoolEntry, decimal> GetSpeedBonus,
        Func<PlanNode, int> GetRequestedActivitySlots);

    private sealed record ResolvedIndustryRequirementProfile(
        IReadOnlyList<CharacterIndustrySkillLevel> RequiredSkills,
        IReadOnlyList<string> RequiredSpecialtyTags,
        IReadOnlyList<string> RequiredFacilityTags);
}
