using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.IdentityDirectory;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.IdentityDirectory;

public sealed class InMemoryIdentityDirectoryService : IIdentityDirectoryService
{
    private const int PersistenceRetryCount = 3;
    private const string InternalProvider = "edenos.internal";
    private const string StablePublicAliasLabelPrefix = "informant";

    private readonly LocalJsonStateStore<IdentityDirectoryRuntimeState>? _stateStore;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IdentityDirectoryUserSummary> _usersById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _userIdsBySubjectId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryIdentitySummary> _identitiesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _identityIdsByProviderSubject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryActorSummary> _actorsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryOrganizationSummary> _organizationsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryMembershipSummary> _membershipsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryAccessGrantSummary> _accessGrantsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdentityDirectoryAuditAliasSummary> _auditAliasesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeActorIdsByUserId = new(StringComparer.Ordinal);
    private readonly List<PersistedIdentityDirectoryActorSwitchRecord> _actorSwitchHistory = [];

    public InMemoryIdentityDirectoryService()
        : this(stateStore: null)
    {
    }

    internal InMemoryIdentityDirectoryService(LocalJsonStateStore<IdentityDirectoryRuntimeState>? stateStore)
    {
        _stateStore = stateStore;
        ReloadPersistedState();
    }

    public UseCaseResult<IdentityDirectoryResolvedSubject> ResolveSubject(ResolveIdentityDirectorySubjectRequest request)
    {
        var traceId = CreateTraceId("identity_directory.resolve_subject");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();

            if (string.IsNullOrWhiteSpace(request.SubjectId))
            {
                return UseCaseResult<IdentityDirectoryResolvedSubject>.Failure(
                    UseCaseStatus.InvalidInput,
                    "identity_directory.resolve_subject requires a subject id.",
                    traceId,
                    ["SubjectId cannot be empty."]);
            }

            if (!_userIdsBySubjectId.TryGetValue(request.SubjectId.Trim(), out var userId)
                || !_usersById.TryGetValue(userId, out var user))
            {
                return UseCaseResult<IdentityDirectoryResolvedSubject>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory could not resolve the requested subject.",
                    traceId,
                    [$"Subject '{request.SubjectId}' is not provisioned."]);
            }

            var activeActorResult = ResolveActiveActorForUser(user, request.ActiveActorId, traceId);
            if (!activeActorResult.IsSuccess)
            {
                return UseCaseResult<IdentityDirectoryResolvedSubject>.Failure(
                    activeActorResult.Status,
                    activeActorResult.Summary,
                    traceId,
                    activeActorResult.Errors);
            }

