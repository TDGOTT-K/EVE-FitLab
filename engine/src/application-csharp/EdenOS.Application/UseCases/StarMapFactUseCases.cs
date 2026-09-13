using EdenOS.Contracts.Application;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class StarMapGetSystemJumpsUseCase(IStarMapFactsService starMapFactsService)
    : IQueryUseCase<GetStarMapSystemJumpsRequest, StarMapSystemJumpCatalog>
{
    public Task<UseCaseResult<StarMapSystemJumpCatalog>> ExecuteAsync(GetStarMapSystemJumpsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapFactsService.GetSystemJumps(request));
    }
}

public sealed class StarMapGetSystemKillsUseCase(IStarMapFactsService starMapFactsService)
    : IQueryUseCase<GetStarMapSystemKillsRequest, StarMapSystemKillCatalog>
{
    public Task<UseCaseResult<StarMapSystemKillCatalog>> ExecuteAsync(GetStarMapSystemKillsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapFactsService.GetSystemKills(request));
    }
}

public sealed class StarMapGetSovereigntyMapUseCase(IStarMapFactsService starMapFactsService)
    : IQueryUseCase<GetStarMapSovereigntyMapRequest, StarMapSovereigntyMapCatalog>
{
    public Task<UseCaseResult<StarMapSovereigntyMapCatalog>> ExecuteAsync(GetStarMapSovereigntyMapRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(starMapFactsService.GetSovereigntyMap(request));
    }
}
