using EdenOS.Contracts.Application;
using EdenOS.Contracts.CharacterProgression;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class CharacterProgressionGetSkillCatalogUseCase(ICharacterProgressionService progressionService)
    : IQueryUseCase<GetCharacterProgressionSkillCatalogRequest, CharacterProgressionSkillCatalogView>
{
    public Task<UseCaseResult<CharacterProgressionSkillCatalogView>> ExecuteAsync(
        GetCharacterProgressionSkillCatalogRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(progressionService.GetSkillCatalog(request));
    }
}

public sealed class CharacterProgressionSimulateUseCase(ICharacterProgressionService progressionService)
    : IWorkflowUseCase<SimulateCharacterProgressionRequest, CharacterProgressionSimulationView>
{
    public Task<UseCaseResult<CharacterProgressionSimulationView>> ExecuteAsync(
        SimulateCharacterProgressionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(progressionService.Simulate(request));
    }
}
