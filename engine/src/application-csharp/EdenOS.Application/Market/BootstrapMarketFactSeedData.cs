using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

internal static class BootstrapMarketFactSeedData
{
    private const long JitaStationId = 60003760;
    private const long PerimeterSharedMarketHubId = 1038457641676;

    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> CreateSnapshots() =>
        new Dictionary<(long TypeId, long LocationId), MarketSnapshotRecord>
        {
            [(34, JitaStationId)] = new(34, JitaStationId, 6.12m, 5.98m, 11_600_000m, 9_400_000m, 822, 601, DateTimeOffset.Parse("2026-04-11T04:30:00Z")),
            [(35, JitaStationId)] = new(35, JitaStationId, 10.48m, 10.12m, 4_200_000m, 3_900_000m, 340, 278, DateTimeOffset.Parse("2026-04-11T04:30:00Z")),
            [(587, JitaStationId)] = new(587, JitaStationId, 812000m, 775000m, 18m, 11m, 19, 14, DateTimeOffset.Parse("2026-04-11T04:26:00Z")),
            [(34, PerimeterSharedMarketHubId)] = new(34, PerimeterSharedMarketHubId, 6.08m, 6.01m, 14_100_000m, 11_700_000m, 641, 402, DateTimeOffset.Parse("2026-04-11T04:28:00Z")),
            [(35, PerimeterSharedMarketHubId)] = new(35, PerimeterSharedMarketHubId, 10.39m, 10.17m, 3_100_000m, 2_600_000m, 188, 121, DateTimeOffset.Parse("2026-04-11T04:24:00Z"))
        };

