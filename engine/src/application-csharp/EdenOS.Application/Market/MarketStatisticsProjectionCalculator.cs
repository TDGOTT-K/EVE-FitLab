using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

internal static class MarketStatisticsProjectionCalculator
{
    public static MarketStatisticsProjectionRecord BuildProjection(
        long typeId,
        long locationId,
        DateTimeOffset derivedFromLastObservedAtUtc,
        IReadOnlyList<HistoryPoint> orderedPoints)
    {
        var normalizedPoints = orderedPoints
            .OrderBy(point => point.Day)
            .ToArray();

        return new MarketStatisticsProjectionRecord(
            typeId,
            locationId,
            derivedFromLastObservedAtUtc,
            BuildRollingWindow(normalizedPoints, 7),
            BuildRollingWindow(normalizedPoints, 21),
            BuildRollingWindow(normalizedPoints, 60));
    }

    private static RollingWindowStatistics BuildRollingWindow(IReadOnlyList<HistoryPoint> ordered, int windowDays)
    {
        var slice = ordered.TakeLast(Math.Min(windowDays, ordered.Count)).ToArray();
        var averagePrice = Math.Round(slice.Average(point => point.AveragePrice), 2);
        var averageDailyVolume = (long)Math.Round(slice.Average(point => point.Volume), MidpointRounding.AwayFromZero);
        var volatilityPercent = CalculateVolatilityPercent(slice.Select(point => point.AveragePrice).ToArray());
        var sortedPrices = slice.Select(point => point.AveragePrice).OrderBy(value => value).ToArray();

        return new RollingWindowStatistics(
            windowDays,
            averagePrice,
            averageDailyVolume,
            volatilityPercent,
            Percentile(sortedPrices, 0.10m),
            Percentile(sortedPrices, 0.50m),
            Percentile(sortedPrices, 0.90m),
            CalculateExitQualityScore(slice));
    }

    private static decimal CalculateVolatilityPercent(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
        {
            return 0m;
        }

        var mean = values.Average();
        if (mean == 0m)
        {
            return 0m;
        }

        var variance = values
            .Select(value =>
            {
                var delta = value - mean;
                return delta * delta;
            })
            .Average();

        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        return Math.Round((standardDeviation / mean) * 100m, 2);
    }

    private static decimal Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        var index = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
        {
            return Math.Round(sortedValues[lower], 2);
        }

        var weight = index - lower;
        var interpolated = sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
        return Math.Round(interpolated, 2);
    }

    private static decimal CalculateExitQualityScore(IReadOnlyList<HistoryPoint> slice)
    {
        if (slice.Count == 0)
        {
            return 0m;
        }

        var averageVolume = slice.Average(point => (decimal)point.Volume);
        var liquidityFactor = Math.Min(1m, averageVolume / 1_000_000m);
        var stabilityFactor = 1m - Math.Min(1m, CalculateVolatilityPercent(slice.Select(point => point.AveragePrice).ToArray()) / 100m);

        return Math.Round((liquidityFactor * 0.6m + stabilityFactor * 0.4m) * 100m, 2);
    }
}
