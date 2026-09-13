using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.StarMap;

public interface IStarMapService
{
    UseCaseResult<StarMapRegionCatalog> ListRegions(ListStarMapRegionsRequest request);

    UseCaseResult<RegionStarMap> GetRegionMap(GetRegionStarMapRequest request);

    UseCaseResult<StarMapSolarSystemCatalog> SearchSolarSystems(SearchStarMapSolarSystemsRequest request);

    UseCaseResult<StarMapNodeResolution> ResolveReference(ResolveStarMapReferenceRequest request);

    UseCaseResult<StarMapNeighborsView> GetNeighbors(GetStarMapNeighborsRequest request);

    UseCaseResult<StarMapRouteView> FindRoute(FindStarMapRouteRequest request);
}
