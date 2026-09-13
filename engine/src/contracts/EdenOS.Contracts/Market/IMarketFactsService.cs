using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Market;

public interface IMarketFactsService
{
    UseCaseResult<KnownTypesCatalog> GetKnownTypes(GetKnownTypesRequest request);

    UseCaseResult<ItemMarketProfile> GetItemProfile(GetItemProfileRequest request);

    UseCaseResult<AdjustedPriceCatalog> GetAdjustedPrices(GetAdjustedPricesRequest request);

    UseCaseResult<PriceSnapshot> GetPriceSnapshot(GetPriceSnapshotRequest request);

    UseCaseResult<MarketOrdersEnvelope> GetMarketOrders(GetMarketOrdersRequest request);

    UseCaseResult<HistoryWindow> GetHistoryWindow(GetHistoryWindowRequest request);

    UseCaseResult<BasicStatisticsEnvelope> GetBasicStatistics(GetBasicStatisticsRequest request);

    UseCaseResult<AccessibleStructureCatalog> GetAccessibleStructures(GetAccessibleStructuresRequest request);
}
