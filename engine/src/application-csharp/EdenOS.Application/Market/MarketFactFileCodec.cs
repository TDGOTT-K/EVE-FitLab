using System.Globalization;
using System.Text.Json;
using EdenOS.Application.Accounts;
using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

internal static class MarketFactFileCodec
{
    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> ReadSnapshots(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var snapshots = new Dictionary<(long TypeId, long LocationId), MarketSnapshotRecord>();

        foreach (var entry in document.RootElement.GetProperty("snapshots").EnumerateArray())
        {
            var values = entry.EnumerateArray().ToArray();
            var snapshot = new MarketSnapshotRecord(
                values[0].GetInt64(),
                values[1].GetInt64(),
                values[2].GetDecimal(),
                values[3].GetDecimal(),
                values[4].GetDecimal(),
                values[5].GetDecimal(),
                values[6].GetInt32(),
                values[7].GetInt32(),
                DateTimeOffset.Parse(values[8].GetString()!, CultureInfo.InvariantCulture));

            snapshots[(snapshot.TypeId, snapshot.LocationId)] = snapshot;
        }

        return snapshots;
    }

    public static IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> ReadHistoryWindows(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var historyWindows = new Dictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>>();

        foreach (var entry in document.RootElement.GetProperty("series").EnumerateArray())
        {
            var values = entry.EnumerateArray().ToArray();
            var typeId = values[0].GetInt64();
            var locationId = values[1].GetInt64();
            var points = values[2]
                .EnumerateArray()
                .Select(point =>
                {
                    var pointValues = point.EnumerateArray().ToArray();
                    return new HistoryPoint(
                        DateOnly.Parse(pointValues[0].GetString()!, CultureInfo.InvariantCulture),
                        pointValues[1].GetDecimal(),
                        pointValues[2].GetDecimal(),
                        pointValues[3].GetDecimal(),
                        pointValues[4].GetInt64());
                })
                .OrderBy(point => point.Day)
                .ToArray();

            historyWindows[(typeId, locationId)] = points;
        }

        return historyWindows;
    }

    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord> ReadOrderSnapshots(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var orderSnapshots = new Dictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord>();

        foreach (var entry in document.RootElement.GetProperty("order_books").EnumerateArray())
        {
            var values = entry.EnumerateArray().ToArray();
            var snapshot = new MarketOrderSnapshotRecord(
                values[0].GetInt64(),
                values[1].GetInt64(),
                DateTimeOffset.Parse(values[2].GetString()!, CultureInfo.InvariantCulture),
                ReadOrderRows(values[3], MarketOrderSide.Sell),
                ReadOrderRows(values[4], MarketOrderSide.Buy));

            orderSnapshots[(snapshot.TypeId, snapshot.LocationId)] = snapshot;
        }

        return orderSnapshots;
    }

    public static MarketOrdersImportBatch ReadOrderSnapshotImportBatch(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var source = root.TryGetProperty("source", out var sourceElement) && !string.IsNullOrWhiteSpace(sourceElement.GetString())
            ? sourceElement.GetString()!
            : Path.GetFileName(path);

        var snapshots = root.GetProperty("order_books")
            .EnumerateArray()
            .Select(entry =>
            {
                var values = entry.EnumerateArray().ToArray();
                return new MarketOrderSnapshotRecord(
                    values[0].GetInt64(),
                    values[1].GetInt64(),
                    DateTimeOffset.Parse(values[2].GetString()!, CultureInfo.InvariantCulture),
                    ReadOrderRows(values[3], MarketOrderSide.Sell),
                    ReadOrderRows(values[4], MarketOrderSide.Buy));
            })
            .ToArray();

        return new MarketOrdersImportBatch(source, snapshots);
    }

