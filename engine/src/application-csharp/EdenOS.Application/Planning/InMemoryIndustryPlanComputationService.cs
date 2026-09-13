using System.Globalization;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Application.Market;
using EdenOS.Contracts.Market;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Planning;

public sealed class InMemoryIndustryPlanComputationService : IIndustryPlanComputationService
{
    private const long DefaultMarketLocationId = 60003760;
    private readonly IIndustryPlanService _planService;
    private readonly IndustrialRecipeResolver _recipeResolver;
    private readonly IndustryPlanMarketPriceResolver _marketPriceResolver;
    private readonly IndustryPlanCostModelBuilder _costModelBuilder;
    private readonly ICharacterPoolService? _characterPoolService;
    private readonly IOperatorPoolService? _operatorPoolService;

    public InMemoryIndustryPlanComputationService(
        IIndustryPlanService planService,
        IMarketFactsService marketFactsService,
        ICharacterPoolService? characterPoolService = null)
        : this(
            planService,
            marketFactsService,
            characterPoolService,
            characterPoolService as IOperatorPoolService,
            MetadataBootstrapCatalog.LoadDefault())
    {
    }

    internal InMemoryIndustryPlanComputationService(
        IIndustryPlanService planService,
        IMarketFactsService marketFactsService,
        ICharacterPoolService? characterPoolService,
        IOperatorPoolService? operatorPoolService,
        MetadataBootstrapCatalog metadata)
    {
        _planService = planService;
        _recipeResolver = new IndustrialRecipeResolver(metadata);
        _marketPriceResolver = new IndustryPlanMarketPriceResolver(marketFactsService);
        _costModelBuilder = new IndustryPlanCostModelBuilder(_recipeResolver, _marketPriceResolver);
        _characterPoolService = characterPoolService;
        _operatorPoolService = operatorPoolService;
    }

    public UseCaseResult<PlanComputationResult> Compute(PlanComputeRequest request)
    {
        return EvaluatePlan(
            request.WorkspaceId,
            request.PlanId,
            request.MarketLocationId,
            request.MarketAccountKey,
            request.AlternativePathLimit,
            recomputeReason: null);
    }

    public UseCaseResult<PlanComputationResult> Recompute(PlanRecomputeRequest request)
    {
        return EvaluatePlan(
            request.WorkspaceId,
            request.PlanId,
            request.MarketLocationId,
            request.MarketAccountKey,
            request.AlternativePathLimit,
            PlanningMetadataReader.NormalizeOptional(request.Reason));
    }

