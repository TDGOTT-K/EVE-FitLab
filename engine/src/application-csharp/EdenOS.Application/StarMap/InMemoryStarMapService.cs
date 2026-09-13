using EdenOS.Application.Market;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.UseCases;
using System.Globalization;

namespace EdenOS.Application.StarMap;

public sealed class InMemoryStarMapService : IStarMapService
{
    private const int DefaultRegionLimit = 128;
    private const int DefaultRegionMapLimit = 2048;
    private const int DefaultSearchLimit = 32;
    private const int MaxRegionLimit = 512;
    private const int MaxRegionMapLimit = 4096;
    private const int MaxSearchLimit = 256;

    private readonly MetadataBootstrapCatalog _metadata;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<long>> _neighborsBySolarSystemId;
    private readonly bool _usesPartialCoverage;

    public InMemoryStarMapService()
        : this(MetadataBootstrapCatalog.LoadDefault())
    {
    }

    internal InMemoryStarMapService(MetadataBootstrapCatalog metadata)
    {
        _metadata = metadata;
        (_neighborsBySolarSystemId, _usesPartialCoverage) = LoadJumpGraph();
    }

    public UseCaseResult<StarMapRegionCatalog> ListRegions(ListStarMapRegionsRequest request)
    {
        var traceId = CreateTraceId();
        var warnings = BuildWarnings();
        var limit = NormalizeLimit(request.Limit, DefaultRegionLimit, MaxRegionLimit);
        if (limit != request.Limit)
        {
            warnings.Add($"Region limit was normalized to {limit}.");
        }

        IEnumerable<MetadataBootstrapCatalog.MetadataRegionEntry> query = _metadata.ListRegions();
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            query = query.Where(region => MatchesSearch(region.Name, request.SearchText));
        }

        var regions = query
            .OrderBy(region => region.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(region => new StarMapRegionSummary(
                region.RegionId,
                region.Name,
                _metadata.ListSolarSystems(region.RegionId).Count()))
            .ToArray();

        return UseCaseResult<StarMapRegionCatalog>.Success(
            new StarMapRegionCatalog(_metadata.BundleVersion, regions),
            $"Listed {regions.Length} star map regions.",
            traceId,
            warnings);
    }

    public UseCaseResult<RegionStarMap> GetRegionMap(GetRegionStarMapRequest request)
    {
        var traceId = CreateTraceId();
        var warnings = BuildWarnings();
        if (!_metadata.TryGetRegion(request.RegionId, out var region))
        {
            return UseCaseResult<RegionStarMap>.Failure(
                UseCaseStatus.NotFound,
                $"Region '{request.RegionId}' was not found.",
                traceId,
                errors: [$"No star map region exists for id '{request.RegionId}'."]);
        }

        var limit = NormalizeLimit(request.Limit, DefaultRegionMapLimit, MaxRegionMapLimit);
        if (limit != request.Limit)
        {
            warnings.Add($"Region map limit was normalized to {limit}.");
        }

        IEnumerable<MetadataBootstrapCatalog.MetadataSolarSystemEntry> query = _metadata.ListSolarSystems(region.RegionId);
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            query = query.Where(system => MatchesSearch(system.Name, request.SearchText));
        }

        var solarSystems = query
            .OrderBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(system => ToSolarSystemSummary(system, region.Name))
            .ToArray();

