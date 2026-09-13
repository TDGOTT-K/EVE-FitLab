using EdenOS.Contracts.IdentityDirectory;

namespace EdenOS.Application.RuntimeState;

internal sealed record IdentityDirectoryRuntimeState
{
    public IReadOnlyList<IdentityDirectoryUserSummary> Users { get; init; } = Array.Empty<IdentityDirectoryUserSummary>();

    public IReadOnlyList<IdentityDirectoryIdentitySummary> Identities { get; init; } = Array.Empty<IdentityDirectoryIdentitySummary>();

    public IReadOnlyList<IdentityDirectoryActorSummary> Actors { get; init; } = Array.Empty<IdentityDirectoryActorSummary>();

    public IReadOnlyList<IdentityDirectoryOrganizationSummary> Organizations { get; init; } = Array.Empty<IdentityDirectoryOrganizationSummary>();

    public IReadOnlyList<IdentityDirectoryMembershipSummary> Memberships { get; init; } = Array.Empty<IdentityDirectoryMembershipSummary>();

    public IReadOnlyList<IdentityDirectoryAccessGrantSummary> AccessGrants { get; init; } = Array.Empty<IdentityDirectoryAccessGrantSummary>();

    public IReadOnlyList<IdentityDirectoryAuditAliasSummary> AuditAliases { get; init; } = Array.Empty<IdentityDirectoryAuditAliasSummary>();

    public IReadOnlyList<PersistedIdentityDirectoryActiveActorSelection> ActiveActorSelections { get; init; } = Array.Empty<PersistedIdentityDirectoryActiveActorSelection>();

    public IReadOnlyList<PersistedIdentityDirectoryActorSwitchRecord> ActorSwitchHistory { get; init; } = Array.Empty<PersistedIdentityDirectoryActorSwitchRecord>();
}

internal sealed record PersistedIdentityDirectoryActiveActorSelection
{
    public required string UserId { get; init; }

    public required string ActorId { get; init; }
}

internal sealed record PersistedIdentityDirectoryActorSwitchRecord
{
    public required string SwitchId { get; init; }

    public required string UserId { get; init; }

    public string? FromActorId { get; init; }

    public required string ToActorId { get; init; }

    public string? ClientId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
