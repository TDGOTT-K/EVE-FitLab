using EdenOS.Application.Accounts;
using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

public static class MarketFactsServiceCollection
{
    public static IMarketFactsService CreateInMemory() => new InMemoryMarketFactsService();

    public static IMarketFactsService CreateInMemory(IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService) =>
        new InMemoryMarketFactsService(workspaceBoundMarketAccessService);
}