        return UseCaseResult<RegionStarMap>.Success(
            new RegionStarMap(
                _metadata.BundleVersion,
                region.RegionId,
                region.Name,
                solarSystems),
            $"Loaded star map region '{region.Name}' with {solarSystems.Length} solar systems.",
            traceId,
            warnings);
    }

    public UseCaseResult<StarMapSolarSystemCatalog> SearchSolarSystems(SearchStarMapSolarSystemsRequest request)
    {
        var traceId = CreateTraceId();
        if (string.IsNullOrWhiteSpace(request.SearchText))
        {
            return UseCaseResult<StarMapSolarSystemCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "Search text is required for solar system lookup.",
                traceId,
                errors: ["Provide a non-empty search_text value."]);
        }

        var warnings = BuildWarnings();
        if (request.RegionId.HasValue && !_metadata.HasRegion(request.RegionId.Value))
        {
            return UseCaseResult<StarMapSolarSystemCatalog>.Failure(
                UseCaseStatus.NotFound,
                $"Region '{request.RegionId.Value}' was not found.",
                traceId,
                errors: [$"No star map region exists for id '{request.RegionId.Value}'."]);
        }

        var limit = NormalizeLimit(request.Limit, DefaultSearchLimit, MaxSearchLimit);
        if (limit != request.Limit)
        {
            warnings.Add($"Solar system search limit was normalized to {limit}.");
        }

        var solarSystems = _metadata.ListSolarSystems(request.RegionId)
            .Where(system => MatchesSearch(system.Name, request.SearchText))
            .Select(system =>
            {
                _metadata.TryGetRegion(system.RegionId, out var region);
                return new
                {
                    Summary = ToSolarSystemSummary(system, region?.Name ?? system.RegionId.ToString()),
                    Score = ScoreMatch(system.Name, request.SearchText)
                };
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Summary.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => entry.Summary)
            .ToArray();

        return UseCaseResult<StarMapSolarSystemCatalog>.Success(
            new StarMapSolarSystemCatalog(
                _metadata.BundleVersion,
                request.SearchText.Trim(),
                request.RegionId,
                solarSystems),
            $"Found {solarSystems.Length} solar systems for '{request.SearchText.Trim()}'.",
            traceId,
            warnings);
    }

    public UseCaseResult<StarMapNodeResolution> ResolveReference(ResolveStarMapReferenceRequest request)
    {
        var traceId = CreateTraceId();

        var resolved = request.ReferenceKind switch
        {
            StarMapReferenceKind.Any => ResolveAnyReference(request.ReferenceId),
            StarMapReferenceKind.Region => ResolveRegionReference(request.ReferenceId),
            StarMapReferenceKind.SolarSystem => ResolveSolarSystemReference(request.ReferenceId),
            StarMapReferenceKind.Station => ResolveLocationReference(request.ReferenceId, Contracts.Market.MarketLocationKind.Station),
            StarMapReferenceKind.Structure => ResolveLocationReference(request.ReferenceId, Contracts.Market.MarketLocationKind.Structure),
            _ => null
        };

        if (resolved is null)
        {
            return UseCaseResult<StarMapNodeResolution>.Failure(
                UseCaseStatus.NotFound,
                $"Star map reference '{request.ReferenceId}' could not be resolved.",
                traceId,
                errors: [$"No star map entity matched id '{request.ReferenceId}' as '{request.ReferenceKind}'."]);
        }

        return UseCaseResult<StarMapNodeResolution>.Success(
            resolved with { ReferenceKind = request.ReferenceKind },
            $"Resolved star map reference '{request.ReferenceId}' to '{resolved.AnchorLabel}'.",
            traceId,
            BuildWarnings());
    }

    public UseCaseResult<StarMapNeighborsView> GetNeighbors(GetStarMapNeighborsRequest request)
    {
        var traceId = CreateTraceId();
        if (!_metadata.TryGetSolarSystem(request.SolarSystemId, out var solarSystem) ||
            !_metadata.TryGetRegion(solarSystem.RegionId, out var region))
        {
            return UseCaseResult<StarMapNeighborsView>.Failure(
                UseCaseStatus.NotFound,
                $"Solar system '{request.SolarSystemId}' was not found.",
                traceId,
                errors: [$"No solar system exists for id '{request.SolarSystemId}'."]);
        }

        var neighbors = _neighborsBySolarSystemId.GetValueOrDefault(request.SolarSystemId, Array.Empty<long>())
            .Select(ToNeighborSummary)
            .Where(summary => summary is not null)
            .Cast<StarMapNeighborSummary>()
            .OrderBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return UseCaseResult<StarMapNeighborsView>.Success(
            new StarMapNeighborsView(
                _metadata.BundleVersion,
                solarSystem.SolarSystemId,
                solarSystem.Name,
                neighbors.Length,
                _usesPartialCoverage,
                neighbors),
            $"Loaded {neighbors.Length} star map neighbors for '{solarSystem.Name}'.",
            traceId,
            BuildWarnings());
    }

    public UseCaseResult<StarMapRouteView> FindRoute(FindStarMapRouteRequest request)
    {
        var traceId = CreateTraceId();
        if (!_metadata.TryGetSolarSystem(request.FromSolarSystemId, out var fromSystem))
        {
            return UseCaseResult<StarMapRouteView>.Failure(
                UseCaseStatus.NotFound,
                $"Source solar system '{request.FromSolarSystemId}' was not found.",
                traceId,
                errors: [$"No solar system exists for id '{request.FromSolarSystemId}'."]);
        }

        if (!_metadata.TryGetSolarSystem(request.ToSolarSystemId, out var toSystem))
        {
            return UseCaseResult<StarMapRouteView>.Failure(
                UseCaseStatus.NotFound,
                $"Destination solar system '{request.ToSolarSystemId}' was not found.",
                traceId,
                errors: [$"No solar system exists for id '{request.ToSolarSystemId}'."]);
        }

        var routeIds = FindShortestRouteIds(request.FromSolarSystemId, request.ToSolarSystemId);
        var route = routeIds
            .Select((solarSystemId, index) => ToRouteNode(solarSystemId, index))
            .Where(node => node is not null)
            .Cast<StarMapRouteNode>()
            .ToArray();

        var isReachable = routeIds.Count > 0;
        int? jumpCount = isReachable ? route.Length - 1 : null;
        var summary = isReachable
            ? $"Resolved star map route from '{fromSystem.Name}' to '{toSystem.Name}' in {jumpCount} jumps."
            : $"No route is currently available from '{fromSystem.Name}' to '{toSystem.Name}' within the local star map graph.";

        return UseCaseResult<StarMapRouteView>.Success(
            new StarMapRouteView(
                _metadata.BundleVersion,
                fromSystem.SolarSystemId,
                fromSystem.Name,
                toSystem.SolarSystemId,
                toSystem.Name,
                isReachable,
                _usesPartialCoverage,
                jumpCount,
                route),
            summary,
            traceId,
            BuildWarnings());
    }

    private StarMapNodeResolution? ResolveAnyReference(long referenceId)
    {
        return ResolveLocationReference(referenceId, expectedKind: null)
            ?? ResolveSolarSystemReference(referenceId)
            ?? ResolveRegionReference(referenceId);
    }

    private StarMapNodeResolution? ResolveRegionReference(long regionId)
    {
        if (!_metadata.TryGetRegion(regionId, out var region))
        {
            return null;
        }

        return new StarMapNodeResolution(
            regionId,
            StarMapReferenceKind.Region,
            StarMapResolvedKind.Region,
            region.RegionId,
            region.Name,
            null,
            null,
            region.RegionId,
            StarMapAnchorKind.Region,
            region.Name);
    }

    private StarMapNodeResolution? ResolveSolarSystemReference(long solarSystemId)
    {
        if (!_metadata.TryGetSolarSystem(solarSystemId, out var solarSystem) ||
            !_metadata.TryGetRegion(solarSystem.RegionId, out var region))
        {
            return null;
        }

        return new StarMapNodeResolution(
            solarSystemId,
            StarMapReferenceKind.SolarSystem,
            StarMapResolvedKind.SolarSystem,
            region.RegionId,
            region.Name,
            solarSystem.SolarSystemId,
            solarSystem.Name,
            solarSystem.SolarSystemId,
            StarMapAnchorKind.SolarSystem,
            solarSystem.Name);
    }

    private StarMapNodeResolution? ResolveLocationReference(long locationId, Contracts.Market.MarketLocationKind? expectedKind)
    {
        if (!_metadata.TryGetLocation(locationId, out var location))
        {
            return null;
        }

        if (expectedKind.HasValue && location.Kind != expectedKind.Value)
        {
            return null;
        }

        if (!_metadata.TryGetRegion(location.RegionId, out var region))
        {
            return null;
        }

        return new StarMapNodeResolution(
            location.LocationId,
            location.Kind == Contracts.Market.MarketLocationKind.Station
                ? StarMapReferenceKind.Station
                : StarMapReferenceKind.Structure,
            location.Kind == Contracts.Market.MarketLocationKind.Station
                ? StarMapResolvedKind.Station
                : StarMapResolvedKind.Structure,
            region.RegionId,
            region.Name,
            location.SolarSystemId,
            location.SolarSystemName,
            location.SolarSystemId,
            StarMapAnchorKind.SolarSystem,
            location.SolarSystemName);
    }

    private static bool MatchesSearch(string value, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return value.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreMatch(string value, string searchText)
    {
        var normalizedQuery = searchText.Trim();
        if (string.Equals(value, normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (value.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static int NormalizeLimit(int requested, int fallback, int max)
    {
        if (requested <= 0)
        {
            return fallback;
        }

        return Math.Min(requested, max);
    }

    private StarMapNeighborSummary? ToNeighborSummary(long solarSystemId)
    {
        if (!_metadata.TryGetSolarSystem(solarSystemId, out var solarSystem) ||
            !_metadata.TryGetRegion(solarSystem.RegionId, out var region))
        {
            return null;
        }

        return new StarMapNeighborSummary(
            solarSystem.SolarSystemId,
            solarSystem.RegionId,
            region.Name,
            solarSystem.Name,
            solarSystem.SecurityStatus);
    }

    private StarMapRouteNode? ToRouteNode(long solarSystemId, int jumpIndex)
    {
        if (!_metadata.TryGetSolarSystem(solarSystemId, out var solarSystem) ||
            !_metadata.TryGetRegion(solarSystem.RegionId, out var region))
        {
            return null;
        }

        return new StarMapRouteNode(
            solarSystem.SolarSystemId,
            solarSystem.RegionId,
            region.Name,
            solarSystem.Name,
            solarSystem.SecurityStatus,
            jumpIndex);
    }

    private IReadOnlyList<long> FindShortestRouteIds(long fromSolarSystemId, long toSolarSystemId)
    {
        if (fromSolarSystemId == toSolarSystemId)
        {
            return [fromSolarSystemId];
        }

        var queue = new Queue<long>();
        var visited = new HashSet<long> { fromSolarSystemId };
        var previous = new Dictionary<long, long>();
        queue.Enqueue(fromSolarSystemId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in _neighborsBySolarSystemId.GetValueOrDefault(current, Array.Empty<long>()))
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                previous[neighbor] = current;
                if (neighbor == toSolarSystemId)
                {
                    return BuildRoute(previous, fromSolarSystemId, toSolarSystemId);
                }

                queue.Enqueue(neighbor);
            }
        }

        return Array.Empty<long>();
    }

    private static IReadOnlyList<long> BuildRoute(
        IReadOnlyDictionary<long, long> previous,
        long fromSolarSystemId,
        long toSolarSystemId)
    {
        var route = new List<long> { toSolarSystemId };
        var current = toSolarSystemId;

        while (previous.TryGetValue(current, out var parent))
        {
            route.Add(parent);
            if (parent == fromSolarSystemId)
            {
                break;
            }

            current = parent;
        }

        route.Reverse();
        return route;
    }

    private static (IReadOnlyDictionary<long, IReadOnlyList<long>> Graph, bool UsesPartialCoverage) LoadJumpGraph()
    {
        var manifestPath = FindManifestPath();
        var jumpsPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "solar_system_jumps.tsv");
        if (!File.Exists(jumpsPath))
        {
            return (new Dictionary<long, IReadOnlyList<long>>(), true);
        }

        var allLines = File.ReadAllLines(jumpsPath);
        var coverageIsFull = allLines.Any(line =>
            line.Trim().Equals("# coverage=full_sde_map_stargates", StringComparison.OrdinalIgnoreCase));
        var lines = allLines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToArray();
        if (lines.Length <= 1)
        {
            return (new Dictionary<long, IReadOnlyList<long>>(), !coverageIsFull);
        }

        var map = new Dictionary<long, HashSet<long>>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            var fromId = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var toId = long.Parse(parts[1], CultureInfo.InvariantCulture);
            if (!map.TryGetValue(fromId, out var neighbors))
            {
                neighbors = new HashSet<long>();
                map[fromId] = neighbors;
            }

            neighbors.Add(toId);
        }

        return (
            map.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<long>)pair.Value.OrderBy(value => value).ToArray()),
            !coverageIsFull);
    }

    private static string FindManifestPath()
    {
        if (RepositoryPathResolver.TryResolveExistingFile(out var manifestPath, "data", "static-data", "metadata", "manifest.txt"))
        {
            return manifestPath;
        }

        throw new DirectoryNotFoundException(
            "Could not locate metadata manifest. Expected EdenOsRewrite.sln and data/static-data/metadata/manifest.txt.");
    }

    private StarMapSolarSystemSummary ToSolarSystemSummary(
        MetadataBootstrapCatalog.MetadataSolarSystemEntry solarSystem,
        string regionName)
    {
        return new StarMapSolarSystemSummary(
            solarSystem.SolarSystemId,
            solarSystem.RegionId,
            regionName,
            solarSystem.Name,
            solarSystem.SecurityStatus);
    }

    private List<string> BuildWarnings()
    {
        var warnings = new List<string>
        {
            $"Star map results are served from metadata bundle '{_metadata.BundleVersion}'."
        };

        if (_usesPartialCoverage)
        {
            warnings.Add("Star map jump routing currently uses partial local graph coverage.");
        }

        if (_metadata.NonActiveSourceKeys.Count > 0)
        {
            warnings.Add(
                $"Some metadata sources remain non-active: {string.Join(", ", _metadata.NonActiveSourceKeys.OrderBy(key => key, StringComparer.Ordinal))}.");
        }

        return warnings;
    }

    private static string CreateTraceId()
    {
        return $"starmap-{Guid.NewGuid():N}";
    }
}
