using EdenOS.Contracts.Application;
using EdenOS.Contracts.Market;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class MarketGetKnownTypesUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetKnownTypesRequest, KnownTypesCatalog>
{
    public Task<UseCaseResult<KnownTypesCatalog>> ExecuteAsync(GetKnownTypesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetKnownTypes(request));
    }
}

public sealed class MarketGetItemProfileUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetItemProfileRequest, ItemMarketProfile>
{
    public Task<UseCaseResult<ItemMarketProfile>> ExecuteAsync(GetItemProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetItemProfile(request));
    }
}

public sealed class MarketGetAdjustedPricesUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetAdjustedPricesRequest, AdjustedPriceCatalog>
{
    public Task<UseCaseResult<AdjustedPriceCatalog>> ExecuteAsync(GetAdjustedPricesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetAdjustedPrices(request));
    }
}

public sealed class MarketGetPriceSnapshotUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetPriceSnapshotRequest, PriceSnapshot>
{
    public Task<UseCaseResult<PriceSnapshot>> ExecuteAsync(GetPriceSnapshotRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetPriceSnapshot(request));
    }
}

public sealed class MarketGetMarketOrdersUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetMarketOrdersRequest, MarketOrdersEnvelope>
{
    public Task<UseCaseResult<MarketOrdersEnvelope>> ExecuteAsync(GetMarketOrdersRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetMarketOrders(request));
    }
}

public sealed class MarketGetHistoryWindowUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetHistoryWindowRequest, HistoryWindow>
{
    public Task<UseCaseResult<HistoryWindow>> ExecuteAsync(GetHistoryWindowRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetHistoryWindow(request));
    }
}

public sealed class MarketGetBasicStatisticsUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetBasicStatisticsRequest, BasicStatisticsEnvelope>
{
    public Task<UseCaseResult<BasicStatisticsEnvelope>> ExecuteAsync(GetBasicStatisticsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetBasicStatistics(request));
    }
}

public sealed class MarketGetAccessibleStructuresUseCase(IMarketFactsService marketFactsService)
    : IQueryUseCase<GetAccessibleStructuresRequest, AccessibleStructureCatalog>
{
    public Task<UseCaseResult<AccessibleStructureCatalog>> ExecuteAsync(GetAccessibleStructuresRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketFactsService.GetAccessibleStructures(request));
    }
}
