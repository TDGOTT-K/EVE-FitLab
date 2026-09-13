namespace EdenOS.Contracts.Accounts;

public enum CredentialProvider
{
    Esi,
    Local
}

public sealed record CredentialBoundPrincipal
{
    public required CredentialProvider Provider { get; init; }

    public required string SubjectCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public string? PlatformSubjectId { get; init; }

    public string? PlatformDisplayName { get; init; }

    public string? PrimaryEmail { get; init; }
}

public sealed record AccountPropagationPolicy
{
    public bool RequiresCredentialBinding { get; init; } = true;

    public bool AllowsExplicitPropagationForTrustedServicesOnly { get; init; } = true;

    public bool RejectsBareAccountIdAtClientBoundary { get; init; } = true;
}

public sealed record AccountContext
{
    public required string AccountId { get; init; }

    public required CredentialBoundPrincipal Principal { get; init; }

    public required string IsolationKey { get; init; }

    public string? UserId { get; init; }

    public string? SubjectId { get; init; }

    public string? ActiveActorId { get; init; }

    public AccountPropagationPolicy PropagationPolicy { get; init; } = new();
}
