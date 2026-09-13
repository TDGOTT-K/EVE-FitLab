using EdenOS.Contracts.Application;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class EsiCredentialBindUseCase(IEsiCredentialService esiCredentialService)
    : ICommandUseCase<BindEsiCredentialRequest, EsiCredentialStatusView>
{
    public Task<UseCaseResult<EsiCredentialStatusView>> ExecuteAsync(
        BindEsiCredentialRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return esiCredentialService.BindAsync(request, cancellationToken);
    }
}

public sealed class EsiCredentialGetStatusUseCase(IEsiCredentialService esiCredentialService)
    : IQueryUseCase<GetEsiCredentialStatusRequest, EsiCredentialStatusView>
{
    public Task<UseCaseResult<EsiCredentialStatusView>> ExecuteAsync(
        GetEsiCredentialStatusRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return esiCredentialService.GetStatusAsync(request, cancellationToken);
    }
}

public sealed class EsiCredentialResolveAccessTokenUseCase(IEsiCredentialService esiCredentialService)
    : IWorkflowUseCase<ResolveEsiAccessTokenRequest, EsiAccessTokenEnvelope>
{
    public Task<UseCaseResult<EsiAccessTokenEnvelope>> ExecuteAsync(
        ResolveEsiAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return esiCredentialService.ResolveAccessTokenAsync(request, cancellationToken);
    }
}
