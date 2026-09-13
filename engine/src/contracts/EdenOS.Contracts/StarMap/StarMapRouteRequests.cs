namespace EdenOS.Contracts.StarMap;

public sealed record GetStarMapNeighborsRequest(
    long SolarSystemId);

public sealed record FindStarMapRouteRequest(
    long FromSolarSystemId,
    long ToSolarSystemId);
