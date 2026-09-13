using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Authentication;

public enum EsiCredentialSourceKind
{
    FrontendTokenRelay,
    FrontendAuthorizationCode,
    AuthorizationCodePkce,
    DevFixtureImport,
    InternalRefresh
}

public enum EsiAccessTokenStateKind
{
    Fresh,
    ExpiringSoon,
    Expired,
    RefreshUnavailable,
    RefreshFailed
}

public sealed record BindEsiCredentialRequest
{
    public required string WorkspaceId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string? OwnerHash { get; init; }

    public string? OauthClientId { get; init; }

    public string? TokenEndpoint { get; init; }

    public string? TokenType { get; init; }

    public EsiCredentialSourceKind CredentialSource { get; init; } = EsiCredentialSourceKind.FrontendTokenRelay;

    public string? Notes { get; init; }
}

public sealed record GetEsiCredentialStatusRequest
{
    public required string WorkspaceId { get; init; }

    public required string EsiCharacterId { get; init; }

    public int ExpiringSoonWindowSeconds { get; init; } = 300;
}

public sealed record ResolveEsiAccessTokenRequest
{
    public required string WorkspaceId { get; init; }

    public required string EsiCharacterId { get; init; }

    public int MinimumValiditySeconds { get; init; } = 300;

    public bool AllowRefresh { get; init; } = true;
}

public sealed record EsiCredentialStatusView
{
    public required string WorkspaceId { get; init; }

    public string? UserId { get; init; }

    public string? SubjectId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public EsiAccessTokenStateKind AccessTokenState { get; init; }

    public bool HasRefreshToken { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string? OwnerHash { get; init; }

    public string? OauthClientId { get; init; }

    public string? TokenEndpoint { get; init; }

    public string? TokenType { get; init; }

    public EsiCredentialSourceKind CredentialSource { get; init; }

    public DateTimeOffset BoundAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? LastRefreshAtUtc { get; init; }

    public DateTimeOffset? LastRefreshAttemptAtUtc { get; init; }

    public string? LastRefreshError { get; init; }

    public string? Notes { get; init; }
}

public sealed record EsiAccessTokenEnvelope
{
    public required string WorkspaceId { get; init; }

    public string? UserId { get; init; }

    public string? SubjectId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public required string AccessToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string? OwnerHash { get; init; }

    public string? TokenType { get; init; }

    public bool WasRefreshed { get; init; }

    public DateTimeOffset ResolvedAtUtc { get; init; }
}

public interface IEsiCredentialService
{
    Task<UseCaseResult<EsiCredentialStatusView>> BindAsync(
        BindEsiCredentialRequest request,
        CancellationToken cancellationToken);

    Task<UseCaseResult<EsiCredentialStatusView>> GetStatusAsync(
        GetEsiCredentialStatusRequest request,
        CancellationToken cancellationToken);

    Task<UseCaseResult<EsiAccessTokenEnvelope>> ResolveAccessTokenAsync(
        ResolveEsiAccessTokenRequest request,
        CancellationToken cancellationToken);
}
