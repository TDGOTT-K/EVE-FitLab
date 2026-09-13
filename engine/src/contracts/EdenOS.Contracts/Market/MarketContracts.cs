namespace EdenOS.Contracts.Market;

public enum MarketLocationKind
{
    Station,
    Structure
}

public enum MarketFactAnomalySeverity
{
    Info,
    Warning,
    Error
}

public enum MarketOrderSide
{
    Buy,
    Sell
}

public sealed record MarketFactSource(
    string SourceKind,
    bool IsCanonicalSurface,
    bool UsesPlaceholderData);

public sealed record MarketCanonicalFact(
    string SurfaceName,
    string CanonicalFactKind,
    string ScopeKind,
    string ScopeKey,
    string Grain,
    bool IsStitched,
    bool IsReadModel);

public sealed record MarketRawFactReference(
    string PartitionKey,
    string SourceKind,
    string RawFactKind,
    string ScopeKey,
    DateTimeOffset? ObservedAtUtc,
    DateOnly? LastMarketDay,
    bool MatchesCanonicalScope);

public sealed record MarketFactProvenance(
    string BundleVersion,
    string ReadModelKind,
    string ProjectionKind,
    bool UsesDerivedProjection,
    bool UsesStitchedSources,
    IReadOnlyList<MarketRawFactReference> RawFacts);

public sealed record MarketFactFreshness(
    string FreshnessScope,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? ReferenceObservedAtUtc,
    DateOnly? LastMarketDay,
    int? LagMinutes,
    bool IsStale,
    string Summary);

public sealed record MarketFactCoverage(
    string CoverageKind,
    bool IsCompleteForRequest,
    int CoveredEntryCount,
    IReadOnlyList<long> CoveredRegionIds,
    IReadOnlyList<long> CoveredLocationIds,
    IReadOnlyList<string> Notes);

public sealed record MarketFactAnomaly(
    string Code,
    MarketFactAnomalySeverity Severity,
    string Summary,
    bool IsBlocking);

public sealed record KnownTypeSummary(
    long TypeId,
    string Name,
    string MarketGroupName,
    decimal PackagedVolumeM3,
    bool IsPublished);

public sealed record KnownTypesCatalog(
    IReadOnlyList<KnownTypeSummary> Types,
    IReadOnlyList<long> RegionIds,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record MarketLocationSummary(
    long LocationId,
    string Name,
    MarketLocationKind Kind,
    bool RequiresAccountGrant,
    bool HasCurrentSnapshot);

public sealed record ItemMarketProfile(
    long TypeId,
    string Name,
    string Description,
    string MarketGroupName,
    decimal VolumeM3,
    decimal PackagedVolumeM3,
    bool IsPublished,
    IReadOnlyList<MarketLocationSummary> KnownMarkets,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record PriceSnapshot(
    long TypeId,
    string ItemName,
    long LocationId,
    string LocationName,
    MarketLocationKind LocationKind,
    decimal LowestSellPrice,
    decimal HighestBuyPrice,
    decimal MidPrice,
    decimal TopFiveSellDepthUnits,
    decimal TopFiveBuyDepthUnits,
    int SellOrderCount,
    int BuyOrderCount,
    DateTimeOffset ObservedAtUtc,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record MarketOrderEntry(
    long OrderId,
    decimal UnitPrice,
    long RemainingVolume,
    long MinVolume,
    string Range,
    DateTimeOffset IssuedAtUtc);

public sealed record MarketOrdersEnvelope(
    long TypeId,
    string ItemName,
    long LocationId,
    string LocationName,
    MarketLocationKind LocationKind,
    int RequestedDepth,
    DateTimeOffset ObservedAtUtc,
    int TotalSellOrderCount,
    int TotalBuyOrderCount,
    IReadOnlyList<MarketOrderEntry> SellOrders,
    IReadOnlyList<MarketOrderEntry> BuyOrders,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record HistoryPoint(
    DateOnly Day,
    decimal AveragePrice,
    decimal HighestPrice,
    decimal LowestPrice,
    long Volume);

public sealed record HistoryWindow(
    long TypeId,
    string ItemName,
    long LocationId,
    string LocationName,
    int RequestedDays,
    IReadOnlyList<HistoryPoint> Points,
    DateTimeOffset LastObservedAtUtc,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record RollingWindowStatistics(
    int WindowDays,
    decimal AveragePrice,
    long AverageDailyVolume,
    decimal VolatilityPercent,
    decimal P10Price,
    decimal MedianPrice,
    decimal P90Price,
    decimal ExitQualityScore);

public sealed record BasicStatisticsEnvelope(
    long TypeId,
    string ItemName,
    long LocationId,
    string LocationName,
    RollingWindowStatistics SevenDay,
    RollingWindowStatistics TwentyOneDay,
    RollingWindowStatistics SixtyDay,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record AdjustedPriceEntry(
    long TypeId,
    string ItemName,
    decimal AdjustedPrice,
    decimal? AveragePrice);

public sealed record AdjustedPriceCatalog(
    IReadOnlyList<AdjustedPriceEntry> Prices,
    DateTimeOffset? ObservedAtUtc,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);

public sealed record AccessibleStructure(
    long StructureId,
    string Name,
    long SolarSystemId,
    string SolarSystemName,
    string GrantSource,
    DateTimeOffset LastVerifiedAtUtc,
    bool IsMarketReadEnabled,
    bool IsStale);

public sealed record AccessibleStructureCatalog(
    string AccountKey,
    IReadOnlyList<AccessibleStructure> Structures,
    MarketFactSource Source,
    MarketCanonicalFact Canonical,
    MarketFactFreshness Freshness,
    MarketFactCoverage Coverage,
    MarketFactProvenance Provenance,
    IReadOnlyList<MarketFactAnomaly> Anomalies);