    public UseCaseResult<PlanFeasibilitySummary> GetFeasibility(GetPlanFeasibilityRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, alternativePathLimit: 3, recomputeReason: null),
            computation => computation.Feasibility,
            "Loaded plan feasibility summary.");

    public UseCaseResult<PlanCostBreakdown> GetCostBreakdown(GetPlanCostBreakdownRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, alternativePathLimit: 3, recomputeReason: null),
            computation => computation.CostBreakdown,
            "Loaded plan cost breakdown.");

    public UseCaseResult<PlanProfitEstimate> GetProfitEstimate(GetPlanProfitEstimateRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, alternativePathLimit: 3, recomputeReason: null),
            computation => computation.ProfitEstimate,
            "Loaded plan profit estimate.");

    public UseCaseResult<PlanTimeEstimate> GetTimeEstimate(GetPlanTimeEstimateRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, alternativePathLimit: 3, recomputeReason: null),
            computation => computation.TimeEstimate,
            "Loaded plan time estimate.");

    public UseCaseResult<PlanConstraintSummary> GetConstraintSummary(GetPlanConstraintSummaryRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, alternativePathLimit: 3, recomputeReason: null),
            computation => computation.ConstraintSummary,
            "Loaded plan constraint summary.");

    public UseCaseResult<IReadOnlyList<PlanAlternativePath>> GetAlternativePaths(GetPlanAlternativePathsRequest request) =>
        Project(
            EvaluatePlan(request.WorkspaceId, request.PlanId, request.MarketLocationId, request.MarketAccountKey, request.AlternativePathLimit, recomputeReason: null),
            computation => computation.AlternativePaths,
            "Loaded plan alternative paths.");

    private UseCaseResult<PlanComputationResult> EvaluatePlan(
        string workspaceId,
        string planId,
        long? marketLocationId,
        string? marketAccountKey,
        int alternativePathLimit,
        string? recomputeReason)
    {
        var traceId = NextTraceId();
        var normalizedWorkspaceId = PlanningMetadataReader.NormalizeOptional(workspaceId) ?? string.Empty;
        var normalizedPlanId = PlanningMetadataReader.NormalizeOptional(planId);
        if (normalizedPlanId is null)
        {
            return UseCaseResult<PlanComputationResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Plan id is required.",
                traceId,
                ["plan_id must not be empty."]);
        }

        if (alternativePathLimit <= 0 || alternativePathLimit > 5)
        {
            return UseCaseResult<PlanComputationResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Alternative path limit must be between 1 and 5.",
                traceId,
                ["alternative_path_limit must be between 1 and 5."]);
        }

        var planResult = _planService.Get(new GetPlanRequest
        {
            WorkspaceId = normalizedWorkspaceId,
            PlanId = normalizedPlanId
        });

        if (!planResult.IsSuccess || planResult.Data is null)
        {
            return UseCaseResult<PlanComputationResult>.Failure(
                planResult.Status,
                planResult.Summary,
                traceId,
                planResult.Errors,
                planResult.Warnings);
        }

        var plan = planResult.Data;
        var resolvedLocationId = ResolveMarketLocationId(plan, marketLocationId);
        var resolvedAccountKey = ResolveMarketAccountKey(plan, marketAccountKey);
        var context = new IndustryPlanComputationContext(plan, resolvedLocationId, resolvedAccountKey);

        if (plan.Goal is null)
        {
            context.AddHardConflict("Plan goal is missing; feasibility and revenue cannot be explained without a target.");
        }

        if (plan.Nodes.Count == 0)
        {
            context.AddHardConflict("Plan has no nodes, so there is nothing to compute.");
        }

        if (!plan.Preferences.AllowMultiLocationExecution)
        {
            var distinctLocations = plan.Nodes
                .Select(node => PlanningMetadataReader.NormalizeOptional(node.LocationLabel))
                .Where(location => location is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctLocations > 1 || plan.Nodes.Any(node => node.Kind == PlanNodeKind.Transport))
            {
                context.AddHardConflict("Plan preference forbids multi-location execution, but the current graph spans transport or multiple locations.");
            }
        }

        AnalyzePreferenceBudgets(context);
        AnalyzeNodeSemantics(context);
        IndustryPlanCharacterConstraintAnalyzer.Analyze(context, _characterPoolService, _operatorPoolService, _recipeResolver);

        var lineItems = _costModelBuilder.BuildCostAndRevenue(context);
        var timeModel = IndustryPlanTimeEstimator.BuildTimeModel(context, _recipeResolver);
        var reservationSummary = IndustryPlanComputationSummaryBuilder.BuildReservationSummary(context);
        var nodeStatuses = IndustryPlanComputationSummaryBuilder.BuildNodeConstraintStatuses(context);
        var constraintSummary = new PlanConstraintSummary
        {
            HardConflicts = context.HardConflicts.ToArray(),
            SoftWarnings = context.SoftWarnings.ToArray(),
            ResourceReservations = reservationSummary,
            NodeStatuses = nodeStatuses,
            UncertainFacts = context.UncertainFacts.ToArray()
        };

        var costBreakdown = IndustryPlanComputationSummaryBuilder.BuildCostBreakdown(lineItems, context);
        var profitEstimate = IndustryPlanComputationSummaryBuilder.BuildProfitEstimate(plan, costBreakdown, lineItems, context);
        var feasibility = IndustryPlanComputationSummaryBuilder.BuildFeasibilitySummary(context, costBreakdown, profitEstimate);
        var alternatives = IndustrialAlternativePathPlanner.BuildAlternativePaths(plan, costBreakdown, profitEstimate, timeModel, alternativePathLimit, context, _recipeResolver);

        var explanations = new List<string>
        {
            $"Computed against market location '{resolvedLocationId}'.",
            $"Evaluated {plan.Nodes.Count} node(s) and {plan.Links.Count} link(s).",
            "Role, blueprint, BPC, and job slots still behave as reservation-style resources; real SDE recipes are used when a node can be matched to blueprint activities and materials."
        };

        var executionBurden = AnalyzeExecutionBurden(plan);
        explanations.Add(
            $"Current graph implies about {executionBurden.ManualOperations} manual touchpoint(s), {executionBurden.TransportLegs} transport leg(s), and {executionBurden.DistinctLocations} execution location(s).");

        if (plan.Preferences.MaxDailyManualOperations.HasValue)
        {
            explanations.Add($"Manual-operation budget: {plan.Preferences.MaxDailyManualOperations.Value} touchpoint(s) per day.");
        }

        if (plan.Preferences.MaxAcceptedTransportLegs.HasValue)
        {
            explanations.Add($"Transport-leg budget: {plan.Preferences.MaxAcceptedTransportLegs.Value}.");
        }

        explanations.Add($"Logistics tolerance: {DescribeLogisticsTolerance(plan.Preferences.LogisticsTolerance)}.");

        if (context.NodeCandidateCharacters.Count > 0)
        {
            explanations.Add($"Checked workspace character coverage for {context.NodeCandidateCharacters.Count} node(s); candidate and suggested assignee data are now emitted per node.");
        }

        var liveEsiSuggestedNodeCount = context.SuggestedCharactersByNodeId
            .Values
            .Count(character => string.Equals(
                character.CapabilityProfile.SkillProfile,
                "esi_live_skill_snapshot",
                StringComparison.OrdinalIgnoreCase));
        if (liveEsiSuggestedNodeCount > 0)
        {
            explanations.Add($"Live ESI skill snapshots informed character capacity or industrial bonus estimates for {liveEsiSuggestedNodeCount} node(s).");
        }

        if (context.OperatorsById.Count > 0)
        {
            explanations.Add($"Loaded {context.OperatorsById.Count} operator record(s), so suggested characters now also resolve to operator ownership where available.");
        }

        if (_recipeResolver.HasStaticSkillRequirementData)
        {
            explanations.Add("Static blueprint skill-requirement rows are loaded, so automatic role filtering includes blueprint activity skill gates alongside specialty/facility tags and any explicit node metadata overrides.");
        }
        else
        {
            explanations.Add("Static blueprint skill-requirement rows are unavailable in the loaded metadata bundle, so automatic role filtering falls back to inferred specialty/facility tags plus any explicit node metadata skill gates.");
        }

        if (context.RecipeBackedNodeIds.Count > 0)
        {
            explanations.Add($"Matched {context.RecipeBackedNodeIds.Count} industrial node(s) to real SDE recipe metadata.");
        }

        if (context.InheritedBlueprintQualityNodeIds.Count > 0)
        {
            explanations.Add($"Inherited upstream blueprint quality for {context.InheritedBlueprintQualityNodeIds.Count} industrial node(s).");
        }

        if (context.ProbabilityAdjustedNodeIds.Count > 0)
        {
            explanations.Add($"Applied invention-style success probability when estimating {context.ProbabilityAdjustedNodeIds.Count} node(s).");
        }

        if (context.CharacterAdjustedTimeNodeIds.Count > 0)
        {
            explanations.Add($"Applied suggested-character industrial time bonuses to {context.CharacterAdjustedTimeNodeIds.Count} node(s).");
        }

        if (context.CharacterAdjustedProbabilityNodeIds.Count > 0)
        {
            explanations.Add($"Applied suggested-character invention success bonuses to {context.CharacterAdjustedProbabilityNodeIds.Count} node(s).");
        }

        if (context.CharacterAdjustedYieldNodeIds.Count > 0)
        {
            explanations.Add($"Applied suggested-character reprocessing yield bonuses to {context.CharacterAdjustedYieldNodeIds.Count} node(s).");
        }

        if (recomputeReason is not null)
        {
            explanations.Add($"Recompute reason: {recomputeReason}.");
        }

        if (context.UsedPlaceholderMarketFacts)
        {
            explanations.Add("Market-backed estimates are currently based on placeholder fact fixtures and should be treated as provisional.");
        }

        var data = new PlanComputationResult
        {
            PlanId = plan.PlanId,
            PlanName = plan.Name,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            MarketLocationId = resolvedLocationId,
            MarketAccountKey = resolvedAccountKey,
            Feasibility = feasibility,
            CostBreakdown = costBreakdown,
            ProfitEstimate = profitEstimate,
            TimeEstimate = timeModel,
            ConstraintSummary = constraintSummary,
            AlternativePaths = alternatives,
            Explanations = explanations
        };

        var warnings = context.Warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = feasibility.IsFeasible
            ? $"Computed industrial plan '{plan.Name}' with explainable feasibility, cost, profit, time, and constraint outputs."
            : $"Computed industrial plan '{plan.Name}', but the current graph is not feasible under its declared constraints.";

        return UseCaseResult<PlanComputationResult>.Success(data, summary, traceId, warnings);
    }

    private static long ResolveMarketLocationId(IndustryPlan plan, long? requestedLocationId)
    {
        if (requestedLocationId.HasValue)
        {
            return requestedLocationId.Value;
        }

        foreach (var node in plan.Nodes)
        {
            if (PlanningMetadataReader.TryReadLong(node.Metadata, "market_location_id", out var nodeLocationId))
            {
                return nodeLocationId;
            }
        }

        return DefaultMarketLocationId;
    }

    private static string? ResolveMarketAccountKey(IndustryPlan plan, string? requestedAccountKey)
    {
        var normalizedRequested = PlanningMetadataReader.NormalizeOptional(requestedAccountKey);
        if (normalizedRequested is not null)
        {
            return normalizedRequested;
        }

        foreach (var node in plan.Nodes)
        {
            if (PlanningMetadataReader.TryReadString(node.Metadata, "account_key", out var accountKey))
            {
                return accountKey;
            }
        }

        return null;
    }

    private static void AnalyzeNodeSemantics(IndustryPlanComputationContext context)
    {
        foreach (var node in context.Plan.Nodes)
        {
            switch (node.Details)
            {
                case TradeNodeDetails trade when trade.TradeMode == PlanTradeMode.Purchase && !context.Plan.Preferences.AllowMarketPurchases:
                    context.AddHardConflict($"Trade node '{node.Title}' requires market purchasing, but the plan preferences forbid market purchases.", node.NodeId);
                    break;
                case TradeNodeDetails trade when trade.TradeMode == PlanTradeMode.OutsourceInput && !context.Plan.Preferences.AllowOutsourcing:
                    context.AddHardConflict($"Trade node '{node.Title}' depends on outsourced inputs, but the plan preferences forbid outsourcing.", node.NodeId);
                    break;
                case CopyOrInventionNodeDetails copyOrInvention:
                    if (copyOrInvention.RequiresPerBpcTracking && node.ResourceProfile.BpcSlots <= 0)
                    {
                        context.AddHardConflict($"Copy or invention node '{node.Title}' requires per-BPC tracking but declares no BPC slot reservation.", node.NodeId);
                    }

                    if (node.ResourceProfile.BlueprintSlots <= 0)
                    {
                        context.AddHardConflict($"Copy or invention node '{node.Title}' declares no blueprint slot reservation.", node.NodeId);
                    }

                    if (node.ResourceProfile.JobSlots <= 0)
                    {
                        context.AddSoftWarning($"Copy or invention node '{node.Title}' declares no job slot reservation, so time confidence is reduced.", node.NodeId);
                    }

                    break;
                case ProductionNodeDetails:
                case ReactionNodeDetails:
                    if (node.ResourceProfile.JobSlots <= 0)
                    {
                        context.AddSoftWarning($"Industrial node '{node.Title}' declares no job slot reservation, so the schedule remains a heuristic estimate.", node.NodeId);
                    }

                    break;
                case TransportNodeDetails transport:
                    if (string.Equals(transport.SourceLocation, transport.DestinationLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        context.AddSoftWarning($"Transport node '{node.Title}' moves between the same source and destination, so it may be redundant.", node.NodeId);
                    }

                    break;
            }
        }

        var reservationLinks = context.Plan.Links.Count(link => link.Kind == PlanLinkKind.Reservation);
        var declaredReservedNodes = context.Plan.Nodes.Count(node => SumReservedSlots(node.ResourceProfile) > 0);
        if (declaredReservedNodes > 0 && reservationLinks == 0)
        {
            context.AddSoftWarning("Plan declares slot reservations but contains no reservation links, so contention reasoning is only node-local.");
        }
    }

    private void AnalyzePreferenceBudgets(IndustryPlanComputationContext context)
    {
        var burden = AnalyzeExecutionBurden(context.Plan);
        var preferences = context.Plan.Preferences;

        if (preferences.MaxDailyManualOperations.HasValue &&
            burden.ManualOperations > preferences.MaxDailyManualOperations.Value)
        {
            context.AddHardConflict(
                $"Plan is estimated to require {burden.ManualOperations} manual touchpoint(s), which exceeds the daily budget of {preferences.MaxDailyManualOperations.Value}."); 
        }

        if (preferences.MaxAcceptedTransportLegs.HasValue &&
            burden.TransportLegs > preferences.MaxAcceptedTransportLegs.Value)
        {
            context.AddHardConflict(
                $"Plan needs {burden.TransportLegs} transport leg(s), which exceeds the accepted transport budget of {preferences.MaxAcceptedTransportLegs.Value}.");
        }

        switch (preferences.LogisticsTolerance)
        {
            case PlanLogisticsTolerance.Minimal when burden.TransportLegs > 0:
                context.AddHardConflict(
                    $"Plan declares minimal logistics tolerance, but the current graph still requires {burden.TransportLegs} transport leg(s).");
                break;
            case PlanLogisticsTolerance.Balanced when burden.TransportLegs > 2:
                context.AddSoftWarning(
                    $"Plan exceeds balanced logistics tolerance because it already spans {burden.TransportLegs} transport leg(s).");
                break;
        }

        if (preferences.RequireOwnedReactionFacility)
        {
            RequireOwnedFacilityForTag(context, "reaction_structure", "owned reaction structure");
        }

        if (preferences.RequireOwnedCapitalProductionFacility)
        {
            RequireOwnedFacilityForTag(context, "capital_yard", "owned capital yard");
        }
    }

    private void RequireOwnedFacilityForTag(
        IndustryPlanComputationContext context,
        string facilityTag,
        string facilityLabel)
    {
        foreach (var node in context.Plan.Nodes)
        {
            var requirements = _recipeResolver.ResolveStaticRequirements(node);
            if (!requirements.RequiredFacilityTags.Contains(facilityTag, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NodeDeclaresOwnedFacility(node))
            {
                continue;
            }

            context.AddHardConflict(
                $"Node '{node.Title}' needs {facilityLabel} access under current preferences, but the node is not marked with owned facility access metadata.",
                node.NodeId);
        }
    }

    private static bool NodeDeclaresOwnedFacility(PlanNode node)
    {
        return TryReadFacilityAccessMode(node.Metadata, out var facilityAccessMode) &&
               string.Equals(facilityAccessMode, "owned", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadFacilityAccessMode(
        IReadOnlyDictionary<string, string> metadata,
        out string facilityAccessMode)
    {
        return PlanningMetadataReader.TryReadString(metadata, "industry_facility_access_mode", out facilityAccessMode)
               || PlanningMetadataReader.TryReadString(metadata, "facility_access_mode", out facilityAccessMode)
               || PlanningMetadataReader.TryReadString(metadata, "facility_owner_scope", out facilityAccessMode);
    }

    private static ExecutionBurdenProfile AnalyzeExecutionBurden(IndustryPlan plan)
    {
        var manualOperations = plan.Nodes.Sum(EstimateManualTouchpoints);
        var transportLegs = plan.Nodes.Count(node => node.Kind == PlanNodeKind.Transport);
        var distinctLocations = CollectExecutionLocations(plan).Count;
        return new ExecutionBurdenProfile(
            Math.Max(1, manualOperations),
            transportLegs,
            Math.Max(1, distinctLocations));
    }

    private static int EstimateManualTouchpoints(PlanNode node)
    {
        return node.Kind switch
        {
            PlanNodeKind.InventoryPool => 0,
            _ => 1
        };
    }

    private static IReadOnlyCollection<string> CollectExecutionLocations(IndustryPlan plan)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in plan.Nodes)
        {
            var locationLabel = PlanningMetadataReader.NormalizeOptional(node.LocationLabel);
            if (locationLabel is not null)
            {
                locations.Add(locationLabel);
            }

            if (node.Details is TransportNodeDetails transport)
            {
                var source = PlanningMetadataReader.NormalizeOptional(transport.SourceLocation);
                var destination = PlanningMetadataReader.NormalizeOptional(transport.DestinationLocation);
                if (source is not null)
                {
                    locations.Add(source);
                }

                if (destination is not null)
                {
                    locations.Add(destination);
                }
            }
        }

        return locations;
    }

    private static string DescribeLogisticsTolerance(PlanLogisticsTolerance tolerance)
    {
        return tolerance switch
        {
            PlanLogisticsTolerance.Minimal => "minimal",
            PlanLogisticsTolerance.Flexible => "flexible",
            _ => "balanced"
        };
    }

    private static UseCaseResult<T> Project<T>(
        UseCaseResult<PlanComputationResult> computationResult,
        Func<PlanComputationResult, T> selector,
        string successSummary)
    {
        var traceId = NextTraceId();
        if (!computationResult.IsSuccess || computationResult.Data is null)
        {
            return UseCaseResult<T>.Failure(
                computationResult.Status,
                computationResult.Summary,
                traceId,
                computationResult.Errors,
                computationResult.Warnings);
        }

        return UseCaseResult<T>.Success(
            selector(computationResult.Data),
            successSummary,
            traceId,
            computationResult.Warnings);
    }

    private static int SumReservedSlots(PlanNodeResourceProfile profile)
    {
        return profile.CharacterSlots + profile.BlueprintSlots + profile.BpcSlots + profile.JobSlots;
    }

    private static string NextTraceId() => Guid.NewGuid().ToString("N");

    private sealed record ExecutionBurdenProfile(
        int ManualOperations,
        int TransportLegs,
        int DistinctLocations);
}
