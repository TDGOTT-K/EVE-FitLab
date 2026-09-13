using System.Globalization;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal sealed class IndustryPlanCostModelBuilder(
    IndustrialRecipeResolver recipeResolver,
    IndustryPlanMarketPriceResolver marketPriceResolver)
{
    private const decimal DefaultNpcFacilityTaxRate = 0.0025m;
    private const decimal DefaultSccSurchargeRate = 0.04m;
    private const decimal DefaultOmegaAlphaCloneRate = 0m;
    private const decimal DefaultFacilityBonusMultiplier = 1m;
    private static readonly LocalIndustryReferenceCatalog IndustryReferenceCatalog = LocalIndustryReferenceCatalog.LoadDefaultOrEmpty();

    public List<PlanCostLineItem> BuildCostAndRevenue(IndustryPlanComputationContext context)
    {
        var lineItems = new List<PlanCostLineItem>();

        foreach (var node in context.Plan.Nodes)
        {
            switch (node.Details)
            {
                case TradeNodeDetails trade:
                    lineItems.AddRange(BuildTradeLineItems(context, node, trade));
                    break;
                case ProductionNodeDetails production:
                    lineItems.Add(BuildIndustryJobCostLine(
                        context,
                        node,
                        "industry_job",
                        ResolveIndustryJobCost(context, node, "Manufacturing installation fee"),
                        production.Activity == ProductionActivityKind.Manufacturing));
                    lineItems.AddRange(BuildIndustrialRecipeGapLineItems(context, node));
                    break;
                case ReactionNodeDetails:
                    lineItems.Add(BuildIndustryJobCostLine(
                        context,
                        node,
                        "industry_job",
                        ResolveIndustryJobCost(context, node, "Reaction installation fee"),
                        true));
                    lineItems.AddRange(BuildIndustrialRecipeGapLineItems(context, node));
                    break;
                case CopyOrInventionNodeDetails copyOrInvention:
                    lineItems.Add(BuildIndustryJobCostLine(
                        context,
                        node,
                        "industry_job",
                        ResolveIndustryJobCost(
                            context,
                            node,
                            copyOrInvention.Activity == CopyOrInventionActivity.Invention
                                ? "Invention installation fee"
                                : "Copy installation fee"),
                        true));
                    lineItems.AddRange(BuildIndustrialRecipeGapLineItems(context, node));
                    break;
                case TransportNodeDetails:
                    var transportCost = PlanningMetadataReader.ReadDecimal(node.Metadata, "transport_cost_isk") ?? 250000m;
                    lineItems.Add(new PlanCostLineItem
                    {
                        Category = "logistics",
                        Title = "Transport handling estimate",
                        AmountIsk = transportCost,
                        NodeId = node.NodeId,
                        UsesMarketFacts = false,
                        UsesPlaceholderFacts = false
                    });
                    context.AddCostTrace(
                        $"Logistics '{node.Title}': transport handling estimate = {transportCost:N2} ISK.");
                    break;
            }
        }

        if (!lineItems.Any(item => item.Category == "revenue") && context.Plan.Goal is not null)
        {
            if (marketPriceResolver.TryResolvePrice(context, context.Plan.Goal.TargetTypeId, out var goalSnapshot))
            {
                lineItems.Add(new PlanCostLineItem
                {
                    Category = "revenue",
                    Title = $"Goal output value for '{context.Plan.Goal.TargetName}'",
                    AmountIsk = Math.Round(goalSnapshot.HighestBuyPrice * context.Plan.Goal.Quantity, 2),
                    Quantity = context.Plan.Goal.Quantity,
                    ResourceTypeId = context.Plan.Goal.TargetTypeId,
                    UsesMarketFacts = true,
                    UsesPlaceholderFacts = goalSnapshot.Source.UsesPlaceholderData
                });
                context.AddCostTrace(
                    $"Revenue fallback for goal '{context.Plan.Goal.TargetName}': {context.Plan.Goal.Quantity:N2} x {goalSnapshot.HighestBuyPrice:N2} ISK = {Math.Round(goalSnapshot.HighestBuyPrice * context.Plan.Goal.Quantity, 2):N2} ISK.");
            }
            else
            {
                context.AddUncertainFact($"Goal target '{context.Plan.Goal.TargetName}' could not be priced from the market surface.");
            }
        }

        return lineItems;
    }

    private IEnumerable<PlanCostLineItem> BuildTradeLineItems(IndustryPlanComputationContext context, PlanNode node, TradeNodeDetails trade)
    {
        var links = trade.TradeMode == PlanTradeMode.Sale
            ? context.Plan.Links.Where(link => link.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase)).ToArray()
            : context.Plan.Links.Where(link => link.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (links.Length == 0)
        {
            var fallback = BuildMetadataTradeLineItem(context, node, trade);
            if (fallback is not null)
            {
                yield return fallback;
            }

            yield break;
        }

        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link.ResourceTypeId))
            {
                context.AddUncertainFact($"Trade node '{node.Title}' has a link without resource_type_id, so it cannot be priced precisely.");
                continue;
            }

            var quantity = link.Quantity ?? PlanningMetadataReader.ReadDecimal(node.Metadata, "market_quantity") ?? context.Plan.Goal?.Quantity ?? 1m;
            if (quantity <= 0)
            {
                context.AddUncertainFact($"Trade node '{node.Title}' has a non-positive quantity for resource '{link.ResourceTypeId}'.");
                continue;
            }

            var explicitUnitPrice = trade.UnitPricePreference ?? PlanningMetadataReader.ReadDecimal(node.Metadata, "unit_price_isk");
            if (explicitUnitPrice.HasValue)
            {
                yield return new PlanCostLineItem
                {
                    Category = trade.TradeMode == PlanTradeMode.Sale ? "revenue" : "purchase",
                    Title = BuildTradeTitle(node, trade, link.ResourceTypeId),
                    AmountIsk = Math.Round(explicitUnitPrice.Value * quantity, 2),
                    NodeId = node.NodeId,
                    ResourceTypeId = link.ResourceTypeId,
                    Quantity = quantity,
                    UsesMarketFacts = false,
                    UsesPlaceholderFacts = false
                };
                context.AddCostTrace(
                    $"{BuildTradeTitle(node, trade, link.ResourceTypeId)}: {quantity:N2} x {explicitUnitPrice.Value:N2} ISK = {Math.Round(explicitUnitPrice.Value * quantity, 2):N2} ISK (explicit unit price).");

                continue;
            }

            if (!marketPriceResolver.TryResolvePrice(context, link.ResourceTypeId, out var snapshot))
            {
                context.AddUncertainFact($"Trade node '{node.Title}' could not resolve market pricing for resource '{link.ResourceTypeId}'.");
                continue;
            }

            var unitPrice = trade.TradeMode == PlanTradeMode.Sale
                ? snapshot.HighestBuyPrice
                : snapshot.LowestSellPrice;

            yield return new PlanCostLineItem
            {
                Category = trade.TradeMode == PlanTradeMode.Sale ? "revenue" : "purchase",
                Title = BuildTradeTitle(node, trade, link.ResourceTypeId),
                AmountIsk = Math.Round(unitPrice * quantity, 2),
                NodeId = node.NodeId,
                ResourceTypeId = link.ResourceTypeId,
                Quantity = quantity,
                UsesMarketFacts = true,
                UsesPlaceholderFacts = snapshot.Source.UsesPlaceholderData
            };
            context.AddCostTrace(
                $"{BuildTradeTitle(node, trade, link.ResourceTypeId)}: {quantity:N2} x {unitPrice:N2} ISK = {Math.Round(unitPrice * quantity, 2):N2} ISK ({DescribeTradePricingSource(trade.TradeMode)}).");
        }
    }

    private PlanCostLineItem? BuildMetadataTradeLineItem(IndustryPlanComputationContext context, PlanNode node, TradeNodeDetails trade)
    {
        if (!PlanningMetadataReader.TryReadString(node.Metadata, "market_type_id", out var marketTypeId))
        {
            context.AddUncertainFact($"Trade node '{node.Title}' has no linked resource and no market_type_id metadata.");
            return null;
        }

        var quantity = PlanningMetadataReader.ReadDecimal(node.Metadata, "market_quantity") ?? context.Plan.Goal?.Quantity ?? 1m;
        if (quantity <= 0)
        {
            context.AddUncertainFact($"Trade node '{node.Title}' has a non-positive market quantity.");
            return null;
        }

        var explicitUnitPrice = trade.UnitPricePreference ?? PlanningMetadataReader.ReadDecimal(node.Metadata, "unit_price_isk");
        if (explicitUnitPrice.HasValue)
        {
            var amount = Math.Round(explicitUnitPrice.Value * quantity, 2);
            context.AddCostTrace(
                $"{BuildTradeTitle(node, trade, marketTypeId)}: {quantity:N2} x {explicitUnitPrice.Value:N2} ISK = {amount:N2} ISK (explicit unit price).");
            return new PlanCostLineItem
            {
                Category = trade.TradeMode == PlanTradeMode.Sale ? "revenue" : "purchase",
                Title = BuildTradeTitle(node, trade, marketTypeId),
                AmountIsk = amount,
                NodeId = node.NodeId,
                ResourceTypeId = marketTypeId,
                Quantity = quantity,
                UsesMarketFacts = false,
                UsesPlaceholderFacts = false
            };
        }

        if (!marketPriceResolver.TryResolvePrice(context, marketTypeId, out var snapshot))
        {
            context.AddUncertainFact($"Trade node '{node.Title}' could not resolve market pricing for metadata resource '{marketTypeId}'.");
            return null;
        }

        var unitPrice = trade.TradeMode == PlanTradeMode.Sale
            ? snapshot.HighestBuyPrice
            : snapshot.LowestSellPrice;

        var amountIsk = Math.Round(unitPrice * quantity, 2);
        context.AddCostTrace(
            $"{BuildTradeTitle(node, trade, marketTypeId)}: {quantity:N2} x {unitPrice:N2} ISK = {amountIsk:N2} ISK ({DescribeTradePricingSource(trade.TradeMode)}).");
        return new PlanCostLineItem
        {
            Category = trade.TradeMode == PlanTradeMode.Sale ? "revenue" : "purchase",
            Title = BuildTradeTitle(node, trade, marketTypeId),
            AmountIsk = amountIsk,
            NodeId = node.NodeId,
            ResourceTypeId = marketTypeId,
            Quantity = quantity,
            UsesMarketFacts = true,
            UsesPlaceholderFacts = snapshot.Source.UsesPlaceholderData
        };
    }

    private static string BuildTradeTitle(PlanNode node, TradeNodeDetails trade, string resourceTypeId)
    {
        var action = trade.TradeMode switch
        {
            PlanTradeMode.Sale => "Sell",
            PlanTradeMode.PrivateExchange => "Private exchange",
            PlanTradeMode.OutsourceInput => "Outsource",
            _ => "Buy"
        };

        return $"{action} resource '{resourceTypeId}' via '{node.Title}'";
    }

    private static PlanCostLineItem BuildIndustryJobCostLine(
        IndustryPlanComputationContext context,
        PlanNode node,
        string category,
        IndustryJobCostResolution resolution,
        bool explainReservation)
    {
        var explanationSuffix = explainReservation && SumReservedSlots(node.ResourceProfile) == 0
            ? " (no explicit slot reservation declared)"
            : string.Empty;

        context.AddCostTrace(resolution.TraceExplanation);

        return new PlanCostLineItem
        {
            Category = category,
            Title = $"{resolution.Title}{explanationSuffix}",
            AmountIsk = resolution.AmountIsk,
            NodeId = node.NodeId,
            UsesMarketFacts = resolution.UsesMarketFacts,
            UsesPlaceholderFacts = resolution.UsesPlaceholderFacts
        };
    }

    private static Dictionary<long, decimal> BuildIncomingResourceQuantities(IndustryPlanComputationContext context, PlanNode node)
    {
        var quantities = new Dictionary<long, decimal>();

        foreach (var link in context.Plan.Links.Where(link => link.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase)))
        {
            if (link.Kind is PlanLinkKind.Reservation or PlanLinkKind.Dependency)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(link.ResourceTypeId) ||
                !long.TryParse(link.ResourceTypeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var resourceTypeId))
            {
                continue;
            }

            if (!link.Quantity.HasValue || link.Quantity.Value <= 0m)
            {
                continue;
            }

            quantities[resourceTypeId] = quantities.GetValueOrDefault(resourceTypeId, 0m) + link.Quantity.Value;
        }

        return quantities;
    }

    private IEnumerable<PlanCostLineItem> BuildIndustrialRecipeGapLineItems(IndustryPlanComputationContext context, PlanNode node)
    {
        var recipe = recipeResolver.ResolveRecipeActivity(node);
        if (recipe is null)
        {
            if (node.Kind is PlanNodeKind.Production or PlanNodeKind.Reaction or PlanNodeKind.CopyOrInvention)
            {
                var semanticLabel = DescribeNodeSemantic(node);
                context.AddUncertainFact($"{semanticLabel} '{node.Title}' could not be matched to a real SDE recipe, so only explicit graph links contribute material pricing.");
            }

            return [];
        }

        context.RecipeBackedNodeIds.Add(node.NodeId);
        context.RecordRecipeSemanticClass(node.NodeId, recipe.PlannerSemanticClass);
        if (recipe.Materials.Count == 0)
        {
            return [];
        }

        var recipeSemanticLabel = recipeResolver.DescribePlannerSemanticClass(recipe.PlannerSemanticClass);

        var plannedRuns = recipeResolver.ResolvePlannedRuns(context, node, recipe);
        if (plannedRuns <= 0m)
        {
            context.AddUncertainFact($"{recipeSemanticLabel} '{node.Title}' resolved to a recipe but its effective run count is non-positive.");
            return [];
        }

        var suppliedQuantities = BuildIncomingResourceQuantities(context, node);
        var shortages = recipe.Materials
            .Select(material =>
            {
                var requiredQuantity = recipeResolver.ApplyMaterialEfficiency(context, node, material.QuantityPerRun, plannedRuns);
                var suppliedQuantity = suppliedQuantities.GetValueOrDefault(material.MaterialTypeId, 0m);
                var missingQuantity = Math.Max(0m, requiredQuantity - suppliedQuantity);
                return new
                {
                    Material = material,
                    RequiredQuantity = requiredQuantity,
                    SuppliedQuantity = suppliedQuantity,
                    MissingQuantity = missingQuantity
                };
            })
            .Where(entry => entry.MissingQuantity > 0m)
            .ToArray();

        if (shortages.Length == 0)
        {
            return [];
        }

        if (!context.Plan.Preferences.AllowMarketPurchases)
        {
            var labels = shortages
                .Select(entry => $"{recipeResolver.ResolveTypeName(entry.Material.MaterialTypeId)} x {entry.MissingQuantity:N0}")
                .ToArray();
            context.AddHardConflict(
                $"{recipeSemanticLabel} '{node.Title}' is missing recipe inputs from the current graph ({string.Join(", ", labels)}), but market purchasing is forbidden by plan preferences.",
                node.NodeId);
            return [];
        }

        var lineItems = new List<PlanCostLineItem>(shortages.Length);
        foreach (var shortage in shortages)
        {
            var materialTypeIdText = shortage.Material.MaterialTypeId.ToString(CultureInfo.InvariantCulture);
            if (!marketPriceResolver.TryResolvePrice(context, materialTypeIdText, out var snapshot))
            {
                context.AddUncertainFact(
                    $"{recipeSemanticLabel} '{node.Title}' matched real SDE recipe inputs, but market pricing for material '{materialTypeIdText}' is unavailable.");
                continue;
            }

            lineItems.Add(new PlanCostLineItem
            {
                Category = "purchase",
                Title = $"{recipeSemanticLabel} gap for '{node.Title}': {recipeResolver.ResolveTypeName(shortage.Material.MaterialTypeId)}",
                AmountIsk = Math.Round(snapshot.LowestSellPrice * shortage.MissingQuantity, 2),
                NodeId = node.NodeId,
                ResourceTypeId = materialTypeIdText,
                Quantity = shortage.MissingQuantity,
                UsesMarketFacts = true,
                UsesPlaceholderFacts = snapshot.Source.UsesPlaceholderData
            });
            context.AddCostTrace(
                $"{recipeSemanticLabel} gap for '{node.Title}': {recipeResolver.ResolveTypeName(shortage.Material.MaterialTypeId)} {shortage.MissingQuantity:N2} x {snapshot.LowestSellPrice:N2} ISK = {Math.Round(snapshot.LowestSellPrice * shortage.MissingQuantity, 2):N2} ISK (market buy-in estimate).");
        }

        return lineItems;
    }

    private IndustryJobCostResolution ResolveIndustryJobCost(IndustryPlanComputationContext context, PlanNode node, string baseTitle)
    {
        var semanticLabel = DescribeNodeSemantic(node);
        var explicitOverride = PlanningMetadataReader.ReadDecimal(node.Metadata, "job_cost_isk");
        if (explicitOverride.HasValue)
        {
            var explicitAmount = Math.Round(Math.Max(0m, explicitOverride.Value), 2);
            return new IndustryJobCostResolution(
                $"{baseTitle} (explicit override)",
                explicitAmount,
                UsesMarketFacts: false,
                UsesPlaceholderFacts: false,
                TraceExplanation: $"{semanticLabel} '{node.Title}': explicit job-cost override = {explicitAmount:N2} ISK.");
        }

        var resolvedEiv = ResolveEstimatedItemValue(context, node);
        if (resolvedEiv is null)
        {
            context.AddUncertainFact(
                $"{semanticLabel} '{node.Title}' is missing estimated item value input, so installation fee was left at 0 until real EIV data is supplied.");
            return new IndustryJobCostResolution(
                $"{baseTitle} (missing EIV input)",
                0m,
                UsesMarketFacts: false,
                UsesPlaceholderFacts: false,
                TraceExplanation: $"{semanticLabel} '{node.Title}': missing EIV input, so installation fee = 0 ISK.");
        }

        var systemCostIndex =
            ReadFirstDecimal(node.Metadata, "industry_system_cost_index", "system_cost_index")
            ?? ResolveReferenceSystemCostIndex(context, node);
        if (!systemCostIndex.HasValue)
        {
            context.AddUncertainFact(
                $"Industrial node '{node.Title}' has no system cost index, so installation fee currently applies only tax/surcharge components.");
        }

        var facilityTaxRate = ReadFirstDecimal(node.Metadata, "industry_facility_tax_rate", "facility_tax_rate")
            ?? DefaultNpcFacilityTaxRate;
        var sccSurchargeRate = ReadFirstDecimal(node.Metadata, "industry_scc_surcharge_rate", "scc_surcharge_rate")
            ?? DefaultSccSurchargeRate;
        var alphaCloneRate = ReadFirstDecimal(node.Metadata, "industry_alpha_clone_rate", "alpha_clone_rate")
            ?? DefaultOmegaAlphaCloneRate;
        var facilityBonusMultiplier = ReadFirstDecimal(node.Metadata, "industry_facility_bonus_multiplier", "facility_bonus_multiplier")
            ?? DefaultFacilityBonusMultiplier;

        var effectiveRate =
            (Math.Max(0m, systemCostIndex ?? 0m) * Math.Max(0m, facilityBonusMultiplier))
            + Math.Max(0m, facilityTaxRate)
            + Math.Max(0m, sccSurchargeRate)
            + Math.Max(0m, alphaCloneRate);

        var amount = Math.Round(resolvedEiv.AmountIsk * effectiveRate, 2);
        var systemIndexNote = systemCostIndex.HasValue ? "SCI-aware" : "tax-floor-only";
        var traceExplanation =
            $"{semanticLabel} '{node.Title}': EIV {resolvedEiv.AmountIsk:N2} ISK x rate {effectiveRate:P2} "
            + $"(SCI {(systemCostIndex ?? 0m):P2} x facility bonus {facilityBonusMultiplier:N2} + facility tax {facilityTaxRate:P2} + SCC {sccSurchargeRate:P2} + alpha {alphaCloneRate:P2}) = {amount:N2} ISK.";
        return new IndustryJobCostResolution(
            $"{baseTitle} ({systemIndexNote} formula skeleton)",
            amount,
            resolvedEiv.UsesMarketFacts,
            resolvedEiv.UsesPlaceholderFacts,
            traceExplanation);
    }

    private ResolvedEstimatedItemValue? ResolveEstimatedItemValue(IndustryPlanComputationContext context, PlanNode node)
    {
        var explicitValue = ReadFirstDecimal(node.Metadata, "estimated_item_value_isk", "industry_eiv_isk", "job_value_base_isk");
        if (explicitValue.HasValue)
        {
            return new ResolvedEstimatedItemValue(Math.Max(0m, explicitValue.Value), false, false);
        }

        var recipe = recipeResolver.ResolveRecipeActivity(node);
        if (recipe is null || recipe.Materials.Count == 0)
        {
            return null;
        }

        var recipeSemanticLabel = recipeResolver.DescribePlannerSemanticClass(recipe.PlannerSemanticClass);

        var plannedRuns = recipeResolver.ResolvePlannedRuns(context, node, recipe);
        if (plannedRuns <= 0m)
        {
            return null;
        }

        var estimatedValue = 0m;
        var usedPlaceholderFacts = false;
        var usedMarketMidPriceFallback = false;
        foreach (var material in recipe.Materials)
        {
            var requiredQuantity = recipeResolver.ApplyMaterialEfficiency(context, node, material.QuantityPerRun, plannedRuns);
            if (requiredQuantity <= 0m)
            {
                continue;
            }

            if (IndustryReferenceCatalog.TryGetAdjustedPrice(material.MaterialTypeId, out var adjustedPrice))
            {
                estimatedValue += adjustedPrice * requiredQuantity;
                continue;
            }

            if (!marketPriceResolver.TryResolvePrice(context, material.MaterialTypeId.ToString(CultureInfo.InvariantCulture), out var snapshot))
            {
                context.AddUncertainFact(
                    $"{recipeSemanticLabel} '{node.Title}' could not derive EIV because adjusted-price input for material '{material.MaterialTypeId}' is unavailable.");
                return null;
            }

            // When the local adjusted-price reference lacks a material, MidPrice is the closest available EIV proxy.
            estimatedValue += snapshot.MidPrice * requiredQuantity;
            usedPlaceholderFacts |= snapshot.Source.UsesPlaceholderData;
            usedMarketMidPriceFallback = true;
        }

        if (estimatedValue <= 0m)
        {
            return null;
        }

        if (usedMarketMidPriceFallback)
        {
            context.AddUncertainFact(
                $"{recipeSemanticLabel} '{node.Title}' derived EIV from current market mid prices because adjusted-price coverage was missing for one or more recipe materials.");
        }

        return new ResolvedEstimatedItemValue(Math.Round(estimatedValue, 2), usedMarketMidPriceFallback, usedPlaceholderFacts);
    }

    private static decimal? ResolveReferenceSystemCostIndex(IndustryPlanComputationContext context, PlanNode node)
    {
        if (!PlanningMetadataReader.TryReadLong(node.Metadata, "industry_solar_system_id", out var solarSystemId) &&
            !PlanningMetadataReader.TryReadLong(node.Metadata, "solar_system_id", out solarSystemId))
        {
            return null;
        }

        var activityKind = ResolveIndustryActivityKind(node);
        if (activityKind is null)
        {
            return null;
        }

        if (!IndustryReferenceCatalog.TryGetSystemCostIndex(solarSystemId, activityKind, out var costIndex))
        {
            context.AddUncertainFact(
                $"Industrial node '{node.Title}' references solar system '{solarSystemId}', but no local cost index snapshot exists for activity '{activityKind}'.");
            return null;
        }

        return costIndex;
    }

    private static string? ResolveIndustryActivityKind(PlanNode node)
    {
        return node.Details switch
        {
            ProductionNodeDetails => "manufacturing",
            ReactionNodeDetails => "reaction",
            CopyOrInventionNodeDetails copyOrInvention when copyOrInvention.Activity == CopyOrInventionActivity.Copy => "copying",
            CopyOrInventionNodeDetails copyOrInvention when copyOrInvention.Activity == CopyOrInventionActivity.Invention => "invention",
            _ => null
        };
    }

    private static decimal? ReadFirstDecimal(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = PlanningMetadataReader.ReadDecimal(metadata, key);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    private static int SumReservedSlots(PlanNodeResourceProfile profile)
    {
        return profile.CharacterSlots + profile.BlueprintSlots + profile.BpcSlots + profile.JobSlots;
    }

    private static string DescribeTradePricingSource(PlanTradeMode tradeMode)
    {
        return tradeMode == PlanTradeMode.Sale
            ? "market sale-side estimate using highest buy price"
            : "market buy-in estimate using lowest sell price";
    }

    private string DescribeNodeSemantic(PlanNode node)
    {
        return recipeResolver.DescribePlannerSemanticClass(recipeResolver.ResolvePlannerSemanticClass(node));
    }
}

internal sealed record IndustryJobCostResolution(
    string Title,
    decimal AmountIsk,
    bool UsesMarketFacts,
    bool UsesPlaceholderFacts,
    string TraceExplanation);

internal sealed record ResolvedEstimatedItemValue(
    decimal AmountIsk,
    bool UsesMarketFacts,
    bool UsesPlaceholderFacts);
