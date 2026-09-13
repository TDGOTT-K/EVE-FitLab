using System.Globalization;
using EdenOS.Application.Market;
using EdenOS.Contracts.Market;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Planning;

internal sealed class IndustryPlanMarketPriceResolver(IMarketFactsService marketFactsService)
{
    public bool TryResolvePrice(IndustryPlanComputationContext context, string typeIdText, out PriceSnapshot snapshot)
    {
        snapshot = default!;
        if (!long.TryParse(typeIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeId))
        {
            context.AddUncertainFact($"Resource type id '{typeIdText}' is not a numeric market type id, so price lookup was skipped.");
            return false;
        }

        var result = marketFactsService.GetPriceSnapshot(new GetPriceSnapshotRequest(typeId, context.MarketLocationId, context.MarketAccountKey));
        if (!result.IsSuccess || result.Data is null)
        {
            var failure = $"Market facts for type '{typeId}' at location '{context.MarketLocationId}' are unavailable: {result.Summary}";
            context.AddUncertainFact(failure);

            if (result.Status is UseCaseStatus.PermissionDenied or UseCaseStatus.DependencyUnavailable)
            {
                context.AddSoftWarning(failure);
            }

            return false;
        }

        snapshot = result.Data;
        context.AddWarnings(result.Warnings);
        if (snapshot.Source.UsesPlaceholderData)
        {
            context.UsedPlaceholderMarketFacts = true;
        }

        return true;
    }
}
