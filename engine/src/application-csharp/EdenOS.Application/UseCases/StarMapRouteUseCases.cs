using EdenOS.Contracts.Application;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class StarMapGetNeighborsUseCase(IStarMapService starMapService)
    : IQueryUseCase<GetStarMapNeighborsRequest, StarMapNeighborsView>
{
    public Task<UseCaseResult<StarMapNeighborsView>> ExecuteAsync(GetStarMapNeighborsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.GetNeighbors(request));
    }
}

public sealed class StarMapFindRouteUseCase(IStarMapService starMapService)
    : IQueryUseCase<FindStarMapRouteRequest, StarMapRouteView>
{
    public Task<UseCaseResult<StarMapRouteView>> ExecuteAsync(FindStarMapRouteRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapService.FindRoute(request));
    }
}