    public static IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> CreateHistoryWindows() =>
        new Dictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>>
        {
            [(34, JitaStationId)] = GenerateHistorySeries(34, 6.02m, 7_500_000, 60, 0.03m),
            [(35, JitaStationId)] = GenerateHistorySeries(35, 10.21m, 2_450_000, 60, 0.05m),
            [(587, JitaStationId)] = GenerateHistorySeries(587, 786000m, 16, 60, 5500m),
            [(34, PerimeterSharedMarketHubId)] = GenerateHistorySeries(34, 6.00m, 9_100_000, 45, 0.02m)
        };

    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> CreateStatisticsProjections(
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots,
        IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> historyWindows)
    {
        return historyWindows.ToDictionary(
            entry => entry.Key,
            entry => MarketStatisticsProjectionCalculator.BuildProjection(
                entry.Key.TypeId,
                entry.Key.LocationId,
                ResolveLastObservedAtUtc(snapshots, entry.Key.LocationId),
                entry.Value));
    }

    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord> CreateOrderSnapshots() =>
        new Dictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord>
        {
            [(34, JitaStationId)] = new(
                34,
                JitaStationId,
                DateTimeOffset.Parse("2026-04-11T04:30:00Z"),
                [
                    new MarketOrderRecord(500001, MarketOrderSide.Sell, 6.12m, 3_600_000, 1, "station", DateTimeOffset.Parse("2026-04-11T03:40:00Z")),
                    new MarketOrderRecord(500002, MarketOrderSide.Sell, 6.13m, 2_900_000, 1, "station", DateTimeOffset.Parse("2026-04-11T03:22:00Z")),
                    new MarketOrderRecord(500003, MarketOrderSide.Sell, 6.14m, 1_750_000, 1, "station", DateTimeOffset.Parse("2026-04-11T02:48:00Z")),
                    new MarketOrderRecord(500004, MarketOrderSide.Sell, 6.16m, 1_200_000, 1000, "station", DateTimeOffset.Parse("2026-04-10T22:10:00Z")),
                    new MarketOrderRecord(500005, MarketOrderSide.Sell, 6.18m, 850_000, 1000, "station", DateTimeOffset.Parse("2026-04-10T19:02:00Z")),
                    new MarketOrderRecord(500006, MarketOrderSide.Sell, 6.20m, 610_000, 5000, "region", DateTimeOffset.Parse("2026-04-10T16:17:00Z"))
                ],
                [
                    new MarketOrderRecord(510001, MarketOrderSide.Buy, 5.98m, 2_700_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:58:00Z")),
                    new MarketOrderRecord(510002, MarketOrderSide.Buy, 5.97m, 2_250_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:35:00Z")),
                    new MarketOrderRecord(510003, MarketOrderSide.Buy, 5.96m, 1_800_000, 5000, "station", DateTimeOffset.Parse("2026-04-11T02:54:00Z")),
                    new MarketOrderRecord(510004, MarketOrderSide.Buy, 5.94m, 1_240_000, 1000, "region", DateTimeOffset.Parse("2026-04-10T23:14:00Z")),
                    new MarketOrderRecord(510005, MarketOrderSide.Buy, 5.93m, 920_000, 1000, "region", DateTimeOffset.Parse("2026-04-10T20:33:00Z")),
                    new MarketOrderRecord(510006, MarketOrderSide.Buy, 5.91m, 710_000, 10000, "region", DateTimeOffset.Parse("2026-04-10T18:09:00Z"))
                ]),
            [(35, JitaStationId)] = new(
                35,
                JitaStationId,
                DateTimeOffset.Parse("2026-04-11T04:31:00Z"),
                [
                    new MarketOrderRecord(520001, MarketOrderSide.Sell, 10.48m, 1_650_000, 1, "station", DateTimeOffset.Parse("2026-04-11T04:00:00Z")),
                    new MarketOrderRecord(520002, MarketOrderSide.Sell, 10.50m, 1_420_000, 1, "station", DateTimeOffset.Parse("2026-04-11T03:18:00Z")),
                    new MarketOrderRecord(520003, MarketOrderSide.Sell, 10.53m, 980_000, 1000, "station", DateTimeOffset.Parse("2026-04-11T01:47:00Z"))
                ],
                [
                    new MarketOrderRecord(530001, MarketOrderSide.Buy, 10.12m, 1_100_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:42:00Z")),
                    new MarketOrderRecord(530002, MarketOrderSide.Buy, 10.10m, 880_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:05:00Z")),
                    new MarketOrderRecord(530003, MarketOrderSide.Buy, 10.08m, 670_000, 1000, "station", DateTimeOffset.Parse("2026-04-11T00:58:00Z"))
                ]),
            [(34, PerimeterSharedMarketHubId)] = new(
                34,
                PerimeterSharedMarketHubId,
                DateTimeOffset.Parse("2026-04-11T04:28:00Z"),
                [
                    new MarketOrderRecord(540001, MarketOrderSide.Sell, 6.08m, 4_400_000, 1, "structure", DateTimeOffset.Parse("2026-04-11T04:01:00Z")),
                    new MarketOrderRecord(540002, MarketOrderSide.Sell, 6.09m, 3_150_000, 1, "structure", DateTimeOffset.Parse("2026-04-11T03:39:00Z")),
                    new MarketOrderRecord(540003, MarketOrderSide.Sell, 6.10m, 2_280_000, 1000, "structure", DateTimeOffset.Parse("2026-04-11T02:57:00Z")),
                    new MarketOrderRecord(540004, MarketOrderSide.Sell, 6.12m, 1_640_000, 1000, "region", DateTimeOffset.Parse("2026-04-11T02:06:00Z")),
                    new MarketOrderRecord(540005, MarketOrderSide.Sell, 6.13m, 980_000, 10000, "region", DateTimeOffset.Parse("2026-04-10T22:26:00Z")),
                    new MarketOrderRecord(540006, MarketOrderSide.Sell, 6.15m, 610_000, 10000, "region", DateTimeOffset.Parse("2026-04-10T18:44:00Z"))
                ],
                [
                    new MarketOrderRecord(550001, MarketOrderSide.Buy, 6.01m, 3_800_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:56:00Z")),
                    new MarketOrderRecord(550002, MarketOrderSide.Buy, 6.00m, 2_950_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:04:00Z")),
                    new MarketOrderRecord(550003, MarketOrderSide.Buy, 5.99m, 2_100_000, 1000, "structure", DateTimeOffset.Parse("2026-04-11T02:41:00Z")),
                    new MarketOrderRecord(550004, MarketOrderSide.Buy, 5.98m, 1_650_000, 1000, "region", DateTimeOffset.Parse("2026-04-11T01:15:00Z")),
                    new MarketOrderRecord(550005, MarketOrderSide.Buy, 5.97m, 990_000, 5000, "region", DateTimeOffset.Parse("2026-04-10T21:30:00Z")),
                    new MarketOrderRecord(550006, MarketOrderSide.Buy, 5.96m, 720_000, 10000, "region", DateTimeOffset.Parse("2026-04-10T17:53:00Z"))
                ]),
            [(35, PerimeterSharedMarketHubId)] = new(
                35,
                PerimeterSharedMarketHubId,
                DateTimeOffset.Parse("2026-04-11T04:32:00Z"),
                [
                    new MarketOrderRecord(560001, MarketOrderSide.Sell, 10.39m, 1_280_000, 1, "structure", DateTimeOffset.Parse("2026-04-11T04:12:00Z")),
                    new MarketOrderRecord(560002, MarketOrderSide.Sell, 10.41m, 1_020_000, 1, "structure", DateTimeOffset.Parse("2026-04-11T03:34:00Z")),
                    new MarketOrderRecord(560003, MarketOrderSide.Sell, 10.43m, 780_000, 1000, "region", DateTimeOffset.Parse("2026-04-11T02:22:00Z"))
                ],
                [
                    new MarketOrderRecord(570001, MarketOrderSide.Buy, 10.17m, 960_000, 1, "region", DateTimeOffset.Parse("2026-04-11T03:47:00Z")),
                    new MarketOrderRecord(570002, MarketOrderSide.Buy, 10.15m, 740_000, 1, "region", DateTimeOffset.Parse("2026-04-11T02:59:00Z")),
                    new MarketOrderRecord(570003, MarketOrderSide.Buy, 10.13m, 520_000, 1000, "structure", DateTimeOffset.Parse("2026-04-11T01:38:00Z"))
                ])
        };

    private static IReadOnlyList<HistoryPoint> GenerateHistorySeries(long typeId, decimal basePrice, long baseVolume, int days, decimal dailyVariance)
    {
        var startDay = new DateOnly(2026, 2, 11);
        var points = new List<HistoryPoint>(days);

        for (var index = 0; index < days; index++)
        {
            var direction = ((index + typeId) % 7) - 3;
            var drift = direction * dailyVariance;
            var price = Math.Round(basePrice + drift + (index * dailyVariance * 0.05m), 2);
            var high = Math.Round(price * 1.018m, 2);
            var low = Math.Round(price * 0.982m, 2);
            var volume = baseVolume + ((((index + (int)typeId) % 9) - 4) * 37_500L);

            points.Add(new HistoryPoint(
                startDay.AddDays(index),
                price,
                high,
                low,
                Math.Max(1L, volume)));
        }

        return points;
    }

    private static DateTimeOffset ResolveLastObservedAtUtc(
        IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots,
        long locationId)
    {
        return snapshots.Values
            .Where(snapshot => snapshot.LocationId == locationId)
            .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
            .Select(snapshot => snapshot.ObservedAtUtc)
            .DefaultIfEmpty(DateTimeOffset.Parse("2026-04-11T00:00:00Z"))
            .First();
    }
}
