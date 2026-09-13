namespace EdenOS.Contracts.StarMap;

public enum StarMapAnchorKind
{
    Region,
    SolarSystem
}

public enum StarMapReferenceKind
{
    Any,
    Region,
    SolarSystem,
    Station,
    Structure
}

public enum StarMapResolvedKind
{
    Region,
    SolarSystem,
    Station,
    Structure
}

public sealed record StarMapRegionSummary(
    long RegionId,
    string Name,
    int SolarSystemCount);

public sealed record StarMapSolarSystemSummary(
    long SolarSystemId,
    long RegionId,
    string RegionName,
    string Name,
    decimal? SecurityStatus);

public sealed record StarMapRegionCatalog(
    string BundleVersion,
    IReadOnlyList<StarMapRegionSummary> Regions);

public sealed record RegionStarMap(
    string BundleVersion,
    long RegionId,
    string RegionName,
    IReadOnlyList<StarMapSolarSystemSummary> SolarSystems);

public sealed record StarMapSolarSystemCatalog(
    string BundleVersion,
    string SearchText,
    long? RegionId,
    IReadOnlyList<StarMapSolarSystemSummary> SolarSystems);

public sealed record StarMapNodeResolution(
    long ReferenceId,
    StarMapReferenceKind ReferenceKind,
    StarMapResolvedKind ResolvedKind,
    long RegionId,
    string RegionName,
    long? SolarSystemId,
    string? SolarSystemName,
    long AnchorNodeId,
    StarMapAnchorKind AnchorKind,
    string AnchorLabel);
