namespace EdenOS.Contracts.Market;

public sealed record GetKnownTypesRequest(
    IReadOnlyCollection<long> RegionIds,
    string? SearchText = null,
    int Limit = 50);

public sealed record GetItemProfileRequest(long TypeId);

public sealed record GetAdjustedPricesRequest(
    IReadOnlyCollection<long>? TypeIds = null,
    string? SearchText = null,
    int? Limit = null);

public sealed record GetPriceSnapshotRequest(
    long TypeId,
    long LocationId,
    string? AccountKey = null);

public sealed record GetMarketOrdersRequest(
    long TypeId,
    long LocationId,
    int Depth = 5,
    string? AccountKey = null);

public sealed record GetHistoryWindowRequest(
    long TypeId,
    long LocationId,
    int Days = 30,
    string? AccountKey = null);

public sealed record GetBasicStatisticsRequest(
    long TypeId,
    long LocationId,
    string? AccountKey = null);

public sealed record GetAccessibleStructuresRequest(
    string AccountKey,
    bool IncludeStaleGrants = false);
