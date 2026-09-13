using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;

namespace EdenOS.Application.Execution;

internal sealed record ExecutionContextData(
    string WorkspaceId,
    IndustryPlan Plan,
    ExecutionState State,
    PlanComputationResult Computation,
    IReadOnlyList<string> ComputationWarnings,
    PendingTaskPoolView TaskPool);
