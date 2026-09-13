using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.StarMap;

public interface IStarMapFactsService
{
    UseCaseResult<StarMapSystemJumpCatalog> GetSystemJumps(GetStarMapSystemJumpsRequest request);

    UseCaseResult<StarMapSystemKillCatalog> GetSystemKills(GetStarMapSystemKillsRequest request);

    UseCaseResult<StarMapSovereigntyMapCatalog> GetSovereigntyMap(GetStarMapSovereigntyMapRequest request);
}