            var availableActors = GetActorsByUser(user.UserId, includeProtectedActors: true)
                .Where(actor => actor.Status == IdentityDirectoryActorStatus.Active)
                .OrderBy(actor => actor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var organizations = GetOrganizationsByUser(user.UserId)
                .OrderBy(organization => organization.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IdentityDirectoryResolvedSubject>.Success(
                new IdentityDirectoryResolvedSubject
                {
                    User = user,
                    ActiveActor = activeActorResult.Data,
                    AvailableActors = availableActors,
                    Organizations = organizations
                },
                "Resolved identity directory subject context.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryUserSummary> GetUser(GetIdentityDirectoryUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.user_get");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!_usersById.TryGetValue(NormalizeRequiredId(request.UserId) ?? string.Empty, out var user))
            {
                return UseCaseResult<IdentityDirectoryUserSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            return UseCaseResult<IdentityDirectoryUserSummary>.Success(user, "Loaded identity directory user.", traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryUserSummary> GetUserBySubject(GetIdentityDirectoryUserBySubjectRequest request)
    {
        var traceId = CreateTraceId("identity_directory.user_get_by_subject");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            var subjectId = NormalizeRequiredId(request.SubjectId);
            if (subjectId is null || !_userIdsBySubjectId.TryGetValue(subjectId, out var userId) || !_usersById.TryGetValue(userId, out var user))
            {
                return UseCaseResult<IdentityDirectoryUserSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory subject was not found.",
                    traceId,
                    [$"Subject '{request.SubjectId}' does not exist."]);
            }

            return UseCaseResult<IdentityDirectoryUserSummary>.Success(user, "Loaded identity directory user by subject.", traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryIdentitySummary>> ListIdentitiesByUser(ListIdentityDirectoryIdentitiesByUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.identity_list_by_user");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!UserExists(request.UserId))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryIdentitySummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var identities = _identitiesById.Values
                .Where(identity => string.Equals(identity.UserId, request.UserId, StringComparison.Ordinal))
                .Where(identity => request.StatusFilter is null || identity.Status == request.StatusFilter.Value)
                .OrderBy(identity => identity.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryIdentitySummary>>.Success(
                identities,
                "Listed identity directory identities for the user.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryIdentitySummary> GetIdentity(GetIdentityDirectoryIdentityRequest request)
    {
        var traceId = CreateTraceId("identity_directory.identity_get");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!_identitiesById.TryGetValue(NormalizeRequiredId(request.IdentityId) ?? string.Empty, out var identity))
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory identity was not found.",
                    traceId,
                    [$"Identity '{request.IdentityId}' does not exist."]);
            }

            return UseCaseResult<IdentityDirectoryIdentitySummary>.Success(identity, "Loaded identity directory identity.", traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>> ListActorsByUser(ListIdentityDirectoryActorsByUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_list_by_user");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!UserExists(request.UserId))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var actors = GetActorsByUser(request.UserId, request.IncludeProtectedActors)
                .OrderBy(actor => actor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>>.Success(
                actors,
                "Listed identity directory actors for the user.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryActorSummary> GetActor(GetIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_get");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!_actorsById.TryGetValue(NormalizeRequiredId(request.ActorId) ?? string.Empty, out var actor))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory actor was not found.",
                    traceId,
                    [$"Actor '{request.ActorId}' does not exist."]);
            }

            return UseCaseResult<IdentityDirectoryActorSummary>.Success(actor, "Loaded identity directory actor.", traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>> ListSwitchableActors(ListSwitchableIdentityDirectoryActorsRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_list_switchable");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!UserExists(request.UserId))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var actors = GetActorsByUser(request.UserId, includeProtectedActors: true)
                .Where(actor => actor.Status == IdentityDirectoryActorStatus.Active)
                .OrderBy(actor => actor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>>.Success(
                actors,
                "Listed switchable identity directory actors for the user.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryOrganizationSummary> GetOrganization(GetIdentityDirectoryOrganizationRequest request)
    {
        var traceId = CreateTraceId("identity_directory.organization_get");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!_organizationsById.TryGetValue(NormalizeRequiredId(request.OrganizationId) ?? string.Empty, out var organization))
            {
                return UseCaseResult<IdentityDirectoryOrganizationSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory organization was not found.",
                    traceId,
                    [$"Organization '{request.OrganizationId}' does not exist."]);
            }

            return UseCaseResult<IdentityDirectoryOrganizationSummary>.Success(organization, "Loaded identity directory organization.", traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryOrganizationSummary>> ListOrganizationsByUser(ListIdentityDirectoryOrganizationsByUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.organization_list_by_user");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!UserExists(request.UserId))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryOrganizationSummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var organizations = GetOrganizationsByUser(request.UserId)
                .OrderBy(organization => organization.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryOrganizationSummary>>.Success(
                organizations,
                "Listed identity directory organizations for the user.",
                traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>> ListMembershipsByUser(ListIdentityDirectoryMembershipsByUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_list_by_user");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!UserExists(request.UserId))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var memberships = _membershipsById.Values
                .Where(membership => string.Equals(membership.UserId, request.UserId, StringComparison.Ordinal))
                .OrderBy(membership => membership.OrganizationId, StringComparer.Ordinal)
                .ThenBy(membership => membership.RoleKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>>.Success(
                memberships,
                "Listed identity directory memberships for the user.",
                traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>> ListMembershipsByOrganization(ListIdentityDirectoryMembershipsByOrganizationRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_list_by_organization");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (!_organizationsById.ContainsKey(NormalizeRequiredId(request.OrganizationId) ?? string.Empty))
            {
                return UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory organization was not found.",
                    traceId,
                    [$"Organization '{request.OrganizationId}' does not exist."]);
            }

            var memberships = _membershipsById.Values
                .Where(membership => string.Equals(membership.OrganizationId, request.OrganizationId, StringComparison.Ordinal))
                .OrderBy(membership => membership.UserId, StringComparer.Ordinal)
                .ThenBy(membership => membership.RoleKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>>.Success(
                memberships,
                "Listed identity directory memberships for the organization.",
                traceId);
        }
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>> ListAccessGrantsForSubject(ListIdentityDirectoryAccessGrantsForSubjectRequest request)
    {
        var traceId = CreateTraceId("identity_directory.access_list_for_subject");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            var grants = _accessGrantsById.Values
                .Where(grant => grant.SubjectType == request.SubjectType)
                .Where(grant => string.Equals(grant.SubjectId, request.SubjectId, StringComparison.Ordinal))
                .Where(grant => request.ResourceType is null || string.Equals(grant.ResourceType, request.ResourceType, StringComparison.Ordinal))
                .OrderBy(grant => grant.ResourceType, StringComparer.Ordinal)
                .ThenBy(grant => grant.ResourceId, StringComparer.Ordinal)
                .ThenBy(grant => grant.Action, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>>.Success(
                grants,
                "Listed identity directory access grants for the subject.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryAccessDecision> CheckAccess(CheckIdentityDirectoryAccessRequest request)
    {
        var traceId = CreateTraceId("identity_directory.access_check");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            if (string.IsNullOrWhiteSpace(request.SubjectId)
                || string.IsNullOrWhiteSpace(request.ResourceType)
                || string.IsNullOrWhiteSpace(request.ResourceId)
                || string.IsNullOrWhiteSpace(request.Action))
            {
                return UseCaseResult<IdentityDirectoryAccessDecision>.Failure(
                    UseCaseStatus.InvalidInput,
                    "identity_directory.access_check requires a complete subject and resource descriptor.",
                    traceId,
                    ["SubjectId, ResourceType, ResourceId, and Action cannot be empty."]);
            }

            var now = DateTimeOffset.UtcNow;
            var matchedGrants = _accessGrantsById.Values
                .Where(grant => grant.SubjectType == request.SubjectType)
                .Where(grant => string.Equals(grant.SubjectId, request.SubjectId, StringComparison.Ordinal))
                .Where(grant => string.Equals(grant.ResourceType, request.ResourceType, StringComparison.Ordinal))
                .Where(grant => string.Equals(grant.ResourceId, request.ResourceId, StringComparison.Ordinal))
                .Where(grant => string.Equals(grant.Action, request.Action, StringComparison.OrdinalIgnoreCase))
                .Where(grant => grant.ValidFromUtc is null || grant.ValidFromUtc <= now)
                .Where(grant => grant.ValidToUtc is null || grant.ValidToUtc >= now)
                .OrderBy(grant => grant.Effect)
                .ToArray();

            var denyMatch = matchedGrants.FirstOrDefault(grant => grant.Effect == IdentityDirectoryGrantEffect.Deny);
            if (denyMatch is not null)
            {
                return UseCaseResult<IdentityDirectoryAccessDecision>.Success(
                    new IdentityDirectoryAccessDecision
                    {
                        Allowed = false,
                        DecisionReason = "A deny grant matched the requested resource action.",
                        MatchedGrants = matchedGrants
                    },
                    "Denied access through identity directory grant evaluation.",
                    traceId);
            }

            var allowMatch = matchedGrants.FirstOrDefault(grant => grant.Effect == IdentityDirectoryGrantEffect.Allow);
            if (allowMatch is not null)
            {
                return UseCaseResult<IdentityDirectoryAccessDecision>.Success(
                    new IdentityDirectoryAccessDecision
                    {
                        Allowed = true,
                        DecisionReason = "An allow grant matched the requested resource action.",
                        MatchedGrants = matchedGrants
                    },
                    "Granted access through identity directory grant evaluation.",
                    traceId);
            }

            return UseCaseResult<IdentityDirectoryAccessDecision>.Success(
                new IdentityDirectoryAccessDecision
                {
                    Allowed = false,
                    DecisionReason = "No matching access grant was found.",
                    MatchedGrants = Array.Empty<IdentityDirectoryAccessGrantSummary>()
                },
                "No matching access grant was found.",
                traceId);
        }
    }

    public UseCaseResult<IdentityDirectoryUserSummary> ProvisionUserFromSubject(ProvisionIdentityDirectoryUserFromSubjectRequest request)
    {
        var traceId = CreateTraceId("identity_directory.user_provision_from_subject");
        return ExecutePersistedMutation(traceId, "identity_directory.user_provision_from_subject", () =>
        {
            var subjectId = NormalizeRequiredId(request.SubjectId);
            var displayName = NormalizeRequiredText(request.DisplayName);
            if (subjectId is null || displayName is null)
            {
                return UseCaseResult<IdentityDirectoryUserSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Provisioning a user requires a subject id and display name.",
                    traceId,
                    ["SubjectId and DisplayName cannot be empty."]);
            }

            if (_userIdsBySubjectId.TryGetValue(subjectId, out var existingUserId)
                && _usersById.TryGetValue(existingUserId, out var existingUser))
            {
                var updatedUser = existingUser with
                {
                    DisplayName = displayName,
                    PrimaryEmail = NormalizeOptionalText(request.PrimaryEmail) ?? existingUser.PrimaryEmail
                };

                _usersById[updatedUser.UserId] = updatedUser;
                PersistState();

                return UseCaseResult<IdentityDirectoryUserSummary>.Success(
                    updatedUser,
                    "Provisioned existing identity directory user from subject.",
                    traceId);
            }

            var user = new IdentityDirectoryUserSummary
            {
                UserId = CreateId("user"),
                SubjectId = subjectId,
                DisplayName = displayName,
                PrimaryEmail = NormalizeOptionalText(request.PrimaryEmail),
                Status = IdentityDirectoryUserStatus.Active,
                DefaultActorId = null
            };

            _usersById[user.UserId] = user;
            _userIdsBySubjectId[user.SubjectId] = user.UserId;
            PersistState();

            return UseCaseResult<IdentityDirectoryUserSummary>.Success(
                user,
                "Provisioned new identity directory user from subject.",
                traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryUserSummary> DisableUser(DisableIdentityDirectoryUserRequest request)
    {
        var traceId = CreateTraceId("identity_directory.user_disable");
        return ExecutePersistedMutation(traceId, "identity_directory.user_disable", () =>
        {
            if (!_usersById.TryGetValue(NormalizeRequiredId(request.UserId) ?? string.Empty, out var user))
            {
                return UseCaseResult<IdentityDirectoryUserSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var updatedUser = user with { Status = IdentityDirectoryUserStatus.Disabled };
            _usersById[updatedUser.UserId] = updatedUser;
            _activeActorIdsByUserId.Remove(updatedUser.UserId);
            PersistState();

            return UseCaseResult<IdentityDirectoryUserSummary>.Success(updatedUser, "Disabled identity directory user.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryUserSummary> UpdateUserProfile(UpdateIdentityDirectoryUserProfileRequest request)
    {
        var traceId = CreateTraceId("identity_directory.user_update_profile");
        return ExecutePersistedMutation(traceId, "identity_directory.user_update_profile", () =>
        {
            if (!_usersById.TryGetValue(NormalizeRequiredId(request.UserId) ?? string.Empty, out var user))
            {
                return UseCaseResult<IdentityDirectoryUserSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var updatedUser = user with
            {
                DisplayName = NormalizeOptionalText(request.DisplayName) ?? user.DisplayName,
                PrimaryEmail = request.PrimaryEmail is null ? user.PrimaryEmail : NormalizeOptionalText(request.PrimaryEmail)
            };

            _usersById[updatedUser.UserId] = updatedUser;
            PersistState();
            return UseCaseResult<IdentityDirectoryUserSummary>.Success(updatedUser, "Updated identity directory user profile.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryIdentitySummary> AttachExternalIdentity(AttachExternalIdentityRequest request)
    {
        var traceId = CreateTraceId("identity_directory.identity_attach_external");
        return ExecutePersistedMutation(traceId, "identity_directory.identity_attach_external", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var provider = NormalizeRequiredText(request.Provider);
            var providerSubject = NormalizeRequiredText(request.ProviderSubject);
            var displayLabel = NormalizeRequiredText(request.DisplayLabel);
            if (provider is null || providerSubject is null || displayLabel is null)
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Attaching an external identity requires provider, provider subject, and display label.",
                    traceId,
                    ["Provider, ProviderSubject, and DisplayLabel cannot be empty."]);
            }

            var providerKey = BuildProviderSubjectKey(provider, providerSubject);
            if (_identityIdsByProviderSubject.TryGetValue(providerKey, out var existingIdentityId)
                && _identitiesById.TryGetValue(existingIdentityId, out var existingIdentity))
            {
                if (!string.Equals(existingIdentity.UserId, user.UserId, StringComparison.Ordinal))
                {
                    return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                        UseCaseStatus.Conflict,
                        "The requested external identity is already attached to another user.",
                        traceId,
                        [$"Provider subject '{providerSubject}' is already bound to another user."]);
                }

                var updatedExisting = existingIdentity with
                {
                    IdentityType = request.IdentityType,
                    DisplayLabel = displayLabel,
                    Status = IdentityDirectoryIdentityStatus.Active,
                    Metadata = NormalizeMetadata(request.Metadata)
                };

                _identitiesById[updatedExisting.IdentityId] = updatedExisting;
                PersistState();

                return UseCaseResult<IdentityDirectoryIdentitySummary>.Success(
                    updatedExisting,
                    "Updated existing external identity binding.",
                    traceId);
            }

            var identity = new IdentityDirectoryIdentitySummary
            {
                IdentityId = CreateId("identity"),
                UserId = user.UserId,
                IdentityType = request.IdentityType,
                Provider = provider,
                ProviderSubject = providerSubject,
                DisplayLabel = displayLabel,
                Status = IdentityDirectoryIdentityStatus.Active,
                Metadata = NormalizeMetadata(request.Metadata)
            };

            _identitiesById[identity.IdentityId] = identity;
            _identityIdsByProviderSubject[providerKey] = identity.IdentityId;
            PersistState();

            return UseCaseResult<IdentityDirectoryIdentitySummary>.Success(
                identity,
                "Attached external identity to the user.",
                traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryIdentitySummary> CreateVirtualIdentity(CreateVirtualIdentityRequest request)
    {
        var traceId = CreateTraceId("identity_directory.identity_create_virtual");
        return ExecutePersistedMutation(traceId, "identity_directory.identity_create_virtual", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            if (request.IdentityType == IdentityDirectoryIdentityType.EsiCharacter)
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual identity creation cannot use the ESI character type.",
                    traceId,
                    ["Use AttachExternalIdentity for ESI identities."]);
            }

            var displayLabel = NormalizeRequiredText(request.DisplayLabel);
            if (displayLabel is null)
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual identity display label is required.",
                    traceId,
                    ["DisplayLabel cannot be empty."]);
            }

            var identity = new IdentityDirectoryIdentitySummary
            {
                IdentityId = CreateId("identity"),
                UserId = user.UserId,
                IdentityType = request.IdentityType,
                Provider = InternalProvider,
                ProviderSubject = CreateId("provider_subject"),
                DisplayLabel = displayLabel,
                Status = IdentityDirectoryIdentityStatus.Active,
                Metadata = NormalizeMetadata(request.Metadata)
            };

            _identitiesById[identity.IdentityId] = identity;
            _identityIdsByProviderSubject[BuildProviderSubjectKey(identity.Provider, identity.ProviderSubject)] = identity.IdentityId;
            PersistState();

            return UseCaseResult<IdentityDirectoryIdentitySummary>.Success(
                identity,
                "Created virtual identity for the user.",
                traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryIdentitySummary> RevokeIdentity(RevokeIdentityRequest request)
    {
        var traceId = CreateTraceId("identity_directory.identity_revoke");
        return ExecutePersistedMutation(traceId, "identity_directory.identity_revoke", () =>
        {
            if (!_identitiesById.TryGetValue(NormalizeRequiredId(request.IdentityId) ?? string.Empty, out var identity))
            {
                return UseCaseResult<IdentityDirectoryIdentitySummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory identity was not found.",
                    traceId,
                    [$"Identity '{request.IdentityId}' does not exist."]);
            }

            var updatedIdentity = identity with { Status = IdentityDirectoryIdentityStatus.Revoked };
            _identitiesById[updatedIdentity.IdentityId] = updatedIdentity;
            PersistState();
            return UseCaseResult<IdentityDirectoryIdentitySummary>.Success(updatedIdentity, "Revoked identity directory identity.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> CreateActor(CreateIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_create");
        return ExecutePersistedMutation(traceId, "identity_directory.actor_create", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var displayName = NormalizeRequiredText(request.DisplayName);
            if (displayName is null)
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Actor display name is required.",
                    traceId,
                    ["DisplayName cannot be empty."]);
            }

            IdentityDirectoryIdentitySummary? identity = null;
            if (request.BoundIdentityId is not null)
            {
                if (!_identitiesById.TryGetValue(request.BoundIdentityId, out identity))
                {
                    return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                        UseCaseStatus.NotFound,
                        "The requested bound identity was not found.",
                        traceId,
                        [$"Identity '{request.BoundIdentityId}' does not exist."]);
                }

                if (!string.Equals(identity.UserId, user.UserId, StringComparison.Ordinal))
                {
                    return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                        UseCaseStatus.Conflict,
                        "The requested bound identity belongs to another user.",
                        traceId,
                        [$"Identity '{request.BoundIdentityId}' does not belong to user '{user.UserId}'."]);
                }
            }

            IdentityDirectoryOrganizationSummary? organization = null;
            if (request.OrganizationId is not null)
            {
                if (!_organizationsById.TryGetValue(request.OrganizationId, out organization))
                {
                    return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                        UseCaseStatus.NotFound,
                        "The requested organization was not found.",
                        traceId,
                        [$"Organization '{request.OrganizationId}' does not exist."]);
                }
            }

            var actor = new IdentityDirectoryActorSummary
            {
                ActorId = CreateId("actor"),
                UserId = user.UserId,
                BoundIdentityId = identity?.IdentityId,
                OrganizationId = organization?.OrganizationId,
                ActorType = request.ActorType,
                DisplayName = displayName,
                VisibilityMode = request.VisibilityMode,
                Status = IdentityDirectoryActorStatus.Active,
                IsDefaultForIdentity = false
            };

            if (request.ActorType == IdentityDirectoryActorType.Personal && string.IsNullOrWhiteSpace(user.DefaultActorId))
            {
                actor = actor with { IsDefaultForIdentity = identity is not null };
                user = user with { DefaultActorId = actor.ActorId };
                _usersById[user.UserId] = user;
            }

            _actorsById[actor.ActorId] = actor;
            PersistState();

            return UseCaseResult<IdentityDirectoryActorSummary>.Success(actor, "Created identity directory actor.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> UpdateActor(UpdateIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_update");
        return ExecutePersistedMutation(traceId, "identity_directory.actor_update", () =>
        {
            if (!_actorsById.TryGetValue(NormalizeRequiredId(request.ActorId) ?? string.Empty, out var actor))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory actor was not found.",
                    traceId,
                    [$"Actor '{request.ActorId}' does not exist."]);
            }

            if (request.OrganizationId is not null && !_organizationsById.ContainsKey(request.OrganizationId))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "The requested organization was not found.",
                    traceId,
                    [$"Organization '{request.OrganizationId}' does not exist."]);
            }

            var updatedActor = actor with
            {
                DisplayName = NormalizeOptionalText(request.DisplayName) ?? actor.DisplayName,
                VisibilityMode = request.VisibilityMode ?? actor.VisibilityMode,
                OrganizationId = request.OrganizationId is null ? actor.OrganizationId : NormalizeOptionalId(request.OrganizationId)
            };

            _actorsById[updatedActor.ActorId] = updatedActor;
            PersistState();
            return UseCaseResult<IdentityDirectoryActorSummary>.Success(updatedActor, "Updated identity directory actor.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> DisableActor(DisableIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_disable");
        return ExecutePersistedMutation(traceId, "identity_directory.actor_disable", () =>
        {
            if (!_actorsById.TryGetValue(NormalizeRequiredId(request.ActorId) ?? string.Empty, out var actor))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory actor was not found.",
                    traceId,
                    [$"Actor '{request.ActorId}' does not exist."]);
            }

            var updatedActor = actor with { Status = IdentityDirectoryActorStatus.Disabled, IsDefaultForIdentity = false };
            _actorsById[updatedActor.ActorId] = updatedActor;

            if (_usersById.TryGetValue(updatedActor.UserId, out var user)
                && string.Equals(user.DefaultActorId, updatedActor.ActorId, StringComparison.Ordinal))
            {
                _usersById[user.UserId] = user with { DefaultActorId = null };
            }

            if (_activeActorIdsByUserId.TryGetValue(updatedActor.UserId, out var activeActorId)
                && string.Equals(activeActorId, updatedActor.ActorId, StringComparison.Ordinal))
            {
                _activeActorIdsByUserId.Remove(updatedActor.UserId);
            }

            PersistState();
            return UseCaseResult<IdentityDirectoryActorSummary>.Success(updatedActor, "Disabled identity directory actor.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> SetDefaultActor(SetDefaultIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_set_default");
        return ExecutePersistedMutation(traceId, "identity_directory.actor_set_default", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            if (!TryGetActiveActorForUser(user.UserId, request.ActorId, out var actor, out var actorError))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    actorError!.Value.Status,
                    actorError.Value.Summary,
                    traceId,
                    actorError.Value.Errors);
            }

            if (!string.IsNullOrWhiteSpace(actor.BoundIdentityId))
            {
                var relatedActorIds = _actorsById.Values
                    .Where(candidate => string.Equals(candidate.BoundIdentityId, actor.BoundIdentityId, StringComparison.Ordinal))
                    .Select(candidate => candidate.ActorId)
                    .ToArray();

                foreach (var relatedActorId in relatedActorIds)
                {
                    var relatedActor = _actorsById[relatedActorId];
                    _actorsById[relatedActorId] = relatedActor with
                    {
                        IsDefaultForIdentity = string.Equals(relatedActor.ActorId, actor.ActorId, StringComparison.Ordinal)
                    };
                }

                actor = _actorsById[actor.ActorId];
            }

            _usersById[user.UserId] = user with { DefaultActorId = actor.ActorId };
            PersistState();
            return UseCaseResult<IdentityDirectoryActorSummary>.Success(actor, "Set default identity directory actor for the user.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> SwitchActiveActor(SwitchActiveIdentityDirectoryActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.actor_switch_active");
        return ExecutePersistedMutation(traceId, "identity_directory.actor_switch_active", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            if (!TryGetActiveActorForUser(user.UserId, request.ActorId, out var actor, out var actorError))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    actorError!.Value.Status,
                    actorError.Value.Summary,
                    traceId,
                    actorError.Value.Errors);
            }

            _activeActorIdsByUserId.TryGetValue(user.UserId, out var previousActorId);
            _activeActorIdsByUserId[user.UserId] = actor.ActorId;
            _actorSwitchHistory.Add(new PersistedIdentityDirectoryActorSwitchRecord
            {
                SwitchId = CreateId("actor_switch"),
                UserId = user.UserId,
                FromActorId = previousActorId,
                ToActorId = actor.ActorId,
                ClientId = NormalizeOptionalText(request.ClientId),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            PersistState();
            return UseCaseResult<IdentityDirectoryActorSummary>.Success(actor, "Switched active identity directory actor.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryOrganizationSummary> CreateOrganization(CreateIdentityDirectoryOrganizationRequest request)
    {
        var traceId = CreateTraceId("identity_directory.organization_create");
        return ExecutePersistedMutation(traceId, "identity_directory.organization_create", () =>
        {
            var displayName = NormalizeRequiredText(request.DisplayName);
            if (displayName is null)
            {
                return UseCaseResult<IdentityDirectoryOrganizationSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Organization display name is required.",
                    traceId,
                    ["DisplayName cannot be empty."]);
            }

            var organization = new IdentityDirectoryOrganizationSummary
            {
                OrganizationId = CreateId("organization"),
                OrganizationType = request.OrganizationType,
                DisplayName = displayName,
                Status = IdentityDirectoryOrganizationStatus.Active,
                ExternalReference = NormalizeOptionalText(request.ExternalReference)
            };

            _organizationsById[organization.OrganizationId] = organization;
            PersistState();
            return UseCaseResult<IdentityDirectoryOrganizationSummary>.Success(organization, "Created identity directory organization.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryMembershipSummary> AddMembership(AddIdentityDirectoryMembershipRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_add");
        return ExecutePersistedMutation(traceId, "identity_directory.membership_add", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            if (!_organizationsById.ContainsKey(NormalizeRequiredId(request.OrganizationId) ?? string.Empty))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory organization was not found.",
                    traceId,
                    [$"Organization '{request.OrganizationId}' does not exist."]);
            }

            if (request.ActorId is not null
                && (!TryGetActor(request.ActorId, out var actor) || !string.Equals(actor.UserId, user.UserId, StringComparison.Ordinal)))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.Conflict,
                    "The requested actor does not belong to the user.",
                    traceId,
                    [$"Actor '{request.ActorId}' does not belong to user '{user.UserId}'."]);
            }

            var roleKey = NormalizeRequiredText(request.RoleKey);
            if (roleKey is null)
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Membership role key is required.",
                    traceId,
                    ["RoleKey cannot be empty."]);
            }

            var membership = new IdentityDirectoryMembershipSummary
            {
                MembershipId = CreateId("membership"),
                OrganizationId = request.OrganizationId,
                UserId = user.UserId,
                ActorId = NormalizeOptionalId(request.ActorId),
                RoleKey = roleKey,
                Status = IdentityDirectoryMembershipStatus.Active,
                JoinedAtUtc = DateTimeOffset.UtcNow,
                LeftAtUtc = null
            };

            _membershipsById[membership.MembershipId] = membership;
            PersistState();
            return UseCaseResult<IdentityDirectoryMembershipSummary>.Success(membership, "Added identity directory membership.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryMembershipSummary> UpdateMembershipRole(UpdateIdentityDirectoryMembershipRoleRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_update_role");
        return ExecutePersistedMutation(traceId, "identity_directory.membership_update_role", () =>
        {
            if (!_membershipsById.TryGetValue(NormalizeRequiredId(request.MembershipId) ?? string.Empty, out var membership))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory membership was not found.",
                    traceId,
                    [$"Membership '{request.MembershipId}' does not exist."]);
            }

            var roleKey = NormalizeRequiredText(request.RoleKey);
            if (roleKey is null)
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Membership role key is required.",
                    traceId,
                    ["RoleKey cannot be empty."]);
            }

            var updatedMembership = membership with { RoleKey = roleKey };
            _membershipsById[updatedMembership.MembershipId] = updatedMembership;
            PersistState();
            return UseCaseResult<IdentityDirectoryMembershipSummary>.Success(updatedMembership, "Updated identity directory membership role.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryMembershipSummary> SuspendMembership(SuspendIdentityDirectoryMembershipRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_suspend");
        return ExecutePersistedMutation(traceId, "identity_directory.membership_suspend", () =>
        {
            if (!_membershipsById.TryGetValue(NormalizeRequiredId(request.MembershipId) ?? string.Empty, out var membership))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory membership was not found.",
                    traceId,
                    [$"Membership '{request.MembershipId}' does not exist."]);
            }

            var updatedMembership = membership with { Status = IdentityDirectoryMembershipStatus.Suspended };
            _membershipsById[updatedMembership.MembershipId] = updatedMembership;
            PersistState();
            return UseCaseResult<IdentityDirectoryMembershipSummary>.Success(updatedMembership, "Suspended identity directory membership.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryMembershipSummary> RevokeMembership(RevokeIdentityDirectoryMembershipRequest request)
    {
        var traceId = CreateTraceId("identity_directory.membership_revoke");
        return ExecutePersistedMutation(traceId, "identity_directory.membership_revoke", () =>
        {
            if (!_membershipsById.TryGetValue(NormalizeRequiredId(request.MembershipId) ?? string.Empty, out var membership))
            {
                return UseCaseResult<IdentityDirectoryMembershipSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory membership was not found.",
                    traceId,
                    [$"Membership '{request.MembershipId}' does not exist."]);
            }

            var updatedMembership = membership with
            {
                Status = IdentityDirectoryMembershipStatus.Revoked,
                LeftAtUtc = DateTimeOffset.UtcNow
            };

            _membershipsById[updatedMembership.MembershipId] = updatedMembership;
            PersistState();
            return UseCaseResult<IdentityDirectoryMembershipSummary>.Success(updatedMembership, "Revoked identity directory membership.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryAccessGrantSummary> AddAccessGrant(AddIdentityDirectoryAccessGrantRequest request)
    {
        var traceId = CreateTraceId("identity_directory.access_grant_add");
        return ExecutePersistedMutation(traceId, "identity_directory.access_grant_add", () =>
        {
            if (string.IsNullOrWhiteSpace(request.SubjectId)
                || string.IsNullOrWhiteSpace(request.ResourceType)
                || string.IsNullOrWhiteSpace(request.ResourceId)
                || string.IsNullOrWhiteSpace(request.Action))
            {
                return UseCaseResult<IdentityDirectoryAccessGrantSummary>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Access grant creation requires a subject and resource descriptor.",
                    traceId,
                    ["SubjectId, ResourceType, ResourceId, and Action cannot be empty."]);
            }

            var existingGrant = _accessGrantsById.Values.FirstOrDefault(grant =>
                grant.SubjectType == request.SubjectType
                && string.Equals(grant.SubjectId, request.SubjectId, StringComparison.Ordinal)
                && string.Equals(grant.ResourceType, request.ResourceType, StringComparison.Ordinal)
                && string.Equals(grant.ResourceId, request.ResourceId, StringComparison.Ordinal)
                && string.Equals(grant.Action, request.Action, StringComparison.OrdinalIgnoreCase)
                && grant.Effect == request.Effect
                && grant.Source == request.Source);

            if (existingGrant is not null)
            {
                return UseCaseResult<IdentityDirectoryAccessGrantSummary>.Success(existingGrant, "Reused existing identity directory access grant.", traceId);
            }

            var grant = new IdentityDirectoryAccessGrantSummary
            {
                GrantId = CreateId("grant"),
                SubjectType = request.SubjectType,
                SubjectId = request.SubjectId.Trim(),
                ResourceType = request.ResourceType.Trim(),
                ResourceId = request.ResourceId.Trim(),
                Action = request.Action.Trim(),
                Effect = request.Effect,
                Source = request.Source,
                ValidFromUtc = request.ValidFromUtc,
                ValidToUtc = request.ValidToUtc,
                Metadata = NormalizeMetadata(request.Metadata)
            };

            _accessGrantsById[grant.GrantId] = grant;
            PersistState();
            return UseCaseResult<IdentityDirectoryAccessGrantSummary>.Success(grant, "Added identity directory access grant.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryAccessGrantSummary> RevokeAccessGrant(RevokeIdentityDirectoryAccessGrantRequest request)
    {
        var traceId = CreateTraceId("identity_directory.access_grant_revoke");
        return ExecutePersistedMutation(traceId, "identity_directory.access_grant_revoke", () =>
        {
            if (!_accessGrantsById.Remove(NormalizeRequiredId(request.GrantId) ?? string.Empty, out var grant))
            {
                return UseCaseResult<IdentityDirectoryAccessGrantSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory access grant was not found.",
                    traceId,
                    [$"Grant '{request.GrantId}' does not exist."]);
            }

            PersistState();
            return UseCaseResult<IdentityDirectoryAccessGrantSummary>.Success(grant, "Revoked identity directory access grant.", traceId);
        });
    }

    public UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>> RebuildAccessProjection(RebuildIdentityDirectoryAccessProjectionRequest request)
    {
        var traceId = CreateTraceId("identity_directory.access_rebuild_projection");
        lock (_syncRoot)
        {
            RefreshStateIfPersistent();
            var grants = _accessGrantsById.Values
                .Where(grant => request.UserId is null || string.Equals(grant.SubjectId, request.UserId, StringComparison.Ordinal))
                .Where(grant => request.ActorId is null || string.Equals(grant.SubjectId, request.ActorId, StringComparison.Ordinal))
                .Where(grant => request.OrganizationId is null || string.Equals(grant.SubjectId, request.OrganizationId, StringComparison.Ordinal))
                .OrderBy(grant => grant.SubjectType)
                .ThenBy(grant => grant.SubjectId, StringComparer.Ordinal)
                .ThenBy(grant => grant.ResourceType, StringComparer.Ordinal)
                .ToArray();

            return UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>>.Success(
                grants,
                "Returned current identity directory access projection without recomputation.",
                traceId,
                warnings: ["Projection rebuild is not yet implemented; current grants were returned unchanged."]);
        }
    }

    public UseCaseResult<IdentityDirectoryUserSessionView> BootstrapUserSession(BootstrapIdentityDirectoryUserSessionRequest request)
    {
        var traceId = CreateTraceId("identity_directory.bootstrap_user_session");
        return ExecutePersistedMutation(traceId, "identity_directory.bootstrap_user_session", () =>
        {
            var provisionResult = ProvisionUserFromSubject(new ProvisionIdentityDirectoryUserFromSubjectRequest
            {
                SubjectId = request.SubjectId,
                DisplayName = request.DisplayName,
                PrimaryEmail = request.PrimaryEmail
            });

            if (!provisionResult.IsSuccess)
            {
                return UseCaseResult<IdentityDirectoryUserSessionView>.Failure(
                    provisionResult.Status,
                    provisionResult.Summary,
                    traceId,
                    provisionResult.Errors);
            }

            var user = provisionResult.Data!;
            EnsureDefaultPersonalActor(user.UserId);
            _usersById.TryGetValue(user.UserId, out user);
            var activeActor = ResolvePreferredActor(user!);
            var sessionView = BuildUserSessionView(user!, activeActor);
            PersistState();

            return UseCaseResult<IdentityDirectoryUserSessionView>.Success(sessionView, "Bootstrapped identity directory user session.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryUserSessionView> CompleteEsiBinding(CompleteEsiIdentityBindingRequest request)
    {
        var traceId = CreateTraceId("identity_directory.complete_esi_binding");
        return ExecutePersistedMutation(traceId, "identity_directory.complete_esi_binding", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryUserSessionView>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var attachResult = AttachExternalIdentity(new AttachExternalIdentityRequest
            {
                UserId = user.UserId,
                IdentityType = IdentityDirectoryIdentityType.EsiCharacter,
                Provider = "ccp_esi",
                ProviderSubject = request.EsiCharacterId,
                DisplayLabel = request.CharacterName,
                Metadata = request.Metadata
            });

            if (!attachResult.IsSuccess)
            {
                return UseCaseResult<IdentityDirectoryUserSessionView>.Failure(
                    attachResult.Status,
                    attachResult.Summary,
                    traceId,
                    attachResult.Errors);
            }

            var identity = attachResult.Data!;
            var existingActor = _actorsById.Values.FirstOrDefault(actor =>
                string.Equals(actor.UserId, user.UserId, StringComparison.Ordinal)
                && string.Equals(actor.BoundIdentityId, identity.IdentityId, StringComparison.Ordinal)
                && actor.Status == IdentityDirectoryActorStatus.Active);

            if (existingActor is null)
            {
                var actorResult = CreateActor(new CreateIdentityDirectoryActorRequest
                {
                    UserId = user.UserId,
                    ActorType = IdentityDirectoryActorType.Personal,
                    BoundIdentityId = identity.IdentityId,
                    DisplayName = identity.DisplayLabel,
                    VisibilityMode = IdentityDirectoryActorVisibilityMode.Public
                });

                if (!actorResult.IsSuccess)
                {
                    return UseCaseResult<IdentityDirectoryUserSessionView>.Failure(
                        actorResult.Status,
                        actorResult.Summary,
                        traceId,
                        actorResult.Errors);
                }

                existingActor = actorResult.Data!;
            }

            _activeActorIdsByUserId[user.UserId] = existingActor.ActorId;
            _usersById.TryGetValue(user.UserId, out var refreshedUser);
            var sessionView = BuildUserSessionView(refreshedUser!, existingActor);
            PersistState();

            return UseCaseResult<IdentityDirectoryUserSessionView>.Success(sessionView, "Completed ESI identity binding and refreshed user session.", traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryProtectedActorProvision> ProvisionProtectedInformantActor(ProvisionProtectedInformantActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.provision_protected_informant_actor");
        return ExecutePersistedMutation(traceId, "identity_directory.provision_protected_informant_actor", () =>
        {
            if (!TryGetUser(request.UserId, out var user))
            {
                return UseCaseResult<IdentityDirectoryProtectedActorProvision>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory user was not found.",
                    traceId,
                    [$"User '{request.UserId}' does not exist."]);
            }

            var actorResult = CreateActor(new CreateIdentityDirectoryActorRequest
            {
                UserId = user.UserId,
                ActorType = IdentityDirectoryActorType.ProtectedInformant,
                BoundIdentityId = request.BoundIdentityId,
                DisplayName = NormalizeOptionalText(request.DisplayName) ?? $"Informant {user.DisplayName}",
                VisibilityMode = IdentityDirectoryActorVisibilityMode.ProtectedAlias
            });

            if (!actorResult.IsSuccess)
            {
                return UseCaseResult<IdentityDirectoryProtectedActorProvision>.Failure(
                    actorResult.Status,
                    actorResult.Summary,
                    traceId,
                    actorResult.Errors);
            }

            var actor = actorResult.Data!;
            var alias = new IdentityDirectoryAuditAliasSummary
            {
                AliasId = CreateId("alias"),
                ActorId = actor.ActorId,
                AliasMode = IdentityDirectoryAliasMode.StablePublicAlias,
                ExternalLabel = $"{StablePublicAliasLabelPrefix}-{Guid.NewGuid():N[..8]}",
                Status = "active"
            };

            _auditAliasesById[alias.AliasId] = alias;
            PersistState();

            return UseCaseResult<IdentityDirectoryProtectedActorProvision>.Success(
                new IdentityDirectoryProtectedActorProvision
                {
                    Actor = actor,
                    Alias = alias,
                    AuditRestrictions = ["Actor is protected; only alias views should be exposed to normal operators."]
                },
                "Provisioned protected informant actor and audit alias.",
                traceId);
        });
    }

    public UseCaseResult<IdentityDirectoryActorSummary> ActivateMembershipActor(ActivateIdentityDirectoryMembershipActorRequest request)
    {
        var traceId = CreateTraceId("identity_directory.activate_membership_actor");
        return ExecutePersistedMutation(traceId, "identity_directory.activate_membership_actor", () =>
        {
            if (!_membershipsById.TryGetValue(NormalizeRequiredId(request.MembershipId) ?? string.Empty, out var membership))
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.NotFound,
                    "Identity directory membership was not found.",
                    traceId,
                    [$"Membership '{request.MembershipId}' does not exist."]);
            }

            if (membership.Status != IdentityDirectoryMembershipStatus.Active)
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    UseCaseStatus.Conflict,
                    "Membership must be active before a membership actor can be provisioned.",
                    traceId,
                    [$"Membership '{membership.MembershipId}' is currently '{membership.Status}'."]);
            }

            var actorResult = CreateActor(new CreateIdentityDirectoryActorRequest
            {
                UserId = membership.UserId,
                ActorType = IdentityDirectoryActorType.OrganizationMember,
                BoundIdentityId = null,
                OrganizationId = membership.OrganizationId,
                DisplayName = request.DisplayName,
                VisibilityMode = request.VisibilityMode
            });

            if (!actorResult.IsSuccess)
            {
                return UseCaseResult<IdentityDirectoryActorSummary>.Failure(
                    actorResult.Status,
                    actorResult.Summary,
                    traceId,
                    actorResult.Errors);
            }

            var actor = actorResult.Data!;
            _membershipsById[membership.MembershipId] = membership with { ActorId = actor.ActorId };
            PersistState();

            return UseCaseResult<IdentityDirectoryActorSummary>.Success(actor, "Provisioned organization membership actor.", traceId);
        });
    }

    private void RefreshStateIfPersistent()
    {
        if (_stateStore is not null)
        {
            ReloadPersistedState();
        }
    }

    private void ReloadPersistedState()
    {
        if (_stateStore is null)
        {
            return;
        }

        var state = _stateStore.Load();
        _usersById.Clear();
        _userIdsBySubjectId.Clear();
        _identitiesById.Clear();
        _identityIdsByProviderSubject.Clear();
        _actorsById.Clear();
        _organizationsById.Clear();
        _membershipsById.Clear();
        _accessGrantsById.Clear();
        _auditAliasesById.Clear();
        _activeActorIdsByUserId.Clear();
        _actorSwitchHistory.Clear();

        foreach (var user in state.Users)
        {
            _usersById[user.UserId] = user;
            _userIdsBySubjectId[user.SubjectId] = user.UserId;
        }

        foreach (var identity in state.Identities)
        {
            _identitiesById[identity.IdentityId] = identity;
            _identityIdsByProviderSubject[BuildProviderSubjectKey(identity.Provider, identity.ProviderSubject)] = identity.IdentityId;
        }

        foreach (var actor in state.Actors)
        {
            _actorsById[actor.ActorId] = actor;
        }

        foreach (var organization in state.Organizations)
        {
            _organizationsById[organization.OrganizationId] = organization;
        }

        foreach (var membership in state.Memberships)
        {
            _membershipsById[membership.MembershipId] = membership;
        }

        foreach (var grant in state.AccessGrants)
        {
            _accessGrantsById[grant.GrantId] = grant;
        }

        foreach (var alias in state.AuditAliases)
        {
            _auditAliasesById[alias.AliasId] = alias;
        }

        foreach (var selection in state.ActiveActorSelections)
        {
            _activeActorIdsByUserId[selection.UserId] = selection.ActorId;
        }

        _actorSwitchHistory.AddRange(state.ActorSwitchHistory.OrderBy(record => record.CreatedAtUtc));
    }

    private void PersistState()
    {
        _stateStore?.Save(new IdentityDirectoryRuntimeState
        {
            Users = _usersById.Values.OrderBy(user => user.UserId, StringComparer.Ordinal).ToArray(),
            Identities = _identitiesById.Values.OrderBy(identity => identity.IdentityId, StringComparer.Ordinal).ToArray(),
            Actors = _actorsById.Values.OrderBy(actor => actor.ActorId, StringComparer.Ordinal).ToArray(),
            Organizations = _organizationsById.Values.OrderBy(organization => organization.OrganizationId, StringComparer.Ordinal).ToArray(),
            Memberships = _membershipsById.Values.OrderBy(membership => membership.MembershipId, StringComparer.Ordinal).ToArray(),
            AccessGrants = _accessGrantsById.Values.OrderBy(grant => grant.GrantId, StringComparer.Ordinal).ToArray(),
            AuditAliases = _auditAliasesById.Values.OrderBy(alias => alias.AliasId, StringComparer.Ordinal).ToArray(),
            ActiveActorSelections = _activeActorIdsByUserId
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PersistedIdentityDirectoryActiveActorSelection
                {
                    UserId = pair.Key,
                    ActorId = pair.Value
                })
                .ToArray(),
            ActorSwitchHistory = _actorSwitchHistory.OrderBy(record => record.CreatedAtUtc).ToArray()
        });
    }

    private UseCaseResult<T> ExecutePersistedMutation<T>(string traceId, string useCaseName, Func<UseCaseResult<T>> operation)
    {
        lock (_syncRoot)
        {
            for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
            {
                if (_stateStore is not null)
                {
                    ReloadPersistedState();
                }

                try
                {
                    return operation();
                }
                catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
                {
                }
            }
        }

        return UseCaseResult<T>.Failure(
            UseCaseStatus.Conflict,
            $"{useCaseName} detected a concurrent runtime state update.",
            traceId,
            ["Identity directory runtime state changed repeatedly while the request was executing. Retry the request."]);
    }

    private static string CreateTraceId(string useCaseName) => $"{useCaseName}:{Guid.NewGuid():N}";

    private static string CreateId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string BuildProviderSubjectKey(string provider, string providerSubject) =>
        $"{provider.Trim()}::{providerSubject.Trim()}";

    private static string? NormalizeRequiredText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRequiredId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return metadata
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private bool TryGetUser(string userId, out IdentityDirectoryUserSummary user) =>
        _usersById.TryGetValue(NormalizeRequiredId(userId) ?? string.Empty, out user!);

    private bool UserExists(string userId) => _usersById.ContainsKey(NormalizeRequiredId(userId) ?? string.Empty);

    private bool TryGetActor(string actorId, out IdentityDirectoryActorSummary actor) =>
        _actorsById.TryGetValue(NormalizeRequiredId(actorId) ?? string.Empty, out actor!);

    private IdentityDirectoryActorSummary[] GetActorsByUser(string userId, bool includeProtectedActors)
    {
        return _actorsById.Values
            .Where(actor => string.Equals(actor.UserId, userId, StringComparison.Ordinal))
            .Where(actor => includeProtectedActors || actor.ActorType != IdentityDirectoryActorType.ProtectedInformant)
            .ToArray();
    }

    private IdentityDirectoryOrganizationSummary[] GetOrganizationsByUser(string userId)
    {
        var organizationIds = _membershipsById.Values
            .Where(membership => string.Equals(membership.UserId, userId, StringComparison.Ordinal))
            .Select(membership => membership.OrganizationId)
            .Concat(_actorsById.Values
                .Where(actor => string.Equals(actor.UserId, userId, StringComparison.Ordinal))
                .Select(actor => actor.OrganizationId)
                .OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return organizationIds
            .Where(organizationId => _organizationsById.ContainsKey(organizationId))
            .Select(organizationId => _organizationsById[organizationId])
            .ToArray();
    }

    private void EnsureDefaultPersonalActor(string userId)
    {
        if (!_usersById.TryGetValue(userId, out var user))
        {
            return;
        }

        var existingPersonalActor = _actorsById.Values.FirstOrDefault(actor =>
            string.Equals(actor.UserId, userId, StringComparison.Ordinal)
            && actor.ActorType == IdentityDirectoryActorType.Personal
            && actor.Status == IdentityDirectoryActorStatus.Active);

        if (existingPersonalActor is null)
        {
            existingPersonalActor = new IdentityDirectoryActorSummary
            {
                ActorId = CreateId("actor"),
                UserId = userId,
                BoundIdentityId = null,
                OrganizationId = null,
                ActorType = IdentityDirectoryActorType.Personal,
                DisplayName = user.DisplayName,
                VisibilityMode = IdentityDirectoryActorVisibilityMode.Public,
                Status = IdentityDirectoryActorStatus.Active,
                IsDefaultForIdentity = false
            };

            _actorsById[existingPersonalActor.ActorId] = existingPersonalActor;
        }

        if (string.IsNullOrWhiteSpace(user.DefaultActorId))
        {
            _usersById[userId] = user with { DefaultActorId = existingPersonalActor.ActorId };
        }
    }

    private IdentityDirectoryActorSummary? ResolvePreferredActor(IdentityDirectoryUserSummary user)
    {
        if (_activeActorIdsByUserId.TryGetValue(user.UserId, out var activeActorId)
            && _actorsById.TryGetValue(activeActorId, out var selectedActor)
            && selectedActor.Status == IdentityDirectoryActorStatus.Active)
        {
            return selectedActor;
        }

        if (!string.IsNullOrWhiteSpace(user.DefaultActorId)
            && _actorsById.TryGetValue(user.DefaultActorId, out var defaultActor)
            && defaultActor.Status == IdentityDirectoryActorStatus.Active)
        {
            return defaultActor;
        }

        return GetActorsByUser(user.UserId, includeProtectedActors: true)
            .Where(actor => actor.Status == IdentityDirectoryActorStatus.Active)
            .OrderBy(actor => actor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private UseCaseResult<IdentityDirectoryActorSummary?> ResolveActiveActorForUser(IdentityDirectoryUserSummary user, string? requestedActorId, string traceId)
    {
        if (!string.IsNullOrWhiteSpace(requestedActorId))
        {
            if (!TryGetActiveActorForUser(user.UserId, requestedActorId, out var requestedActor, out var actorError))
            {
                return UseCaseResult<IdentityDirectoryActorSummary?>.Failure(
                    actorError!.Value.Status,
                    actorError.Value.Summary,
                    traceId,
                    actorError.Value.Errors);
            }

            return UseCaseResult<IdentityDirectoryActorSummary?>.Success(requestedActor, "Resolved requested active actor for the user.", traceId);
        }

        return UseCaseResult<IdentityDirectoryActorSummary?>.Success(ResolvePreferredActor(user), "Resolved preferred active actor for the user.", traceId);
    }

    private bool TryGetActiveActorForUser(
        string userId,
        string actorId,
        out IdentityDirectoryActorSummary actor,
        out (UseCaseStatus Status, string Summary, IReadOnlyList<string> Errors)? error)
    {
        actor = default!;
        error = null;

        if (!_actorsById.TryGetValue(NormalizeRequiredId(actorId) ?? string.Empty, out var existingActor))
        {
            error = (UseCaseStatus.NotFound, "Identity directory actor was not found.", [$"Actor '{actorId}' does not exist."]);
            return false;
        }

        if (!string.Equals(existingActor.UserId, userId, StringComparison.Ordinal))
        {
            error = (UseCaseStatus.PermissionDenied, "The requested actor does not belong to the user.", [$"Actor '{actorId}' does not belong to user '{userId}'."]);
            return false;
        }

        if (existingActor.Status != IdentityDirectoryActorStatus.Active)
        {
            error = (UseCaseStatus.Conflict, "The requested actor is not active.", [$"Actor '{actorId}' is currently '{existingActor.Status}'."]);
            return false;
        }

        actor = existingActor;
        return true;
    }

    private IdentityDirectoryUserSessionView BuildUserSessionView(IdentityDirectoryUserSummary user, IdentityDirectoryActorSummary? activeActor)
    {
        return new IdentityDirectoryUserSessionView
        {
            User = user,
            ActiveActor = activeActor,
            AvailableActors = GetActorsByUser(user.UserId, includeProtectedActors: true)
                .Where(actor => actor.Status == IdentityDirectoryActorStatus.Active)
                .OrderBy(actor => actor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Identities = _identitiesById.Values
                .Where(identity => string.Equals(identity.UserId, user.UserId, StringComparison.Ordinal))
                .OrderBy(identity => identity.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
