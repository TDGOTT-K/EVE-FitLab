using EdenOS.Contracts.Market;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.CharacterPool;

namespace EdenOS.Application.Planning;

public static class IndustryPlanComputationServiceCollection
{
    public static IIndustryPlanComputationService CreateInMemory(
        IIndustryPlanService planService,
        IMarketFactsService marketFactsService,
        ICharacterPoolService? characterPoolService = null) =>
        new InMemoryIndustryPlanComputationService(planService, marketFactsService, characterPoolService);
}
