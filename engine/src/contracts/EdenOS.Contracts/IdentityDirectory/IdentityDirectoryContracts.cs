namespace EdenOS.Contracts.IdentityDirectory;

public enum IdentityDirectoryUserStatus
{
    Active,
    Disabled,
    PendingReview,
    Deleted
}

public enum IdentityDirectoryIdentityType
{
    EsiCharacter,
    VirtualCharacter,
    TestCharacter,
    ProtectedInformant,
    ServiceIdentity
}

public enum IdentityDirectoryIdentityStatus
{
    Active,
    PendingVerification,
    Revoked,
    Disabled
}

public enum IdentityDirectoryActorType
{
    Personal,
    OrganizationMember,
    ProtectedInformant,
    ServiceActor,
    TestActor
}

public enum IdentityDirectoryActorVisibilityMode
{
    Public,
    OrganizationOnly,
    Restricted,
    ProtectedAlias
}

public enum IdentityDirectoryActorStatus
{
    Active,
    Disabled,
    Archived
}

public enum IdentityDirectoryOrganizationType
{
    Corporation,
    Alliance,
    Team,
    CustomGroup,
    TaskGroup
}

public enum IdentityDirectoryOrganizationStatus
{
    Active,
    Disabled,
    Archived
}

public enum IdentityDirectoryMembershipStatus
{
    Pending,
    Active,
    Suspended,
    Revoked,
    Left
}

public enum IdentityDirectoryGrantSubjectType
{
    User,
    Actor,
    Organization,
    Service
}

public enum IdentityDirectoryGrantEffect
{
    Allow,
    Deny
}

public enum IdentityDirectoryGrantSource
{
    Manual,
    MembershipRole,
    SystemProjection,
    PolicyBinding
}

public enum IdentityDirectoryAliasMode
{
    StablePublicAlias,
    RestrictedAlias,
    OneTimeAlias
}

public sealed record IdentityDirectoryUserSummary
{
    public required string UserId { get; init; }

    public required string SubjectId { get; init; }

    public required string DisplayName { get; init; }

    public string? PrimaryEmail { get; init; }

    public required IdentityDirectoryUserStatus Status { get; init; }

    public string? DefaultActorId { get; init; }
}

public sealed record IdentityDirectoryIdentitySummary
{
    public required string IdentityId { get; init; }

    public required string UserId { get; init; }

    public required IdentityDirectoryIdentityType IdentityType { get; init; }

    public required string Provider { get; init; }

    public required string ProviderSubject { get; init; }

    public required string DisplayLabel { get; init; }

    public required IdentityDirectoryIdentityStatus Status { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record IdentityDirectoryActorSummary
{
    public required string ActorId { get; init; }

    public required string UserId { get; init; }

    public string? BoundIdentityId { get; init; }

    public string? OrganizationId { get; init; }

    public required IdentityDirectoryActorType ActorType { get; init; }

    public required string DisplayName { get; init; }

    public required IdentityDirectoryActorVisibilityMode VisibilityMode { get; init; }

    public required IdentityDirectoryActorStatus Status { get; init; }

    public bool IsDefaultForIdentity { get; init; }
}

public sealed record IdentityDirectoryOrganizationSummary
{
    public required string OrganizationId { get; init; }

    public required IdentityDirectoryOrganizationType OrganizationType { get; init; }

    public required string DisplayName { get; init; }

    public required IdentityDirectoryOrganizationStatus Status { get; init; }

    public string? ExternalReference { get; init; }
}

public sealed record IdentityDirectoryMembershipSummary
{
    public required string MembershipId { get; init; }

    public required string OrganizationId { get; init; }

    public required string UserId { get; init; }

    public string? ActorId { get; init; }

    public required string RoleKey { get; init; }

    public required IdentityDirectoryMembershipStatus Status { get; init; }

    public DateTimeOffset JoinedAtUtc { get; init; }

    public DateTimeOffset? LeftAtUtc { get; init; }
}

public sealed record IdentityDirectoryAccessGrantSummary
{
    public required string GrantId { get; init; }

    public required IdentityDirectoryGrantSubjectType SubjectType { get; init; }

    public required string SubjectId { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public required string Action { get; init; }

    public required IdentityDirectoryGrantEffect Effect { get; init; }

    public required IdentityDirectoryGrantSource Source { get; init; }

    public DateTimeOffset? ValidFromUtc { get; init; }

    public DateTimeOffset? ValidToUtc { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record IdentityDirectoryAuditAliasSummary
{
    public required string AliasId { get; init; }

    public required string ActorId { get; init; }

    public required IdentityDirectoryAliasMode AliasMode { get; init; }

    public required string ExternalLabel { get; init; }

    public required string Status { get; init; }
}

public sealed record IdentityDirectoryAccessDecision
{
    public bool Allowed { get; init; }

    public string? DecisionReason { get; init; }

    public IReadOnlyList<IdentityDirectoryAccessGrantSummary> MatchedGrants { get; init; } = Array.Empty<IdentityDirectoryAccessGrantSummary>();
}

public sealed record IdentityDirectoryResolvedSubject
{
    public required IdentityDirectoryUserSummary User { get; init; }

    public IdentityDirectoryActorSummary? ActiveActor { get; init; }

    public IReadOnlyList<IdentityDirectoryActorSummary> AvailableActors { get; init; } = Array.Empty<IdentityDirectoryActorSummary>();

    public IReadOnlyList<IdentityDirectoryOrganizationSummary> Organizations { get; init; } = Array.Empty<IdentityDirectoryOrganizationSummary>();
}

public sealed record IdentityDirectoryUserSessionView
{
    public required IdentityDirectoryUserSummary User { get; init; }

    public IdentityDirectoryActorSummary? ActiveActor { get; init; }

    public IReadOnlyList<IdentityDirectoryActorSummary> AvailableActors { get; init; } = Array.Empty<IdentityDirectoryActorSummary>();

    public IReadOnlyList<IdentityDirectoryIdentitySummary> Identities { get; init; } = Array.Empty<IdentityDirectoryIdentitySummary>();
}

public sealed record IdentityDirectoryProtectedActorProvision
{
    public required IdentityDirectoryActorSummary Actor { get; init; }

    public required IdentityDirectoryAuditAliasSummary Alias { get; init; }

    public IReadOnlyList<string> AuditRestrictions { get; init; } = Array.Empty<string>();
}
