using EdenOS.Application.Accounts;
using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

public sealed record MarketSnapshotImportEntry(
    long TypeId,
    long LocationId,
    decimal LowestSellPrice,
    decimal HighestBuyPrice,
    decimal TopFiveSellDepthUnits,
    decimal TopFiveBuyDepthUnits,
    int SellOrderCount,
    int BuyOrderCount,
    DateTimeOffset ObservedAtUtc);

public sealed record MarketSnapshotImportBatch(
    string Source,
    IReadOnlyList<MarketSnapshotImportEntry> Snapshots);

public sealed record MarketHistorySeriesImport(
    long TypeId,
    long LocationId,
    IReadOnlyList<HistoryPoint> Points);

public sealed record MarketHistoryImportBatch(
    string Source,
    IReadOnlyList<MarketHistorySeriesImport> Series);

internal sealed record MarketOrdersImportBatch(
    string Source,
    IReadOnlyList<MarketOrderSnapshotRecord> Snapshots);

public sealed record MarketCharacterGrantImport(
    string EsiCharacterId,
    IReadOnlyList<WorkspaceStructureGrant> Grants);

public sealed record MarketStructureGrantRefreshBatch(
    string Source,
    IReadOnlyList<MarketCharacterGrantImport> CharacterGrants);

public sealed record MarketFactRefreshResult(
    string Operation,
    string Source,
    string BundleVersion,
    int SnapshotUpserts,
    int HistorySeriesTouched,
    int HistoryPointsUpserted,
    int StatisticsRecomputed,
    int CharactersRefreshed,
    int StructureGrantsWritten,
    IReadOnlyList<string> Notes);

public sealed class LocalMarketFactRefreshPipeline
{
    private readonly string _marketFactsDirectoryPath;

    public LocalMarketFactRefreshPipeline()
        : this(ResolveDefaultMarketFactsDirectory())
    {
    }

    public LocalMarketFactRefreshPipeline(string marketFactsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        _marketFactsDirectoryPath = Path.GetFullPath(marketFactsDirectoryPath);
    }

    public MarketFactRefreshResult ImportSnapshotFile(string payloadPath)
    {
        return ImportSnapshots(MarketFactFileCodec.ReadSnapshotImportBatch(Path.GetFullPath(payloadPath)));
    }

    public MarketFactRefreshResult ImportSnapshots(MarketSnapshotImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
        var snapshots = MarketFactFileCodec.ReadSnapshots(layout.CurrentSnapshotsPath).ToDictionary(entry => entry.Key, entry => entry.Value);
        var historyWindows = MarketFactFileCodec.ReadHistoryWindows(layout.HistoryWindowsPath);
        var statistics = MarketFactFileCodec.ReadStatisticsProjections(layout.StatisticsProjectionsPath).ToDictionary(entry => entry.Key, entry => entry.Value);

        var touchedPairs = new HashSet<(long TypeId, long LocationId)>();
        foreach (var entry in batch.Snapshots)
        {
            var key = (entry.TypeId, entry.LocationId);
            snapshots[key] = new MarketSnapshotRecord(
                entry.TypeId,
                entry.LocationId,
                entry.LowestSellPrice,
                entry.HighestBuyPrice,
                entry.TopFiveSellDepthUnits,
                entry.TopFiveBuyDepthUnits,
                entry.SellOrderCount,
                entry.BuyOrderCount,
                entry.ObservedAtUtc);
            touchedPairs.Add(key);
        }

        var statisticsRecomputed = RebuildStatisticsForPairs(statistics, snapshots, historyWindows, touchedPairs);
        var bundleVersion = CreateBundleVersion("snapshot-import");

        MarketFactFileCodec.WriteSnapshots(layout.CurrentSnapshotsPath, snapshots);
        MarketFactFileCodec.WriteStatisticsProjections(layout.StatisticsProjectionsPath, statistics);
        MarketFactsDataPaths.WriteManifest(layout, bundleVersion);

        return new MarketFactRefreshResult(
            "snapshot-import",
            batch.Source,
            bundleVersion,
            touchedPairs.Count,
            0,
            0,
            statisticsRecomputed,
            0,
            0,
            [
                $"Upserted {touchedPairs.Count} snapshot pair(s) from '{batch.Source}'.",
                statisticsRecomputed > 0
                    ? $"Recomputed {statisticsRecomputed} statistics projection(s) for pairs that already have history coverage."
                    : "No statistics projections were recomputed because the imported snapshot pairs do not yet have local history coverage."
            ]);
    }

    public MarketFactRefreshResult AppendHistoryFile(string payloadPath)
    {
        return AppendHistory(MarketFactFileCodec.ReadHistoryImportBatch(Path.GetFullPath(payloadPath)));
    }

