using EdenOS.Contracts.Application;
using EdenOS.Contracts.IdentityDirectory;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class IdentityDirectoryResolveSubjectUseCase(IIdentityDirectoryService identityDirectoryService)
    : IQueryUseCase<ResolveIdentityDirectorySubjectRequest, IdentityDirectoryResolvedSubject>
{
    public Task<UseCaseResult<IdentityDirectoryResolvedSubject>> ExecuteAsync(ResolveIdentityDirectorySubjectRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.ResolveSubject(request));
    }
}

public sealed class IdentityDirectoryProvisionUserFromSubjectUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<ProvisionIdentityDirectoryUserFromSubjectRequest, IdentityDirectoryUserSummary>
{
    public Task<UseCaseResult<IdentityDirectoryUserSummary>> ExecuteAsync(ProvisionIdentityDirectoryUserFromSubjectRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.ProvisionUserFromSubject(request));
    }
}

public sealed class IdentityDirectoryAttachExternalIdentityUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<AttachExternalIdentityRequest, IdentityDirectoryIdentitySummary>
{
    public Task<UseCaseResult<IdentityDirectoryIdentitySummary>> ExecuteAsync(AttachExternalIdentityRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.AttachExternalIdentity(request));
    }
}

public sealed class IdentityDirectoryCreateVirtualIdentityUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<CreateVirtualIdentityRequest, IdentityDirectoryIdentitySummary>
{
    public Task<UseCaseResult<IdentityDirectoryIdentitySummary>> ExecuteAsync(CreateVirtualIdentityRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.CreateVirtualIdentity(request));
    }
}

public sealed class IdentityDirectoryCreateActorUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<CreateIdentityDirectoryActorRequest, IdentityDirectoryActorSummary>
{
    public Task<UseCaseResult<IdentityDirectoryActorSummary>> ExecuteAsync(CreateIdentityDirectoryActorRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.CreateActor(request));
    }
}

public sealed class IdentityDirectoryBootstrapUserSessionUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<BootstrapIdentityDirectoryUserSessionRequest, IdentityDirectoryUserSessionView>
{
    public Task<UseCaseResult<IdentityDirectoryUserSessionView>> ExecuteAsync(BootstrapIdentityDirectoryUserSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.BootstrapUserSession(request));
    }
}

public sealed class IdentityDirectoryListSwitchableActorsUseCase(IIdentityDirectoryService identityDirectoryService)
    : IQueryUseCase<ListSwitchableIdentityDirectoryActorsRequest, IReadOnlyList<IdentityDirectoryActorSummary>>
{
    public Task<UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>>> ExecuteAsync(ListSwitchableIdentityDirectoryActorsRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.ListSwitchableActors(request));
    }
}

public sealed class IdentityDirectorySwitchActiveActorUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<SwitchActiveIdentityDirectoryActorRequest, IdentityDirectoryActorSummary>
{
    public Task<UseCaseResult<IdentityDirectoryActorSummary>> ExecuteAsync(SwitchActiveIdentityDirectoryActorRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.SwitchActiveActor(request));
    }
}

public sealed class IdentityDirectoryAddAccessGrantUseCase(IIdentityDirectoryService identityDirectoryService)
    : ICommandUseCase<AddIdentityDirectoryAccessGrantRequest, IdentityDirectoryAccessGrantSummary>
{
    public Task<UseCaseResult<IdentityDirectoryAccessGrantSummary>> ExecuteAsync(AddIdentityDirectoryAccessGrantRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.AddAccessGrant(request));
    }
}

public sealed class IdentityDirectoryCheckAccessUseCase(IIdentityDirectoryService identityDirectoryService)
    : IQueryUseCase<CheckIdentityDirectoryAccessRequest, IdentityDirectoryAccessDecision>
{
    public Task<UseCaseResult<IdentityDirectoryAccessDecision>> ExecuteAsync(CheckIdentityDirectoryAccessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(identityDirectoryService.CheckAccess(request));
    }
}
