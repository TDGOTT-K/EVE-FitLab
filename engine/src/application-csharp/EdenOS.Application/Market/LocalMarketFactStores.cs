using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

internal interface IMarketSnapshotStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    IReadOnlyList<long> ListCoveredLocations(long typeId);

    bool TryGetSnapshot(long typeId, long locationId, out MarketSnapshotRecord snapshot);

    DateTimeOffset? GetLastObservedAtUtc(long locationId);
}

internal interface IMarketHistoryStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    bool TryGetHistory(long typeId, long locationId, out IReadOnlyList<HistoryPoint> points);
}

internal interface IMarketStatisticsStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    bool TryGetProjection(long typeId, long locationId, out MarketStatisticsProjectionRecord projection);
}

internal interface IMarketOrderStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    bool TryGetOrderSnapshot(long typeId, long locationId, out MarketOrderSnapshotRecord snapshot);

    DateTimeOffset? GetLastObservedAtUtc(long locationId);
}

internal sealed record MarketSnapshotRecord(
    long TypeId,
    long LocationId,
    decimal LowestSellPrice,
    decimal HighestBuyPrice,
    decimal TopFiveSellDepthUnits,
    decimal TopFiveBuyDepthUnits,
    int SellOrderCount,
    int BuyOrderCount,
    DateTimeOffset ObservedAtUtc);

internal sealed record MarketStatisticsProjectionRecord(
    long TypeId,
    long LocationId,
    DateTimeOffset DerivedFromLastObservedAtUtc,
    RollingWindowStatistics SevenDay,
    RollingWindowStatistics TwentyOneDay,
    RollingWindowStatistics SixtyDay);

internal sealed record MarketOrderRecord(
    long OrderId,
    MarketOrderSide Side,
    decimal UnitPrice,
    long RemainingVolume,
    long MinVolume,
    string Range,
    DateTimeOffset IssuedAtUtc);

internal sealed record MarketOrderSnapshotRecord(
    long TypeId,
    long LocationId,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<MarketOrderRecord> SellOrders,
    IReadOnlyList<MarketOrderRecord> BuyOrders);

internal sealed class LocalMarketSnapshotStore : IMarketSnapshotStore
{
    private readonly MarketLocalFactCatalog _catalog;

    public LocalMarketSnapshotStore()
        : this(MarketLocalFactCatalog.LoadDefaultOrBootstrapFallback())
    {
    }

    internal LocalMarketSnapshotStore(string marketFactsDirectoryPath)
        : this(MarketLocalFactCatalog.LoadFromDirectory(marketFactsDirectoryPath))
    {
    }

    internal LocalMarketSnapshotStore(MarketLocalFactCatalog catalog)
    {
        _catalog = catalog;
    }

    public string BundleVersion => _catalog.BundleVersion;

    public MarketFactSource Source => _catalog.SnapshotSource;

    public IReadOnlyList<long> ListCoveredLocations(long typeId)
    {
        return _catalog.Snapshots.Values
            .Where(snapshot => snapshot.TypeId == typeId)
            .Select(snapshot => snapshot.LocationId)
            .Distinct()
            .OrderBy(locationId => locationId)
            .ToArray();
    }

    public bool TryGetSnapshot(long typeId, long locationId, out MarketSnapshotRecord snapshot)
    {
        return _catalog.Snapshots.TryGetValue((typeId, locationId), out snapshot!);
    }

    public DateTimeOffset? GetLastObservedAtUtc(long locationId)
    {
        var lastObserved = _catalog.Snapshots.Values
            .Where(snapshot => snapshot.LocationId == locationId)
            .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
            .Select(snapshot => (DateTimeOffset?)snapshot.ObservedAtUtc)
            .FirstOrDefault();

        return lastObserved;
    }
}

internal sealed class LocalMarketHistoryStore : IMarketHistoryStore
{
    private readonly MarketLocalFactCatalog _catalog;

    public LocalMarketHistoryStore()
        : this(MarketLocalFactCatalog.LoadDefaultOrBootstrapFallback())
    {
    }

    internal LocalMarketHistoryStore(string marketFactsDirectoryPath)
        : this(MarketLocalFactCatalog.LoadFromDirectory(marketFactsDirectoryPath))
    {
    }

    internal LocalMarketHistoryStore(MarketLocalFactCatalog catalog)
    {
        _catalog = catalog;
    }

    public string BundleVersion => _catalog.BundleVersion;

    public MarketFactSource Source => _catalog.HistorySource;

    public bool TryGetHistory(long typeId, long locationId, out IReadOnlyList<HistoryPoint> points)
    {
        return _catalog.HistoryWindows.TryGetValue((typeId, locationId), out points!);
    }
}

internal sealed class LocalMarketStatisticsStore : IMarketStatisticsStore
{
    private readonly MarketLocalFactCatalog _catalog;

    public LocalMarketStatisticsStore()
        : this(MarketLocalFactCatalog.LoadDefaultOrBootstrapFallback())
    {
    }

    internal LocalMarketStatisticsStore(string marketFactsDirectoryPath)
        : this(MarketLocalFactCatalog.LoadFromDirectory(marketFactsDirectoryPath))
    {
    }

    internal LocalMarketStatisticsStore(MarketLocalFactCatalog catalog)
    {
        _catalog = catalog;
    }

    public string BundleVersion => _catalog.BundleVersion;

    public MarketFactSource Source => _catalog.StatisticsSource;

