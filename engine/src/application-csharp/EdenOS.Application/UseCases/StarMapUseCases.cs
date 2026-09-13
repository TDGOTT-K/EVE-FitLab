using EdenOS.Contracts.Application;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class StarMapListRegionsUseCase(IStarMapService starMapService)
    : IQueryUseCase<ListStarMapRegionsRequest, StarMapRegionCatalog>
{
    public Task<UseCaseResult<StarMapRegionCatalog>> ExecuteAsync(ListStarMapRegionsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.ListRegions(request));
    }
}

public sealed class StarMapGetRegionMapUseCase(IStarMapService starMapService)
    : IQueryUseCase<GetRegionStarMapRequest, RegionStarMap>
{
    public Task<UseCaseResult<RegionStarMap>> ExecuteAsync(GetRegionStarMapRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.GetRegionMap(request));
    }
}

public sealed class StarMapSearchSolarSystemsUseCase(IStarMapService starMapService)
    : IQueryUseCase<SearchStarMapSolarSystemsRequest, StarMapSolarSystemCatalog>
{
    public Task<UseCaseResult<StarMapSolarSystemCatalog>> ExecuteAsync(SearchStarMapSolarSystemsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.SearchSolarSystems(request));
    }
}

public sealed class StarMapResolveReferenceUseCase(IStarMapService starMapService)
    : IQueryUseCase<ResolveStarMapReferenceRequest, StarMapNodeResolution>
{
    public Task<UseCaseResult<StarMapNodeResolution>> ExecuteAsync(ResolveStarMapReferenceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.ResolveReference(request));
    }
}
