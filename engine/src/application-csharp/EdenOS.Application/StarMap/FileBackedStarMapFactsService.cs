using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.StarMap;

public sealed class FileBackedStarMapFactsService : IStarMapFactsService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _factsDirectoryPath;

    public FileBackedStarMapFactsService(string? factsDirectoryPath = null)
    {
        _factsDirectoryPath = StarMapFactsDataPaths.ResolveRootPath(factsDirectoryPath);
    }

    public UseCaseResult<StarMapSystemJumpCatalog> GetSystemJumps(GetStarMapSystemJumpsRequest request)
    {
        if (!TryNormalizeSolarSystemFilter<StarMapSystemJumpCatalog>(
                request.SolarSystemIds,
                "system_jumps",
                out var solarSystemIds,
                out var errorResult))
        {
            return errorResult!;
        }

        return ReadPartition(
            "system_jumps",
            "system-jumps.json",
            document => MapSystemJumpCatalog(document, solarSystemIds));
    }

    public UseCaseResult<StarMapSystemKillCatalog> GetSystemKills(GetStarMapSystemKillsRequest request)
    {
        if (!TryNormalizeSolarSystemFilter<StarMapSystemKillCatalog>(
                request.SolarSystemIds,
                "system_kills",
                out var solarSystemIds,
                out var errorResult))
        {
            return errorResult!;
        }

        return ReadPartition(
            "system_kills",
            "system-kills.json",
            document => MapSystemKillCatalog(document, solarSystemIds));
    }

    public UseCaseResult<StarMapSovereigntyMapCatalog> GetSovereigntyMap(GetStarMapSovereigntyMapRequest request)
    {
        if (!TryNormalizeSolarSystemFilter<StarMapSovereigntyMapCatalog>(
                request.SolarSystemIds,
                "sovereignty_map",
                out var solarSystemIds,
                out var errorResult))
        {
            return errorResult!;
        }

        return ReadPartition(
            "sovereignty_map",
            "sovereignty-map.json",
            document => MapSovereigntyMapCatalog(document, solarSystemIds));
    }

    private UseCaseResult<TCatalog> ReadPartition<TCatalog>(
        string partitionName,
        string fileName,
        Func<StarMapFactDocument, TCatalog> map)
    {
        var traceId = CreateTraceId(partitionName);
        var path = Path.Combine(_factsDirectoryPath, fileName);
        if (!File.Exists(path))
        {
            return UseCaseResult<TCatalog>.Failure(
                UseCaseStatus.DependencyUnavailable,
                $"Star map fact partition '{partitionName}' is not available.",
                traceId,
                errors: [$"Expected fact file '{path}' was not found."]);
        }

        try
        {
            var document = JsonSerializer.Deserialize<StarMapFactDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Fact file '{path}' did not contain a JSON object.");
            var catalog = map(document);
            var warnings = BuildWarnings(partitionName, catalog, path);

            return UseCaseResult<TCatalog>.Success(
                catalog,
                $"Loaded star map fact partition '{partitionName}'.",
                traceId,
                warnings);
        }
        catch (JsonException ex)
        {
            return UseCaseResult<TCatalog>.Failure(
                UseCaseStatus.Error,
                $"Star map fact partition '{partitionName}' could not be parsed.",
                traceId,
                errors: [$"Failed to parse '{path}': {ex.Message}"]);
        }
        catch (InvalidDataException ex)
        {
            return UseCaseResult<TCatalog>.Failure(
                UseCaseStatus.Error,
                $"Star map fact partition '{partitionName}' is invalid.",
                traceId,
                errors: [ex.Message]);
        }
    }

    private List<string> BuildWarnings<TCatalog>(string partitionName, TCatalog catalog, string path)
    {
        var warnings = new List<string>
        {
            $"Star map facts were read from '{path}'."
        };

        var status = catalog switch
        {
            StarMapSystemJumpCatalog jumps => jumps.Status,
            StarMapSystemKillCatalog kills => kills.Status,
            StarMapSovereigntyMapCatalog sovereignty => sovereignty.Status,
            _ => StarMapFactStatus.Success
        };

        if (status != StarMapFactStatus.Success)
        {
            warnings.Add(
                $"Partition '{partitionName}' currently reports status '{ToSnakeCase(status)}'.");
        }

        return warnings;
    }

    private static StarMapSystemJumpCatalog MapSystemJumpCatalog(
        StarMapFactDocument document,
        IReadOnlySet<long>? filterSolarSystemIds)
    {
        var metadata = MapMetadata("system_jumps", document);
        var items = document.Items
            .Select(MapSystemJumpEntry)
            .Where(item => filterSolarSystemIds is null || filterSolarSystemIds.Contains(item.SolarSystemId))
            .ToArray();

        return new StarMapSystemJumpCatalog(
            metadata.SchemaVersion,
            metadata.GeneratedAtUtc,
            metadata.ObservedAtUtc,
            metadata.Source,
            metadata.Datasource,
            metadata.Status,
            items);
    }

    private static StarMapSystemKillCatalog MapSystemKillCatalog(
        StarMapFactDocument document,
        IReadOnlySet<long>? filterSolarSystemIds)
    {
        var metadata = MapMetadata("system_kills", document);
        var items = document.Items
            .Select(MapSystemKillEntry)
            .Where(item => filterSolarSystemIds is null || filterSolarSystemIds.Contains(item.SolarSystemId))
            .ToArray();

        return new StarMapSystemKillCatalog(
            metadata.SchemaVersion,
            metadata.GeneratedAtUtc,
            metadata.ObservedAtUtc,
            metadata.Source,
            metadata.Datasource,
            metadata.Status,
            items);
    }

    private static StarMapSovereigntyMapCatalog MapSovereigntyMapCatalog(
        StarMapFactDocument document,
        IReadOnlySet<long>? filterSolarSystemIds)
    {
        var metadata = MapMetadata("sovereignty_map", document);
        var items = document.Items
            .Select(MapSovereigntyMapEntry)
            .Where(item => filterSolarSystemIds is null || filterSolarSystemIds.Contains(item.SolarSystemId))
            .ToArray();

        return new StarMapSovereigntyMapCatalog(
            metadata.SchemaVersion,
            metadata.GeneratedAtUtc,
            metadata.ObservedAtUtc,
            metadata.Source,
            metadata.Datasource,
            metadata.Status,
            items);
    }

    private static StarMapFactMetadata MapMetadata(string partitionName, StarMapFactDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            throw new InvalidDataException($"Partition '{partitionName}' is missing 'schema_version'.");
        }

        if (document.GeneratedAtUtc is null)
        {
            throw new InvalidDataException($"Partition '{partitionName}' is missing 'generated_at_utc'.");
        }

        if (string.IsNullOrWhiteSpace(document.Source))
        {
            throw new InvalidDataException($"Partition '{partitionName}' is missing 'source'.");
        }

        if (string.IsNullOrWhiteSpace(document.Datasource))
        {
            throw new InvalidDataException($"Partition '{partitionName}' is missing 'datasource'.");
        }

        if (!TryParseStatus(document.Status, out var status))
        {
            throw new InvalidDataException(
                $"Partition '{partitionName}' has unsupported status '{document.Status ?? "<null>"}'.");
        }

        return new StarMapFactMetadata(
            document.SchemaVersion,
            document.GeneratedAtUtc.Value,
            document.ObservedAtUtc,
            document.Source,
            document.Datasource,
            status);
    }

    private static StarMapSystemJumpEntry MapSystemJumpEntry(StarMapFactItemDocument item)
    {
        return new StarMapSystemJumpEntry(
            RequireLong(item.SolarSystemId, "solar_system_id"),
            RequireLong(item.ShipJumps, "ship_jumps"),
            RequireTimestamp(item.ObservedAtUtc, "observed_at_utc"));
    }

    private static StarMapSystemKillEntry MapSystemKillEntry(StarMapFactItemDocument item)
    {
        return new StarMapSystemKillEntry(
            RequireLong(item.SolarSystemId, "solar_system_id"),
            RequireLong(item.ShipKills, "ship_kills"),
            RequireLong(item.NpcKills, "npc_kills"),
            RequireLong(item.PodKills, "pod_kills"),
            RequireTimestamp(item.ObservedAtUtc, "observed_at_utc"));
    }

    private static StarMapSovereigntyMapEntry MapSovereigntyMapEntry(StarMapFactItemDocument item)
    {
        return new StarMapSovereigntyMapEntry(
            RequireLong(item.SolarSystemId, "solar_system_id"),
            item.AllianceId,
            item.CorporationId,
            item.FactionId,
            RequireTimestamp(item.ObservedAtUtc, "observed_at_utc"));
    }

    private static long RequireLong(long? value, string fieldName)
    {
        if (value is null)
        {
            throw new InvalidDataException($"Star map fact item is missing '{fieldName}'.");
        }

        return value.Value;
    }

    private static DateTimeOffset RequireTimestamp(DateTimeOffset? value, string fieldName)
    {
        if (value is null)
        {
            throw new InvalidDataException($"Star map fact item is missing '{fieldName}'.");
        }

        return value.Value;
    }

    private static bool TryNormalizeSolarSystemFilter<TCatalog>(
        IReadOnlyList<long>? solarSystemIds,
        string partitionName,
        out IReadOnlySet<long>? normalizedSolarSystemIds,
        out UseCaseResult<TCatalog>? errorResult)
    {
        if (solarSystemIds is null || solarSystemIds.Count == 0)
        {
            normalizedSolarSystemIds = null;
            errorResult = null;
            return true;
        }

        if (solarSystemIds.Any(id => id <= 0))
        {
            normalizedSolarSystemIds = null;
            errorResult = UseCaseResult<TCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                $"Partition '{partitionName}' requires positive solar system ids.",
                CreateTraceId(partitionName),
                errors: ["Solar system ids must be positive integers."]);
            return false;
        }

        normalizedSolarSystemIds = solarSystemIds.ToHashSet();
        errorResult = null;
        return true;
    }

    private static bool TryParseStatus(string? rawStatus, out StarMapFactStatus status)
    {
        status = StarMapFactStatus.Success;
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return false;
        }

        var normalized = rawStatus
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);

        foreach (var name in Enum.GetNames<StarMapFactStatus>())
        {
            if (string.Equals(
                    name.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<StarMapFactStatus>(name, ignoreCase: true);
                return true;
            }
        }

        return false;
    }

    private static string ToSnakeCase(StarMapFactStatus status)
    {
        return status switch
        {
            StarMapFactStatus.NotAuthorized => "not_authorized",
            StarMapFactStatus.NotSupported => "not_supported",
            _ => status.ToString().ToLowerInvariant()
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return global::EdenOS.Application.JsonSerializerOptionsFactory.CreateSnakeCase(propertyNameCaseInsensitive: true);
    }

    private static string CreateTraceId(string partitionName)
    {
        return $"starmap-facts.{partitionName}:{Guid.NewGuid():N}";
    }

    private sealed record StarMapFactMetadata(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset? ObservedAtUtc,
        string Source,
        string Datasource,
        StarMapFactStatus Status);

    private sealed class StarMapFactDocument
    {
        public string? SchemaVersion { get; init; }

        public DateTimeOffset? GeneratedAtUtc { get; init; }

        public DateTimeOffset? ObservedAtUtc { get; init; }

        public string? Source { get; init; }

        public string? Datasource { get; init; }

        public string? Status { get; init; }

        public IReadOnlyList<StarMapFactItemDocument> Items { get; init; } = Array.Empty<StarMapFactItemDocument>();
    }

    private sealed class StarMapFactItemDocument
    {
        public long? SolarSystemId { get; init; }

        public long? ShipJumps { get; init; }

        public long? ShipKills { get; init; }

        public long? NpcKills { get; init; }

        public long? PodKills { get; init; }

        public long? AllianceId { get; init; }

        public long? CorporationId { get; init; }

        public long? FactionId { get; init; }

        public DateTimeOffset? ObservedAtUtc { get; init; }
    }
}
