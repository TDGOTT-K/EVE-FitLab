namespace EdenOS.Contracts.IdentityDirectory;

public sealed record ResolveIdentityDirectorySubjectRequest
{
    public required string SubjectId { get; init; }

    public string? ActiveActorId { get; init; }

    public string? ClientId { get; init; }
}

public sealed record GetIdentityDirectoryUserRequest
{
    public required string UserId { get; init; }
}

public sealed record GetIdentityDirectoryUserBySubjectRequest
{
    public required string SubjectId { get; init; }
}

public sealed record ListIdentityDirectoryIdentitiesByUserRequest
{
    public required string UserId { get; init; }

    public IdentityDirectoryIdentityStatus? StatusFilter { get; init; }
}

public sealed record GetIdentityDirectoryIdentityRequest
{
    public required string IdentityId { get; init; }
}

public sealed record ListIdentityDirectoryActorsByUserRequest
{
    public required string UserId { get; init; }

    public bool IncludeProtectedActors { get; init; }
}

public sealed record GetIdentityDirectoryActorRequest
{
    public required string ActorId { get; init; }
}

public sealed record ListSwitchableIdentityDirectoryActorsRequest
{
    public required string UserId { get; init; }
}

public sealed record GetIdentityDirectoryOrganizationRequest
{
    public required string OrganizationId { get; init; }
}

public sealed record ListIdentityDirectoryOrganizationsByUserRequest
{
    public required string UserId { get; init; }
}

public sealed record ListIdentityDirectoryMembershipsByUserRequest
{
    public required string UserId { get; init; }
}

public sealed record ListIdentityDirectoryMembershipsByOrganizationRequest
{
    public required string OrganizationId { get; init; }
}

public sealed record ListIdentityDirectoryAccessGrantsForSubjectRequest
{
    public required IdentityDirectoryGrantSubjectType SubjectType { get; init; }

    public required string SubjectId { get; init; }

    public string? ResourceType { get; init; }
}

public sealed record CheckIdentityDirectoryAccessRequest
{
    public required IdentityDirectoryGrantSubjectType SubjectType { get; init; }

    public required string SubjectId { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public required string Action { get; init; }
}

public sealed record ProvisionIdentityDirectoryUserFromSubjectRequest
{
    public required string SubjectId { get; init; }

    public required string DisplayName { get; init; }

    public string? PrimaryEmail { get; init; }
}

public sealed record DisableIdentityDirectoryUserRequest
{
    public required string UserId { get; init; }

    public string? Reason { get; init; }
}

public sealed record UpdateIdentityDirectoryUserProfileRequest
{
    public required string UserId { get; init; }

    public string? DisplayName { get; init; }

    public string? PrimaryEmail { get; init; }
}

public sealed record AttachExternalIdentityRequest
{
    public required string UserId { get; init; }

    public required IdentityDirectoryIdentityType IdentityType { get; init; }

    public required string Provider { get; init; }

    public required string ProviderSubject { get; init; }

    public required string DisplayLabel { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record CreateVirtualIdentityRequest
{
    public required string UserId { get; init; }

    public required IdentityDirectoryIdentityType IdentityType { get; init; }

    public required string DisplayLabel { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record RevokeIdentityRequest
{
    public required string IdentityId { get; init; }

    public string? Reason { get; init; }
}

public sealed record CreateIdentityDirectoryActorRequest
{
    public required string UserId { get; init; }

    public required IdentityDirectoryActorType ActorType { get; init; }

    public string? BoundIdentityId { get; init; }

    public string? OrganizationId { get; init; }

    public required string DisplayName { get; init; }

    public required IdentityDirectoryActorVisibilityMode VisibilityMode { get; init; }
}

public sealed record UpdateIdentityDirectoryActorRequest
{
    public required string ActorId { get; init; }

    public string? DisplayName { get; init; }

    public IdentityDirectoryActorVisibilityMode? VisibilityMode { get; init; }

    public string? OrganizationId { get; init; }
}

public sealed record DisableIdentityDirectoryActorRequest
{
    public required string ActorId { get; init; }

    public string? Reason { get; init; }
}

public sealed record SetDefaultIdentityDirectoryActorRequest
{
    public required string UserId { get; init; }

    public required string ActorId { get; init; }
}

public sealed record SwitchActiveIdentityDirectoryActorRequest
{
    public required string UserId { get; init; }

    public required string ActorId { get; init; }

    public string? ClientId { get; init; }
}

public sealed record CreateIdentityDirectoryOrganizationRequest
{
    public required IdentityDirectoryOrganizationType OrganizationType { get; init; }

    public required string DisplayName { get; init; }

    public string? ExternalReference { get; init; }
}

public sealed record AddIdentityDirectoryMembershipRequest
{
    public required string OrganizationId { get; init; }

    public required string UserId { get; init; }

    public string? ActorId { get; init; }

    public required string RoleKey { get; init; }

    public string? ApprovedByActorId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record UpdateIdentityDirectoryMembershipRoleRequest
{
    public required string MembershipId { get; init; }

    public required string RoleKey { get; init; }
}

public sealed record SuspendIdentityDirectoryMembershipRequest
{
    public required string MembershipId { get; init; }

    public string? Reason { get; init; }
}

public sealed record RevokeIdentityDirectoryMembershipRequest
{
    public required string MembershipId { get; init; }

    public string? Reason { get; init; }
}

public sealed record AddIdentityDirectoryAccessGrantRequest
{
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

public sealed record RevokeIdentityDirectoryAccessGrantRequest
{
    public required string GrantId { get; init; }
}

public sealed record RebuildIdentityDirectoryAccessProjectionRequest
{
    public string? OrganizationId { get; init; }

    public string? UserId { get; init; }

    public string? ActorId { get; init; }
}

public sealed record BootstrapIdentityDirectoryUserSessionRequest
{
    public required string SubjectId { get; init; }

    public required string DisplayName { get; init; }

    public string? PrimaryEmail { get; init; }

    public string? ClientId { get; init; }
}

public sealed record CompleteEsiIdentityBindingRequest
{
    public required string UserId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProvisionProtectedInformantActorRequest
{
    public required string UserId { get; init; }

    public string? BoundIdentityId { get; init; }

    public string? DisplayName { get; init; }
}

public sealed record ActivateIdentityDirectoryMembershipActorRequest
{
    public required string MembershipId { get; init; }

    public required string DisplayName { get; init; }

    public IdentityDirectoryActorVisibilityMode VisibilityMode { get; init; } = IdentityDirectoryActorVisibilityMode.OrganizationOnly;
}