    public MarketFactRefreshResult AppendHistory(MarketHistoryImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
        var snapshots = MarketFactFileCodec.ReadSnapshots(layout.CurrentSnapshotsPath);
        var historyWindows = MarketFactFileCodec.ReadHistoryWindows(layout.HistoryWindowsPath).ToDictionary(entry => entry.Key, entry => entry.Value);
        var statistics = MarketFactFileCodec.ReadStatisticsProjections(layout.StatisticsProjectionsPath).ToDictionary(entry => entry.Key, entry => entry.Value);

        var touchedPairs = new HashSet<(long TypeId, long LocationId)>();
        var pointUpserts = 0;

        foreach (var series in batch.Series)
        {
            var key = (series.TypeId, series.LocationId);
            var merged = historyWindows.TryGetValue(key, out var existing)
                ? existing.ToDictionary(point => point.Day, point => point)
                : new Dictionary<DateOnly, HistoryPoint>();

            foreach (var point in series.Points)
            {
                merged[point.Day] = point;
                pointUpserts++;
            }

            historyWindows[key] = merged.Values
                .OrderBy(point => point.Day)
                .ToArray();

            touchedPairs.Add(key);
        }

        var statisticsRecomputed = RebuildStatisticsForPairs(statistics, snapshots, historyWindows, touchedPairs);
        var bundleVersion = CreateBundleVersion("history-append");

        MarketFactFileCodec.WriteHistoryWindows(layout.HistoryWindowsPath, historyWindows);
        MarketFactFileCodec.WriteStatisticsProjections(layout.StatisticsProjectionsPath, statistics);
        MarketFactsDataPaths.WriteManifest(layout, bundleVersion);

        return new MarketFactRefreshResult(
            "history-append",
            batch.Source,
            bundleVersion,
            0,
            touchedPairs.Count,
            pointUpserts,
            statisticsRecomputed,
            0,
            0,
            [
                $"Merged {pointUpserts} history point(s) across {touchedPairs.Count} pair(s) from '{batch.Source}'.",
                $"Statistics projections were recomputed immediately for all touched history pairs ({statisticsRecomputed} pair(s))."
            ]);
    }

    public MarketFactRefreshResult RefreshStructureGrantsFile(string payloadPath)
    {
        return RefreshStructureGrants(MarketFactFileCodec.ReadStructureGrantRefreshBatch(Path.GetFullPath(payloadPath)));
    }

    public MarketFactRefreshResult RefreshStructureGrants(MarketStructureGrantRefreshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
        var grantsByCharacterId = MarketFactFileCodec.ReadStructureGrants(layout.StructureGrantsPath).ToDictionary(entry => entry.Key, entry => entry.Value);

        foreach (var entry in batch.CharacterGrants)
        {
            grantsByCharacterId[entry.EsiCharacterId] = entry.Grants
                .OrderBy(grant => grant.StructureId)
                .ToArray();
        }

        var bundleVersion = CreateBundleVersion("grant-refresh");
        MarketFactFileCodec.WriteStructureGrants(layout.StructureGrantsPath, grantsByCharacterId);
        MarketFactsDataPaths.WriteManifest(layout, bundleVersion);

        return new MarketFactRefreshResult(
            "grant-refresh",
            batch.Source,
            bundleVersion,
            0,
            0,
            0,
            0,
            batch.CharacterGrants.Count,
            grantsByCharacterId.Values.Sum(grants => grants.Count),
            [
                $"Refreshed structure grant projections for {batch.CharacterGrants.Count} character slice(s) from '{batch.Source}'.",
                "Grant refresh updates only the local projection file; authenticated ESI refresh is still outside this pipeline."
            ]);
    }

    public MarketFactRefreshResult RebuildStatisticsProjections()
    {
        var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
        var snapshots = MarketFactFileCodec.ReadSnapshots(layout.CurrentSnapshotsPath);
        var historyWindows = MarketFactFileCodec.ReadHistoryWindows(layout.HistoryWindowsPath);
        var statistics = new Dictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord>();
        var statisticsRecomputed = RebuildStatisticsForPairs(statistics, snapshots, historyWindows, historyWindows.Keys);
        var bundleVersion = CreateBundleVersion("statistics-rebuild");

        MarketFactFileCodec.WriteStatisticsProjections(layout.StatisticsProjectionsPath, statistics);
        MarketFactsDataPaths.WriteManifest(layout, bundleVersion);

        return new MarketFactRefreshResult(
            "statistics-rebuild",
            "local-history-store",
            bundleVersion,
            0,
            0,
            0,
            statisticsRecomputed,
            0,
            0,
            [$"Rebuilt {statisticsRecomputed} statistics projection(s) from the current local history store."]);
    }

    private static int RebuildStatisticsForPairs(
        IDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> statistics,
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots,
        IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> historyWindows,
        IEnumerable<(long TypeId, long LocationId)> touchedPairs)
    {
        var recomputed = 0;

        foreach (var pair in touchedPairs.Distinct())
        {
            if (!historyWindows.TryGetValue(pair, out var points) || points.Count == 0)
            {
                statistics.Remove(pair);
                continue;
            }

            statistics[pair] = MarketStatisticsProjectionCalculator.BuildProjection(
                pair.TypeId,
                pair.LocationId,
                ResolveDerivedFromLastObservedAtUtc(snapshots, pair, points),
                points);
            recomputed++;
        }

        return recomputed;
    }

    private static DateTimeOffset ResolveDerivedFromLastObservedAtUtc(
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots,
        (long TypeId, long LocationId) pair,
        IReadOnlyList<HistoryPoint> points)
    {
        if (snapshots.TryGetValue(pair, out var snapshot))
        {
            return snapshot.ObservedAtUtc;
        }

        var locationObservedAtUtc = snapshots.Values
            .Where(value => value.LocationId == pair.LocationId)
            .OrderByDescending(value => value.ObservedAtUtc)
            .Select(value => (DateTimeOffset?)value.ObservedAtUtc)
            .FirstOrDefault();

        if (locationObservedAtUtc.HasValue)
        {
            return locationObservedAtUtc.Value;
        }

        var lastDay = points.Max(point => point.Day);
        return new DateTimeOffset(lastDay.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
    }

    private static string ResolveDefaultMarketFactsDirectory()
    {
        if (!MarketFactsDataPaths.TryResolveDefaultLayout(out var layout))
        {
            throw new DirectoryNotFoundException("Default market facts layout could not be resolved from the current workspace.");
        }

        return layout.DirectoryPath;
    }

    private static string CreateBundleVersion(string operation)
    {
        return $"{DateTimeOffset.UtcNow:yyyy-MM-dd}-local-market-facts-{operation}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}";
    }
}