    public bool TryGetProjection(long typeId, long locationId, out MarketStatisticsProjectionRecord projection)
    {
        return _catalog.StatisticsProjections.TryGetValue((typeId, locationId), out projection!);
    }
}

internal sealed class LocalMarketOrderStore : IMarketOrderStore
{
    private readonly MarketLocalFactCatalog _catalog;

    public LocalMarketOrderStore()
        : this(MarketLocalFactCatalog.LoadDefaultOrBootstrapFallback())
    {
    }

    internal LocalMarketOrderStore(string marketFactsDirectoryPath)
        : this(MarketLocalFactCatalog.LoadFromDirectory(marketFactsDirectoryPath))
    {
    }

    internal LocalMarketOrderStore(MarketLocalFactCatalog catalog)
    {
        _catalog = catalog;
    }

    public string BundleVersion => _catalog.BundleVersion;

    public MarketFactSource Source => _catalog.OrdersSource;

    public bool TryGetOrderSnapshot(long typeId, long locationId, out MarketOrderSnapshotRecord snapshot)
    {
        return _catalog.OrderSnapshots.TryGetValue((typeId, locationId), out snapshot!);
    }

    public DateTimeOffset? GetLastObservedAtUtc(long locationId)
    {
        var lastObserved = _catalog.OrderSnapshots.Values
            .Where(snapshot => snapshot.LocationId == locationId)
            .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
            .Select(snapshot => (DateTimeOffset?)snapshot.ObservedAtUtc)
            .FirstOrDefault();

        return lastObserved;
    }
}

internal sealed class MarketLocalFactCatalog
{
    private MarketLocalFactCatalog(
        string bundleVersion,
        MarketFactSource snapshotSource,
        MarketFactSource historySource,
        MarketFactSource statisticsSource,
        MarketFactSource ordersSource,
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots,
        IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> historyWindows,
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> statisticsProjections,
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord> orderSnapshots)
    {
        BundleVersion = bundleVersion;
        SnapshotSource = snapshotSource;
        HistorySource = historySource;
        StatisticsSource = statisticsSource;
        OrdersSource = ordersSource;
        Snapshots = snapshots;
        HistoryWindows = historyWindows;
        StatisticsProjections = statisticsProjections;
        OrderSnapshots = orderSnapshots;
    }

    public string BundleVersion { get; }

    public MarketFactSource SnapshotSource { get; }

    public MarketFactSource HistorySource { get; }

    public MarketFactSource StatisticsSource { get; }

    public MarketFactSource OrdersSource { get; }

    public IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> Snapshots { get; }

    public IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> HistoryWindows { get; }

    public IReadOnlyDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> StatisticsProjections { get; }

    public IReadOnlyDictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord> OrderSnapshots { get; }

    public static MarketLocalFactCatalog LoadDefaultOrBootstrapFallback()
    {
        if (!MarketFactsDataPaths.TryResolveDefaultLayout(out var layout))
        {
            return CreateBootstrapFallback();
        }

        return LoadFromLayout(layout);
    }

    internal static MarketLocalFactCatalog LoadFromDirectory(string marketFactsDirectoryPath)
    {
        return LoadFromLayout(MarketFactsDataPaths.ResolveFromDirectory(marketFactsDirectoryPath));
    }

    internal static MarketLocalFactCatalog LoadFromLayout(MarketFactsDataLayout layout)
    {
        return new MarketLocalFactCatalog(
            layout.BundleVersion,
            new MarketFactSource("local-market-snapshot-store", IsCanonicalSurface: true, UsesPlaceholderData: false),
            new MarketFactSource("local-market-history-store", IsCanonicalSurface: true, UsesPlaceholderData: false),
            new MarketFactSource("local-market-statistics-store", IsCanonicalSurface: true, UsesPlaceholderData: false),
            new MarketFactSource("local-market-orders-store", IsCanonicalSurface: true, UsesPlaceholderData: false),
            MarketFactFileCodec.ReadSnapshots(layout.CurrentSnapshotsPath),
            MarketFactFileCodec.ReadHistoryWindows(layout.HistoryWindowsPath),
            MarketFactFileCodec.ReadStatisticsProjections(layout.StatisticsProjectionsPath),
            MarketFactFileCodec.ReadOrderSnapshots(layout.CurrentOrdersPath));
    }

    private static MarketLocalFactCatalog CreateBootstrapFallback()
    {
        var snapshots = BootstrapMarketFactSeedData.CreateSnapshots();
        var historyWindows = BootstrapMarketFactSeedData.CreateHistoryWindows();
        var statisticsProjections = BootstrapMarketFactSeedData.CreateStatisticsProjections(snapshots, historyWindows);
        var orderSnapshots = BootstrapMarketFactSeedData.CreateOrderSnapshots();

        return new MarketLocalFactCatalog(
            "bootstrap-fallback",
            new MarketFactSource("bootstrap-market-snapshot-fallback", IsCanonicalSurface: true, UsesPlaceholderData: true),
            new MarketFactSource("bootstrap-market-history-fallback", IsCanonicalSurface: true, UsesPlaceholderData: true),
            new MarketFactSource("bootstrap-market-statistics-fallback", IsCanonicalSurface: true, UsesPlaceholderData: true),
            new MarketFactSource("bootstrap-market-orders-fallback", IsCanonicalSurface: true, UsesPlaceholderData: true),
            snapshots,
            historyWindows,
            statisticsProjections,
            orderSnapshots);
    }
}
