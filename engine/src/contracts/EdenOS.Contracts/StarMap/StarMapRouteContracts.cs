namespace EdenOS.Contracts.StarMap;

public sealed record StarMapNeighborSummary(
    long SolarSystemId,
    long RegionId,
    string RegionName,
    string Name,
    decimal? SecurityStatus);

public sealed record StarMapRouteNode(
    long SolarSystemId,
    long RegionId,
    string RegionName,
    string Name,
    decimal? SecurityStatus,
    int JumpIndex);

public sealed record StarMapNeighborsView(
    string BundleVersion,
    long SolarSystemId,
    string SolarSystemName,
    int NeighborCount,
    bool UsesPartialCoverage,
    IReadOnlyList<StarMapNeighborSummary> Neighbors);

public sealed record StarMapRouteView(
    string BundleVersion,
    long FromSolarSystemId,
    string FromSolarSystemName,
    long ToSolarSystemId,
    string ToSolarSystemName,
    bool IsReachable,
    bool UsesPartialCoverage,
    int? JumpCount,
    IReadOnlyList<StarMapRouteNode> Route);
