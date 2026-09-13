using EdenOS.Contracts.Application;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.UseCases;

public sealed class WorkspaceOpenUseCase(IWorkspaceService workspaceService)
    : ICommandUseCase<OpenWorkspaceRequest, WorkspaceContext>
{
    public Task<UseCaseResult<WorkspaceContext>> ExecuteAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceService.Open(request));
    }
}

public sealed class WorkspaceGetSummaryUseCase(IWorkspaceService workspaceService)
    : IQueryUseCase<GetWorkspaceSummaryRequest, WorkspaceSummary>
{
    public Task<UseCaseResult<WorkspaceSummary>> ExecuteAsync(
        GetWorkspaceSummaryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceService.GetSummary(request));
    }
}

public sealed class CharacterPoolListUseCase(ICharacterPoolService characterPoolService)
    : IQueryUseCase<ListCharacterPoolRequest, CharacterPoolView>
{
    public Task<UseCaseResult<CharacterPoolView>> ExecuteAsync(
        ListCharacterPoolRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(characterPoolService.List(request));
    }
}

public sealed class OperatorPoolListUseCase(IOperatorPoolService operatorPoolService)
    : IQueryUseCase<ListOperatorPoolRequest, OperatorPoolView>
{
    public Task<UseCaseResult<OperatorPoolView>> ExecuteAsync(
        ListOperatorPoolRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operatorPoolService.ListOperators(request));
    }
}

public sealed class CharacterPoolAddVirtualCharacterUseCase(ICharacterPoolService characterPoolService)
    : ICommandUseCase<AddVirtualCharacterRequest, CharacterPoolEntry>
{
    public Task<UseCaseResult<CharacterPoolEntry>> ExecuteAsync(
        AddVirtualCharacterRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(characterPoolService.AddVirtualCharacter(request));
    }
}

public sealed class OperatorPoolAddUseCase(IOperatorPoolService operatorPoolService)
    : ICommandUseCase<AddOperatorRequest, OperatorPoolEntry>
{
    public Task<UseCaseResult<OperatorPoolEntry>> ExecuteAsync(
        AddOperatorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operatorPoolService.AddOperator(request));
    }
}

public sealed class CharacterPoolUpdateVirtualCharacterUseCase(ICharacterPoolService characterPoolService)
    : ICommandUseCase<UpdateVirtualCharacterRequest, CharacterPoolEntry>
{
    public Task<UseCaseResult<CharacterPoolEntry>> ExecuteAsync(
        UpdateVirtualCharacterRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(characterPoolService.UpdateVirtualCharacter(request));
    }
}

public sealed class OperatorPoolUpdateUseCase(IOperatorPoolService operatorPoolService)
    : ICommandUseCase<UpdateOperatorRequest, OperatorPoolEntry>
{
    public Task<UseCaseResult<OperatorPoolEntry>> ExecuteAsync(
        UpdateOperatorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operatorPoolService.UpdateOperator(request));
    }
}

public sealed class CharacterPoolAttachEsiCharacterUseCase(ICharacterPoolService characterPoolService)
    : ICommandUseCase<AttachEsiCharacterRequest, CharacterPoolEntry>
{
    public Task<UseCaseResult<CharacterPoolEntry>> ExecuteAsync(
        AttachEsiCharacterRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(characterPoolService.AttachEsiCharacter(request));
    }
}

public sealed class CharacterPoolUpdateEsiCharacterCapabilityProfileUseCase(ICharacterPoolService characterPoolService)
    : ICommandUseCase<UpdateEsiCharacterCapabilityProfileRequest, CharacterPoolEntry>
{
    public Task<UseCaseResult<CharacterPoolEntry>> ExecuteAsync(
        UpdateEsiCharacterCapabilityProfileRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(characterPoolService.UpdateEsiCharacterCapabilityProfile(request));
    }
}

public sealed class OperatorPoolAssignCharacterUseCase(IOperatorPoolService operatorPoolService)
    : ICommandUseCase<AssignCharacterOperatorRequest, CharacterPoolEntry>
{
    public Task<UseCaseResult<CharacterPoolEntry>> ExecuteAsync(
        AssignCharacterOperatorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(operatorPoolService.AssignCharacterOperator(request));
    }
}
