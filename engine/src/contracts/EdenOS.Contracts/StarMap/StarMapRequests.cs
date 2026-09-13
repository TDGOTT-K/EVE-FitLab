namespace EdenOS.Contracts.StarMap;

public sealed record ListStarMapRegionsRequest(
    string? SearchText = null,
    int Limit = 128);

public sealed record GetRegionStarMapRequest(
    long RegionId,
    string? SearchText = null,
    int Limit = 2048);

public sealed record SearchStarMapSolarSystemsRequest(
    string SearchText,
    long? RegionId = null,
    int Limit = 32);

public sealed record ResolveStarMapReferenceRequest(
    long ReferenceId,
    StarMapReferenceKind ReferenceKind = StarMapReferenceKind.Any);