    public static IReadOnlyDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> ReadStatisticsProjections(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var projections = new Dictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord>();

        foreach (var entry in document.RootElement.GetProperty("projections").EnumerateArray())
        {
            var values = entry.EnumerateArray().ToArray();
            var projection = new MarketStatisticsProjectionRecord(
                values[0].GetInt64(),
                values[1].GetInt64(),
                DateTimeOffset.Parse(values[2].GetString()!, CultureInfo.InvariantCulture),
                ReadRollingWindow(values[3]),
                ReadRollingWindow(values[4]),
                ReadRollingWindow(values[5]));

            projections[(projection.TypeId, projection.LocationId)] = projection;
        }

        return projections;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<WorkspaceStructureGrant>> ReadStructureGrants(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var grantsByCharacterId = new Dictionary<string, IReadOnlyList<WorkspaceStructureGrant>>(StringComparer.Ordinal);

        foreach (var entry in document.RootElement.GetProperty("character_grants").EnumerateArray())
        {
            var values = entry.EnumerateArray().ToArray();
            var esiCharacterId = values[0].GetString()!;
            var grants = values[1]
                .EnumerateArray()
                .Select(grant =>
                {
                    var grantValues = grant.EnumerateArray().ToArray();
                    return new WorkspaceStructureGrant(
                        grantValues[0].GetInt64(),
                        grantValues[1].GetString()!,
                        DateTimeOffset.Parse(grantValues[2].GetString()!, CultureInfo.InvariantCulture),
                        grantValues[3].GetBoolean());
                })
                .OrderBy(grant => grant.StructureId)
                .ToArray();

            grantsByCharacterId[esiCharacterId] = grants;
        }

        return grantsByCharacterId;
    }

    public static void WriteSnapshots(string path, IReadOnlyDictionary<(long TypeId, long LocationId), MarketSnapshotRecord> snapshots)
    {
        WriteJsonAtomically(path, writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("snapshots");
            writer.WriteStartArray();

            foreach (var snapshot in snapshots.Values.OrderBy(value => value.TypeId).ThenBy(value => value.LocationId))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(snapshot.TypeId);
                writer.WriteNumberValue(snapshot.LocationId);
                writer.WriteNumberValue(snapshot.LowestSellPrice);
                writer.WriteNumberValue(snapshot.HighestBuyPrice);
                writer.WriteNumberValue(snapshot.TopFiveSellDepthUnits);
                writer.WriteNumberValue(snapshot.TopFiveBuyDepthUnits);
                writer.WriteNumberValue(snapshot.SellOrderCount);
                writer.WriteNumberValue(snapshot.BuyOrderCount);
                writer.WriteStringValue(FormatInstant(snapshot.ObservedAtUtc));
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static void WriteHistoryWindows(string path, IReadOnlyDictionary<(long TypeId, long LocationId), IReadOnlyList<HistoryPoint>> historyWindows)
    {
        WriteJsonAtomically(path, writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("series");
            writer.WriteStartArray();

            foreach (var entry in historyWindows.OrderBy(value => value.Key.TypeId).ThenBy(value => value.Key.LocationId))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(entry.Key.TypeId);
                writer.WriteNumberValue(entry.Key.LocationId);
                writer.WriteStartArray();

                foreach (var point in entry.Value.OrderBy(value => value.Day))
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(point.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    writer.WriteNumberValue(point.AveragePrice);
                    writer.WriteNumberValue(point.HighestPrice);
                    writer.WriteNumberValue(point.LowestPrice);
                    writer.WriteNumberValue(point.Volume);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static void WriteOrderSnapshots(string path, IReadOnlyDictionary<(long TypeId, long LocationId), MarketOrderSnapshotRecord> orderSnapshots)
    {
        WriteJsonAtomically(path, writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("order_books");
            writer.WriteStartArray();

            foreach (var snapshot in orderSnapshots.Values.OrderBy(value => value.TypeId).ThenBy(value => value.LocationId))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(snapshot.TypeId);
                writer.WriteNumberValue(snapshot.LocationId);
                writer.WriteStringValue(FormatInstant(snapshot.ObservedAtUtc));
                WriteOrderRows(writer, snapshot.SellOrders);
                WriteOrderRows(writer, snapshot.BuyOrders);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static void WriteStatisticsProjections(string path, IReadOnlyDictionary<(long TypeId, long LocationId), MarketStatisticsProjectionRecord> projections)
    {
        WriteJsonAtomically(path, writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("projections");
            writer.WriteStartArray();

            foreach (var projection in projections.Values.OrderBy(value => value.TypeId).ThenBy(value => value.LocationId))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(projection.TypeId);
                writer.WriteNumberValue(projection.LocationId);
                writer.WriteStringValue(FormatInstant(projection.DerivedFromLastObservedAtUtc));
                WriteRollingWindow(writer, projection.SevenDay);
                WriteRollingWindow(writer, projection.TwentyOneDay);
                WriteRollingWindow(writer, projection.SixtyDay);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static void WriteStructureGrants(string path, IReadOnlyDictionary<string, IReadOnlyList<WorkspaceStructureGrant>> grantsByCharacterId)
    {
        WriteJsonAtomically(path, writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("character_grants");
            writer.WriteStartArray();

            foreach (var entry in grantsByCharacterId.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray();
                writer.WriteStringValue(entry.Key);
                writer.WriteStartArray();

                foreach (var grant in entry.Value.OrderBy(value => value.StructureId))
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(grant.StructureId);
                    writer.WriteStringValue(grant.GrantSource);
                    writer.WriteStringValue(FormatInstant(grant.LastVerifiedAtUtc));
                    writer.WriteBooleanValue(grant.IsStale);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static MarketSnapshotImportBatch ReadSnapshotImportBatch(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var source = root.TryGetProperty("source", out var sourceElement) && !string.IsNullOrWhiteSpace(sourceElement.GetString())
            ? sourceElement.GetString()!
            : Path.GetFileName(path);

        var snapshots = root.GetProperty("snapshots")
            .EnumerateArray()
            .Select(entry =>
            {
                var values = entry.EnumerateArray().ToArray();
                return new MarketSnapshotImportEntry(
                    values[0].GetInt64(),
                    values[1].GetInt64(),
                    values[2].GetDecimal(),
                    values[3].GetDecimal(),
                    values[4].GetDecimal(),
                    values[5].GetDecimal(),
                    values[6].GetInt32(),
                    values[7].GetInt32(),
                    DateTimeOffset.Parse(values[8].GetString()!, CultureInfo.InvariantCulture));
            })
            .ToArray();

        return new MarketSnapshotImportBatch(source, snapshots);
    }

    public static MarketHistoryImportBatch ReadHistoryImportBatch(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var source = root.TryGetProperty("source", out var sourceElement) && !string.IsNullOrWhiteSpace(sourceElement.GetString())
            ? sourceElement.GetString()!
            : Path.GetFileName(path);

        var series = root.GetProperty("series")
            .EnumerateArray()
            .Select(entry =>
            {
                var values = entry.EnumerateArray().ToArray();
                var points = values[2]
                    .EnumerateArray()
                    .Select(point =>
                    {
                        var pointValues = point.EnumerateArray().ToArray();
                        return new HistoryPoint(
                            DateOnly.Parse(pointValues[0].GetString()!, CultureInfo.InvariantCulture),
                            pointValues[1].GetDecimal(),
                            pointValues[2].GetDecimal(),
                            pointValues[3].GetDecimal(),
                            pointValues[4].GetInt64());
                    })
                    .ToArray();

                return new MarketHistorySeriesImport(values[0].GetInt64(), values[1].GetInt64(), points);
            })
            .ToArray();

        return new MarketHistoryImportBatch(source, series);
    }

    public static MarketStructureGrantRefreshBatch ReadStructureGrantRefreshBatch(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var source = root.TryGetProperty("source", out var sourceElement) && !string.IsNullOrWhiteSpace(sourceElement.GetString())
            ? sourceElement.GetString()!
            : Path.GetFileName(path);

        var characterGrants = root.GetProperty("character_grants")
            .EnumerateArray()
            .Select(entry =>
            {
                var values = entry.EnumerateArray().ToArray();
                var grants = values[1]
                    .EnumerateArray()
                    .Select(grant =>
                    {
                        var grantValues = grant.EnumerateArray().ToArray();
                        return new WorkspaceStructureGrant(
                            grantValues[0].GetInt64(),
                            grantValues[1].GetString()!,
                            DateTimeOffset.Parse(grantValues[2].GetString()!, CultureInfo.InvariantCulture),
                            grantValues[3].GetBoolean());
                    })
                    .ToArray();

                return new MarketCharacterGrantImport(values[0].GetString()!, grants);
            })
            .ToArray();

        return new MarketStructureGrantRefreshBatch(source, characterGrants);
    }

    private static RollingWindowStatistics ReadRollingWindow(JsonElement element)
    {
        var values = element.EnumerateArray().ToArray();
        return new RollingWindowStatistics(
            values[0].GetInt32(),
            values[1].GetDecimal(),
            values[2].GetInt64(),
            values[3].GetDecimal(),
            values[4].GetDecimal(),
            values[5].GetDecimal(),
            values[6].GetDecimal(),
            values[7].GetDecimal());
    }

    private static IReadOnlyList<MarketOrderRecord> ReadOrderRows(JsonElement element, MarketOrderSide side)
    {
        return element
            .EnumerateArray()
            .Select(order =>
            {
                var values = order.EnumerateArray().ToArray();
                return new MarketOrderRecord(
                    values[0].GetInt64(),
                    side,
                    values[1].GetDecimal(),
                    values[2].GetInt64(),
                    values[3].GetInt64(),
                    values[4].GetString()!,
                    DateTimeOffset.Parse(values[5].GetString()!, CultureInfo.InvariantCulture));
            })
            .OrderBy(order => side == MarketOrderSide.Sell ? order.UnitPrice : -order.UnitPrice)
            .ThenBy(order => order.OrderId)
            .ToArray();
    }

    private static void WriteRollingWindow(Utf8JsonWriter writer, RollingWindowStatistics value)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.WindowDays);
        writer.WriteNumberValue(value.AveragePrice);
        writer.WriteNumberValue(value.AverageDailyVolume);
        writer.WriteNumberValue(value.VolatilityPercent);
        writer.WriteNumberValue(value.P10Price);
        writer.WriteNumberValue(value.MedianPrice);
        writer.WriteNumberValue(value.P90Price);
        writer.WriteNumberValue(value.ExitQualityScore);
        writer.WriteEndArray();
    }

    private static void WriteOrderRows(Utf8JsonWriter writer, IReadOnlyList<MarketOrderRecord> rows)
    {
        writer.WriteStartArray();

        foreach (var row in rows)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(row.OrderId);
            writer.WriteNumberValue(row.UnitPrice);
            writer.WriteNumberValue(row.RemainingVolume);
            writer.WriteNumberValue(row.MinVolume);
            writer.WriteStringValue(row.Range);
            writer.WriteStringValue(FormatInstant(row.IssuedAtUtc));
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static string FormatInstant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static void WriteJsonAtomically(string path, Action<Utf8JsonWriter> write)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (var writer = new Utf8JsonWriter(stream))
            {
                write(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
