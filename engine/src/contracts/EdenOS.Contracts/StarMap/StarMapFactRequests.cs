namespace EdenOS.Contracts.StarMap;

public sealed record GetStarMapSystemJumpsRequest(
    IReadOnlyList<long>? SolarSystemIds = null);

public sealed record GetStarMapSystemKillsRequest(
    IReadOnlyList<long>? SolarSystemIds = null);

public sealed record GetStarMapSovereigntyMapRequest(
    IReadOnlyList<long>? SolarSystemIds = null);
