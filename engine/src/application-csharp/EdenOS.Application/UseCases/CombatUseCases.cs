using EdenOS.Contracts.Application;
using EdenOS.Contracts.Combat;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class CombatSimulateDuelUseCase(ICombatSimulationService combatSimulationService)
    : IWorkflowUseCase<SimulateDuelRequest, CombatOutcome>
{
    public Task<UseCaseResult<CombatOutcome>> ExecuteAsync(SimulateDuelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(combatSimulationService.SimulateDuel(request));
    }
}
