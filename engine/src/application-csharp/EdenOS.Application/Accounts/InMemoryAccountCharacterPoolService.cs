using EdenOS.Contracts.Accounts;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.IdentityDirectory;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;
using EdenOS.Application.RuntimeState;

namespace EdenOS.Application.Accounts;

public sealed class InMemoryAccountCharacterPoolService : IWorkspaceService, ICharacterPoolService, IOperatorPoolService, IWorkspaceBoundMarketAccessService
{
    private const int PersistenceRetryCount = 3;

    private static readonly AccountPropagationPolicy DefaultPropagationPolicy = new();
    private static readonly WorkspaceIsolationBoundary DefaultIsolationBoundary = new();

    private readonly IWorkspaceMarketGrantStore _marketGrantStore;
    private readonly IIdentityDirectoryService? _identityDirectoryService;
    private readonly LocalJsonStateStore<WorkspaceRuntimeState>? _stateStore;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WorkspaceRecord> _recordsByWorkspaceId = [];
    private readonly Dictionary<string, WorkspaceRecord> _recordsByPrincipalKey = [];
    private readonly Dictionary<string, WorkspaceRecord> _recordsByAccountKey = [];

    public InMemoryAccountCharacterPoolService()
        : this(LocalWorkspaceMarketGrantStore.LoadDefaultOrBootstrapFallback(), stateStore: null, identityDirectoryService: null)
    {
    }

    internal InMemoryAccountCharacterPoolService(
        IWorkspaceMarketGrantStore marketGrantStore,
        LocalJsonStateStore<WorkspaceRuntimeState>? stateStore,
        IIdentityDirectoryService? identityDirectoryService = null)
    {
        _marketGrantStore = marketGrantStore;
        _stateStore = stateStore;
        _identityDirectoryService = identityDirectoryService;
        ReloadPersistedState();
    }

    public UseCaseResult<WorkspaceContext> Open(OpenWorkspaceRequest request)
    {
        var traceId = CreateTraceId("workspace.open");
        return ExecutePersistedMutation(traceId, "workspace.open", () =>
        {
            if (string.IsNullOrWhiteSpace(request.Principal.SubjectCharacterId)
                || string.IsNullOrWhiteSpace(request.Principal.CharacterName))
            {
                return UseCaseResult<WorkspaceContext>.Failure(
                    UseCaseStatus.InvalidInput,
                    "workspace.open requires a credential-bound principal.",
                    traceId,
                    ["Principal character id and name are required."]);
            }

            var identityContextResult = ResolveIdentityContext(request.Principal, traceId);
            if (!identityContextResult.IsSuccess)
            {
                return UseCaseResult<WorkspaceContext>.Failure(
                    identityContextResult.Status,
                    identityContextResult.Summary,
                    traceId,
                    identityContextResult.Errors);
            }

            var identityContext = identityContextResult.Data;
            var primaryPrincipalKey = identityContext?.PrincipalKey ?? BuildLegacyPrincipalKey(request.Principal.Provider, request.Principal.SubjectCharacterId);
            var legacyPrincipalKey = BuildLegacyPrincipalKey(request.Principal.Provider, request.Principal.SubjectCharacterId);
            var foundExisting = _recordsByPrincipalKey.TryGetValue(primaryPrincipalKey, out var existing)
                || (!string.Equals(primaryPrincipalKey, legacyPrincipalKey, StringComparison.Ordinal)
                    && _recordsByPrincipalKey.TryGetValue(legacyPrincipalKey, out existing));
            if (foundExisting && existing is not null)
            {
                var updatedAccount = BuildAccountContext(
                    request.Principal,
                    identityContext,
                    fallbackAccountId: existing.Workspace.Account.AccountId);
                existing.Workspace = existing.Workspace with
                {
                    WorkspaceName = request.PreferredWorkspaceName ?? existing.Workspace.WorkspaceName,
                    Account = updatedAccount
                };

                EnsureEsiCharacterAttached(existing, request.Principal, identityContext?.OperatorDisplayName);
                ReindexRecord(existing);

                PersistState();

                return UseCaseResult<WorkspaceContext>.Success(
                    existing.Workspace,
                    identityContext is null
                        ? "Opened existing workspace from credential-bound account context."
                        : "Opened existing workspace from identity-directory user context.",
                    traceId);
            }

            var workspaceId = CreateId("workspace");
            var characterPoolId = CreateId("character_pool");
            var account = BuildAccountContext(
                request.Principal,
                identityContext,
                fallbackAccountId: identityContext?.UserId ?? CreateId("account"));

            var workspace = new WorkspaceContext
            {
                WorkspaceId = workspaceId,
                WorkspaceName = request.PreferredWorkspaceName ?? $"{request.Principal.CharacterName}-workspace",
                Account = account,
                CharacterPoolId = characterPoolId,
                IsolationBoundary = DefaultIsolationBoundary
            };

            var record = new WorkspaceRecord
            {
                Workspace = workspace
            };

            EnsureEsiCharacterAttached(record, request.Principal, identityContext?.OperatorDisplayName);

            _recordsByWorkspaceId[workspaceId] = record;
            ReindexRecord(record);
            PersistState();

            return UseCaseResult<WorkspaceContext>.Success(
                workspace,
                identityContext is null
                    ? "Opened new workspace from credential-bound account context."
                    : "Opened new workspace from identity-directory user context.",
                traceId);
        });
    }

    public UseCaseResult<WorkspaceSummary> GetSummary(GetWorkspaceSummaryRequest request)
    {
        var traceId = CreateTraceId("workspace.get_summary");
        var recordResult = GetRecord(request.WorkspaceId, traceId, "workspace.get_summary");
        if (!recordResult.IsSuccess)
        {
            return UseCaseResult<WorkspaceSummary>.Failure(
                recordResult.Status,
                recordResult.Summary,
                traceId,
                recordResult.Errors);
        }

        var record = recordResult.Data!;
        var characters = ListCharacters(record);
        var summary = new WorkspaceSummary
        {
            WorkspaceId = record.Workspace.WorkspaceId,
            WorkspaceName = record.Workspace.WorkspaceName,
            Account = record.Workspace.Account,
            CharacterPoolId = record.Workspace.CharacterPoolId,
            TotalCharacters = characters.Count,
            EsiCharacters = characters.Count(character => character.SourceKind == CharacterSourceKind.EsiBound),
            VirtualCharacters = characters.Count(character => character.SourceKind == CharacterSourceKind.Virtual),
            TotalOperators = record.Operators.Count,
            Characters = characters,
            Operators = ListOperators(record)
        };

        return UseCaseResult<WorkspaceSummary>.Success(
            summary,
            "Loaded workspace summary.",
            traceId);
    }

    public UseCaseResult<CharacterPoolView> List(ListCharacterPoolRequest request)
    {
        var traceId = CreateTraceId("character_pool.list");
        var recordResult = GetRecord(request.WorkspaceId, traceId, "character_pool.list");
        if (!recordResult.IsSuccess)
        {
            return UseCaseResult<CharacterPoolView>.Failure(
                recordResult.Status,
                recordResult.Summary,
                traceId,
                recordResult.Errors);
        }

        var record = recordResult.Data!;
        var view = new CharacterPoolView
        {
            WorkspaceId = record.Workspace.WorkspaceId,
            CharacterPoolId = record.Workspace.CharacterPoolId,
            Characters = ListCharacters(record)
        };

        return UseCaseResult<CharacterPoolView>.Success(
            view,
            "Loaded character pool.",
            traceId);
    }

    public UseCaseResult<OperatorPoolView> ListOperators(ListOperatorPoolRequest request)
    {
        var traceId = CreateTraceId("operator_pool.list");
        var recordResult = GetRecord(request.WorkspaceId, traceId, "operator_pool.list");
        if (!recordResult.IsSuccess)
        {
            return UseCaseResult<OperatorPoolView>.Failure(
                recordResult.Status,
                recordResult.Summary,
                traceId,
                recordResult.Errors);
        }

        return UseCaseResult<OperatorPoolView>.Success(
            new OperatorPoolView
            {
                WorkspaceId = recordResult.Data!.Workspace.WorkspaceId,
                Operators = ListOperators(recordResult.Data!)
            },
            "Loaded operator pool.",
            traceId);
    }

    public UseCaseResult<OperatorPoolEntry> AddOperator(AddOperatorRequest request)
    {
        var traceId = CreateTraceId("operator_pool.add");
        return ExecutePersistedMutation(traceId, "operator_pool.add", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "operator_pool.add");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            var displayName = NormalizeRequiredName(request.DisplayName);
            if (displayName is null)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Operator display name is required.",
                    traceId,
                    ["DisplayName cannot be empty."]);
            }

            if (request.MaxDailyManualOperations.HasValue && request.MaxDailyManualOperations.Value <= 0)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Operator manual-operation budget must be greater than zero when provided.",
                    traceId,
                    ["MaxDailyManualOperations must be greater than zero when provided."]);
            }

            if (HasOperatorDisplayNameCollision(record, displayName, ignoredOperatorId: null))
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.Conflict,
                    "Operator display name already exists in this workspace.",
                    traceId,
                    [$"An operator named '{displayName}' already exists."]);
            }

            var entry = new OperatorPoolEntry
            {
                WorkspaceId = record.Workspace.WorkspaceId,
                OperatorId = CreateId("operator"),
                DisplayName = displayName,
                CanCoordinateIndustryPlanning = request.CanCoordinateIndustryPlanning,
                MaxDailyManualOperations = request.MaxDailyManualOperations,
                ResponsibilityTags = NormalizeTags(request.ResponsibilityTags),
                Notes = NormalizeOptionalText(request.Notes)
            };

            record.Operators[entry.OperatorId] = entry;
            PersistState();
            return UseCaseResult<OperatorPoolEntry>.Success(entry, "Added operator to the workspace.", traceId);
        });
    }

    public UseCaseResult<OperatorPoolEntry> UpdateOperator(UpdateOperatorRequest request)
    {
        var traceId = CreateTraceId("operator_pool.update");
        return ExecutePersistedMutation(traceId, "operator_pool.update", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "operator_pool.update");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (!record.Operators.TryGetValue(request.OperatorId, out var current))
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.NotFound,
                    "Operator was not found in the workspace.",
                    traceId,
                    [$"Operator '{request.OperatorId}' does not exist."]);
            }

            var displayName = request.DisplayName is null
                ? current.DisplayName
                : NormalizeRequiredName(request.DisplayName);
            if (displayName is null)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Operator display name is required.",
                    traceId,
                    ["DisplayName cannot be empty when provided."]);
            }

            if (request.MaxDailyManualOperations.HasValue && request.MaxDailyManualOperations.Value <= 0)
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Operator manual-operation budget must be greater than zero when provided.",
                    traceId,
                    ["MaxDailyManualOperations must be greater than zero when provided."]);
            }

            if (HasOperatorDisplayNameCollision(record, displayName, ignoredOperatorId: current.OperatorId))
            {
                return UseCaseResult<OperatorPoolEntry>.Failure(
                    UseCaseStatus.Conflict,
                    "Operator display name already exists in this workspace.",
                    traceId,
                    [$"An operator named '{displayName}' already exists."]);
            }

            var updated = current with
            {
                DisplayName = displayName,
                CanCoordinateIndustryPlanning = request.CanCoordinateIndustryPlanning ?? current.CanCoordinateIndustryPlanning,
                MaxDailyManualOperations = request.MaxDailyManualOperations == default ? current.MaxDailyManualOperations : request.MaxDailyManualOperations,
                ResponsibilityTags = request.ResponsibilityTags is null ? current.ResponsibilityTags : NormalizeTags(request.ResponsibilityTags),
                Notes = request.Notes is null ? current.Notes : NormalizeOptionalText(request.Notes)
            };

            record.Operators[updated.OperatorId] = updated;
            PersistState();
            return UseCaseResult<OperatorPoolEntry>.Success(updated, "Updated operator in the workspace.", traceId);
        });
    }

    public UseCaseResult<CharacterPoolEntry> AssignCharacterOperator(AssignCharacterOperatorRequest request)
    {
        var traceId = CreateTraceId("operator_pool.assign_character");
        return ExecutePersistedMutation(traceId, "operator_pool.assign_character", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "operator_pool.assign_character");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (!record.Characters.TryGetValue(request.CharacterId, out var currentCharacter))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.NotFound,
                    "Character was not found in the workspace.",
                    traceId,
                    [$"Character '{request.CharacterId}' does not exist."]);
            }

            var operatorId = NormalizeOptionalId(request.OperatorId);
            if (operatorId is not null && !record.Operators.ContainsKey(operatorId))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.NotFound,
                    "Operator was not found in the workspace.",
                    traceId,
                    [$"Operator '{operatorId}' does not exist."]);
            }

            var updated = currentCharacter with { OperatorId = operatorId };
            record.Characters[updated.CharacterId] = updated;
            PersistState();
            return UseCaseResult<CharacterPoolEntry>.Success(updated, "Updated character operator assignment.", traceId);
        });
    }

    public UseCaseResult<CharacterPoolEntry> AddVirtualCharacter(AddVirtualCharacterRequest request)
    {
        var traceId = CreateTraceId("character_pool.add_virtual_character");
        return ExecutePersistedMutation(traceId, "character_pool.add_virtual_character", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "character_pool.add_virtual_character");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual character display name is required.",
                    traceId,
                    ["DisplayName cannot be empty."]);
            }

            if (request.CapabilityProfile.CanProvideAuthenticatedMarketAccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual characters cannot provide authenticated market access.",
                    traceId,
                    ["Set CanProvideAuthenticatedMarketAccess to false for virtual characters."]);
            }

            if (HasVirtualDisplayNameCollision(record, request.DisplayName, ignoredCharacterId: null))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.Conflict,
                    "Virtual character display name already exists in this workspace.",
                    traceId,
                    [$"A virtual character named '{request.DisplayName}' already exists."]);
            }

            var operatorValidation = ValidateRequestedOperator(record, request.OperatorId, traceId);
            if (!operatorValidation.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    operatorValidation.Status,
                    operatorValidation.Summary,
                    traceId,
                    operatorValidation.Errors);
            }

            var character = new CharacterPoolEntry
            {
                CharacterId = CreateId("character"),
                CharacterPoolId = record.Workspace.CharacterPoolId,
                DisplayName = request.DisplayName,
                SourceKind = CharacterSourceKind.Virtual,
                VirtualKind = request.VirtualKind,
                OperatorId = operatorValidation.Data,
                CapabilityProfile = request.CapabilityProfile,
                IsPrimaryAccountCharacter = false,
                IsSelectableForIndustryPlanning = request.IsSelectableForIndustryPlanning,
                IsSelectableForMarketAccess = false,
                Notes = NormalizeOptionalText(request.Notes)
            };

            record.Characters[character.CharacterId] = character;
            PersistState();

            return UseCaseResult<CharacterPoolEntry>.Success(
                character,
                "Added virtual character to the shared character pool.",
                traceId);
        });
    }

    public UseCaseResult<CharacterPoolEntry> UpdateVirtualCharacter(UpdateVirtualCharacterRequest request)
    {
        var traceId = CreateTraceId("character_pool.update_virtual_character");
        return ExecutePersistedMutation(traceId, "character_pool.update_virtual_character", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "character_pool.update_virtual_character");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (!record.Characters.TryGetValue(request.CharacterId, out var current))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.NotFound,
                    "Virtual character was not found in the workspace.",
                    traceId,
                    [$"Character '{request.CharacterId}' does not exist."]);
            }

            if (current.SourceKind != CharacterSourceKind.Virtual)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Only virtual characters can be updated through character_pool.update_virtual_character.",
                    traceId,
                    [$"Character '{request.CharacterId}' is not virtual."]);
            }

            var nextDisplayName = request.DisplayName ?? current.DisplayName;
            var nextCapabilityProfile = request.CapabilityProfile ?? current.CapabilityProfile;

            if (string.IsNullOrWhiteSpace(nextDisplayName))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual character display name is required.",
                    traceId,
                    ["DisplayName cannot be empty."]);
            }

            if (nextCapabilityProfile.CanProvideAuthenticatedMarketAccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Virtual characters cannot provide authenticated market access.",
                    traceId,
                    ["Set CanProvideAuthenticatedMarketAccess to false for virtual characters."]);
            }

            if (HasVirtualDisplayNameCollision(record, nextDisplayName, request.CharacterId))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.Conflict,
                    "Virtual character display name already exists in this workspace.",
                    traceId,
                    [$"A virtual character named '{nextDisplayName}' already exists."]);
            }

            var operatorValidation = request.OperatorId is null
                ? UseCaseResult<string?>.Success(current.OperatorId, "Operator assignment unchanged.", traceId)
                : ValidateRequestedOperator(record, request.OperatorId, traceId);
            if (!operatorValidation.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    operatorValidation.Status,
                    operatorValidation.Summary,
                    traceId,
                    operatorValidation.Errors);
            }

            var updated = current with
            {
                DisplayName = nextDisplayName,
                OperatorId = operatorValidation.Data,
                CapabilityProfile = nextCapabilityProfile,
                IsSelectableForIndustryPlanning = request.IsSelectableForIndustryPlanning ?? current.IsSelectableForIndustryPlanning,
                IsSelectableForMarketAccess = false,
                Notes = request.Notes is null ? current.Notes : NormalizeOptionalText(request.Notes)
            };

            record.Characters[updated.CharacterId] = updated;
            PersistState();

            return UseCaseResult<CharacterPoolEntry>.Success(
                updated,
                "Updated virtual character in the shared character pool.",
                traceId);
        });
    }

    public UseCaseResult<CharacterPoolEntry> AttachEsiCharacter(AttachEsiCharacterRequest request)
    {
        var traceId = CreateTraceId("character_pool.attach_esi_character");
        return ExecutePersistedMutation(traceId, "character_pool.attach_esi_character", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "character_pool.attach_esi_character");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (string.IsNullOrWhiteSpace(request.EsiCharacterId)
                || string.IsNullOrWhiteSpace(request.CharacterName))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "ESI character id and character name are required.",
                    traceId,
                    ["EsiCharacterId and CharacterName cannot be empty."]);
            }

            var characterId = record.EsiCharacterIds.TryGetValue(request.EsiCharacterId, out var existingCharacterId)
                ? existingCharacterId
                : CreateId("character");

            var existing = record.Characters.TryGetValue(characterId, out var currentCharacter)
                ? currentCharacter
                : null;

            var operatorValidation = request.OperatorId is null
                ? UseCaseResult<string?>.Success(existing?.OperatorId, "Operator assignment unchanged.", traceId)
                : ValidateRequestedOperator(record, request.OperatorId, traceId);
            if (!operatorValidation.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    operatorValidation.Status,
                    operatorValidation.Summary,
                    traceId,
                    operatorValidation.Errors);
            }

            var character = new CharacterPoolEntry
            {
                CharacterId = characterId,
                CharacterPoolId = record.Workspace.CharacterPoolId,
                DisplayName = request.CharacterName,
                SourceKind = CharacterSourceKind.EsiBound,
                EsiCharacterId = request.EsiCharacterId,
                OperatorId = operatorValidation.Data,
                CapabilityProfile = request.CapabilityProfile,
                IsPrimaryAccountCharacter = existing?.IsPrimaryAccountCharacter ?? false,
                IsSelectableForIndustryPlanning = request.IsSelectableForIndustryPlanning,
                IsSelectableForMarketAccess = request.IsSelectableForMarketAccess,
                Notes = request.Notes is null ? existing?.Notes : NormalizeOptionalText(request.Notes)
            };

            record.Characters[character.CharacterId] = character;
            record.EsiCharacterIds[request.EsiCharacterId] = character.CharacterId;

            if (request.IsSelectableForMarketAccess && request.CapabilityProfile.CanProvideAuthenticatedMarketAccess)
            {
                MergeMarketAccessGrants(record, request.EsiCharacterId);
            }

            PersistState();

            return UseCaseResult<CharacterPoolEntry>.Success(
                character,
                existing is null
                    ? "Attached ESI character to the shared character pool."
                    : "Updated attached ESI character in the shared character pool.",
                traceId);
        });
    }

    public UseCaseResult<CharacterPoolEntry> UpdateEsiCharacterCapabilityProfile(UpdateEsiCharacterCapabilityProfileRequest request)
    {
        var traceId = CreateTraceId("character_pool.update_esi_character_capability_profile");
        return ExecutePersistedMutation(traceId, "character_pool.update_esi_character_capability_profile", () =>
        {
            var recordResult = GetRecord(request.WorkspaceId, traceId, "character_pool.update_esi_character_capability_profile");
            if (!recordResult.IsSuccess)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    recordResult.Status,
                    recordResult.Summary,
                    traceId,
                    recordResult.Errors);
            }

            var record = recordResult.Data!;
            if (string.IsNullOrWhiteSpace(request.EsiCharacterId))
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.InvalidInput,
                    "ESI character id is required.",
                    traceId,
                    ["EsiCharacterId cannot be empty."]);
            }

            if (!record.EsiCharacterIds.TryGetValue(request.EsiCharacterId, out var characterId)
                || !record.Characters.TryGetValue(characterId, out var currentCharacter)
                || currentCharacter.SourceKind != CharacterSourceKind.EsiBound)
            {
                return UseCaseResult<CharacterPoolEntry>.Failure(
                    UseCaseStatus.NotFound,
                    "The workspace does not contain the requested attached ESI character.",
                    traceId,
                    [$"ESI character '{request.EsiCharacterId}' is not attached to workspace '{request.WorkspaceId}'."]);
            }

            var updated = currentCharacter with
            {
                CapabilityProfile = request.CapabilityProfile,
                Notes = request.Notes is null ? currentCharacter.Notes : NormalizeOptionalText(request.Notes)
            };

            record.Characters[updated.CharacterId] = updated;
            PersistState();

            return UseCaseResult<CharacterPoolEntry>.Success(
                updated,
                "Updated attached ESI character capability profile.",
                traceId);
        });
    }

    public UseCaseResult<WorkspaceMarketAccessCatalog> GetMarketAccessCatalog(string accountKey)
    {
        var traceId = CreateTraceId("market.get_access_catalog");
        if (string.IsNullOrWhiteSpace(accountKey))
        {
            return UseCaseResult<WorkspaceMarketAccessCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "Account key is required.",
                traceId,
                ["account_key must not be empty."]);
        }

        if (!_recordsByAccountKey.TryGetValue(accountKey, out var record))
        {
            return UseCaseResult<WorkspaceMarketAccessCatalog>.Failure(
                UseCaseStatus.PermissionDenied,
                $"Account '{accountKey}' is not authorized for market structure access.",
                traceId,
                ["The provided account key is not bound to an opened workspace account context."]);
        }

        return UseCaseResult<WorkspaceMarketAccessCatalog>.Success(
            new WorkspaceMarketAccessCatalog(
                accountKey,
                record.StructureGrants.Values
                    .OrderBy(grant => grant.StructureId)
                    .ToArray(),
                _marketGrantStore.Source,
                _marketGrantStore.BundleVersion),
            "Resolved workspace-bound market access catalog.",
            traceId);
    }

    public UseCaseResult<WorkspaceStructureAccessCatalog> GetStructureAccessCatalog(string workspaceId)
    {
        var traceId = CreateTraceId("market.get_structure_access_catalog");
        var recordResult = GetRecord(workspaceId, traceId, "market.get_structure_access_catalog");
        if (!recordResult.IsSuccess)
        {
            return UseCaseResult<WorkspaceStructureAccessCatalog>.Failure(
                recordResult.Status,
                recordResult.Summary,
                traceId,
                recordResult.Errors);
        }

        var record = recordResult.Data!;
        var structuresById = new Dictionary<long, StructureAccessAccumulator>();
        var eligibleEsiCharacters = record.Characters.Values
            .Where(character =>
                character.SourceKind == CharacterSourceKind.EsiBound
                && character.IsSelectableForMarketAccess
                && character.CapabilityProfile.CanProvideAuthenticatedMarketAccess
                && !string.IsNullOrWhiteSpace(character.EsiCharacterId))
            .Select(character => character.EsiCharacterId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        foreach (var esiCharacterId in eligibleEsiCharacters)
        {
            foreach (var grant in _marketGrantStore.GetGrantsForEsiCharacter(esiCharacterId))
            {
                if (!structuresById.TryGetValue(grant.StructureId, out var accumulator))
                {
                    accumulator = new StructureAccessAccumulator(
                        grant.StructureId,
                        grant.GrantSource,
                        grant.LastVerifiedAtUtc,
                        grant.IsStale);
                    structuresById[grant.StructureId] = accumulator;
                }
                else if (grant.LastVerifiedAtUtc > accumulator.LastVerifiedAtUtc
                         || (accumulator.IsStale && !grant.IsStale))
                {
                    accumulator.GrantSource = grant.GrantSource;
                    accumulator.LastVerifiedAtUtc = grant.LastVerifiedAtUtc;
                    accumulator.IsStale = grant.IsStale;
                }

                accumulator.AuthorizedEsiCharacterIds.Add(esiCharacterId);
            }
        }

        var structures = structuresById.Values
            .OrderBy(value => value.StructureId)
            .Select(value => new WorkspaceStructureAccessBinding(
                value.StructureId,
                value.GrantSource,
                value.LastVerifiedAtUtc,
                value.IsStale,
                value.AuthorizedEsiCharacterIds
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        return UseCaseResult<WorkspaceStructureAccessCatalog>.Success(
            new WorkspaceStructureAccessCatalog(
                record.Workspace.WorkspaceId,
                record.Workspace.Account.IsolationKey,
                structures,
                _marketGrantStore.Source,
                _marketGrantStore.BundleVersion),
            "Resolved workspace structure access catalog.",
            traceId);
    }

    private static string BuildLegacyPrincipalKey(CredentialProvider provider, string subjectCharacterId) =>
        $"{provider}:{subjectCharacterId.Trim()}";

    private static string BuildSubjectPrincipalKey(string subjectId) =>
        $"subject:{subjectId.Trim()}";

    private static string CreateId(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string CreateTraceId(string useCaseName)
    {
        return $"{useCaseName}:{Guid.NewGuid():N}";
    }

    private static List<CharacterPoolEntry> ListCharacters(WorkspaceRecord record)
    {
        return record.Characters.Values
            .OrderByDescending(character => character.IsPrimaryAccountCharacter)
            .ThenBy(character => character.SourceKind)
            .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<OperatorPoolEntry> ListOperators(WorkspaceRecord record)
    {
        return record.Operators.Values
            .OrderBy(operatorEntry => operatorEntry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasVirtualDisplayNameCollision(
        WorkspaceRecord record,
        string displayName,
        string? ignoredCharacterId)
    {
        return record.Characters.Values.Any(character =>
            character.SourceKind == CharacterSourceKind.Virtual
            && !string.Equals(character.CharacterId, ignoredCharacterId, StringComparison.Ordinal)
            && string.Equals(character.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasOperatorDisplayNameCollision(
        WorkspaceRecord record,
        string displayName,
        string? ignoredOperatorId)
    {
        return record.Operators.Values.Any(operatorEntry =>
            !string.Equals(operatorEntry.OperatorId, ignoredOperatorId, StringComparison.Ordinal)
            && string.Equals(operatorEntry.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeRequiredName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOptionalId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tags)
    {
        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UseCaseResult<string?> ValidateRequestedOperator(WorkspaceRecord record, string? operatorId, string traceId)
    {
        var normalizedOperatorId = NormalizeOptionalId(operatorId);
        if (normalizedOperatorId is null)
        {
            return UseCaseResult<string?>.Success(null, "No operator assignment requested.", traceId);
        }

        if (!record.Operators.ContainsKey(normalizedOperatorId))
        {
            return UseCaseResult<string?>.Failure(
                UseCaseStatus.NotFound,
                "Operator was not found in the workspace.",
                traceId,
                [$"Operator '{normalizedOperatorId}' does not exist."]);
        }

        return UseCaseResult<string?>.Success(normalizedOperatorId, "Resolved operator assignment.", traceId);
    }

    private void MergeMarketAccessGrants(WorkspaceRecord record, string esiCharacterId)
    {
        foreach (var grant in _marketGrantStore.GetGrantsForEsiCharacter(esiCharacterId))
        {
            record.StructureGrants[grant.StructureId] = grant;
        }
    }

    private UseCaseResult<WorkspaceIdentityContext?> ResolveIdentityContext(CredentialBoundPrincipal principal, string traceId)
    {
        if (_identityDirectoryService is null || principal.Provider == CredentialProvider.Local)
        {
            return UseCaseResult<WorkspaceIdentityContext?>.Success(null, "Identity directory is not configured for workspace aggregation.", traceId);
        }

        var subjectId = NormalizeOptionalText(principal.PlatformSubjectId)
            ?? BuildLegacyPlatformSubjectId(principal.Provider, principal.SubjectCharacterId);
        var displayName = NormalizeOptionalText(principal.PlatformDisplayName)
            ?? principal.CharacterName.Trim();
        var email = NormalizeOptionalText(principal.PrimaryEmail);

        var sessionResult = _identityDirectoryService.BootstrapUserSession(new BootstrapIdentityDirectoryUserSessionRequest
        {
            SubjectId = subjectId,
            DisplayName = displayName,
            PrimaryEmail = email,
            ClientId = "workspace.open"
        });
        if (!sessionResult.IsSuccess || sessionResult.Data is null)
        {
            return UseCaseResult<WorkspaceIdentityContext?>.Failure(
                sessionResult.Status,
                sessionResult.Summary,
                traceId,
                sessionResult.Errors,
                sessionResult.Warnings);
        }

        var session = sessionResult.Data;
        var attachResult = _identityDirectoryService.AttachExternalIdentity(new AttachExternalIdentityRequest
        {
            UserId = session.User.UserId,
            IdentityType = IdentityDirectoryIdentityType.EsiCharacter,
            Provider = "ccp_esi",
            ProviderSubject = principal.SubjectCharacterId.Trim(),
            DisplayLabel = principal.CharacterName.Trim(),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["credential_provider"] = principal.Provider.ToString().ToLowerInvariant()
            }
        });
        if (!attachResult.IsSuccess)
        {
            return UseCaseResult<WorkspaceIdentityContext?>.Failure(
                attachResult.Status,
                attachResult.Summary,
                traceId,
                attachResult.Errors,
                attachResult.Warnings);
        }

        return UseCaseResult<WorkspaceIdentityContext?>.Success(
            new WorkspaceIdentityContext
            {
                UserId = session.User.UserId,
                SubjectId = session.User.SubjectId,
                ActiveActorId = session.ActiveActor?.ActorId,
                PrincipalKey = BuildSubjectPrincipalKey(session.User.SubjectId),
                OperatorDisplayName = session.User.DisplayName
            },
            "Resolved workspace identity through identity directory.",
            traceId);
    }

    private static string BuildLegacyPlatformSubjectId(CredentialProvider provider, string subjectCharacterId) =>
        $"legacy:{provider.ToString().ToLowerInvariant()}:{subjectCharacterId.Trim()}";

    private static AccountContext BuildAccountContext(
        CredentialBoundPrincipal principal,
        WorkspaceIdentityContext? identityContext,
        string fallbackAccountId)
    {
        if (identityContext is not null)
        {
            return new AccountContext
            {
                AccountId = identityContext.UserId,
                Principal = principal,
                IsolationKey = BuildUserIsolationKey(identityContext.UserId),
                UserId = identityContext.UserId,
                SubjectId = identityContext.SubjectId,
                ActiveActorId = identityContext.ActiveActorId,
                PropagationPolicy = DefaultPropagationPolicy
            };
        }

        return new AccountContext
        {
            AccountId = fallbackAccountId,
            Principal = principal,
            IsolationKey = BuildLegacyPrincipalKey(principal.Provider, principal.SubjectCharacterId),
            PropagationPolicy = DefaultPropagationPolicy
        };
    }

    private static string BuildUserIsolationKey(string userId) => $"user:{userId.Trim()}";

    private string EnsureDefaultOperator(WorkspaceRecord record, string? preferredDisplayName)
    {
        if (record.Operators.Count > 0)
        {
            return record.Operators.Values
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.OperatorId, StringComparer.Ordinal)
                .First()
                .OperatorId;
        }

        var defaultOperatorId = CreateId("operator");
        record.Operators[defaultOperatorId] = new OperatorPoolEntry
        {
            WorkspaceId = record.Workspace.WorkspaceId,
            OperatorId = defaultOperatorId,
            DisplayName = NormalizeOptionalText(preferredDisplayName) ?? record.Workspace.Account.Principal.CharacterName,
            CanCoordinateIndustryPlanning = true,
            Notes = "Default workspace operator derived from the account root."
        };
        return defaultOperatorId;
    }

    private void EnsureEsiCharacterAttached(WorkspaceRecord record, CredentialBoundPrincipal principal, string? operatorDisplayName)
    {
        if (principal.Provider == CredentialProvider.Local)
        {
            EnsureDefaultOperator(record, operatorDisplayName ?? principal.CharacterName);
            return;
        }
        var esiCharacterId = principal.SubjectCharacterId.Trim();
        if (record.EsiCharacterIds.TryGetValue(esiCharacterId, out var existingCharacterId)
            && record.Characters.TryGetValue(existingCharacterId, out var existingCharacter))
        {
            record.Characters[existingCharacterId] = existingCharacter with
            {
                DisplayName = principal.CharacterName.Trim(),
                IsSelectableForMarketAccess = true,
                IsSelectableForIndustryPlanning = true
            };

            MergeMarketAccessGrants(record, esiCharacterId);
            return;
        }

        var operatorId = EnsureDefaultOperator(record, operatorDisplayName);
        var characterId = CreateId("character");
        record.Characters[characterId] = new CharacterPoolEntry
        {
            CharacterId = characterId,
            CharacterPoolId = record.Workspace.CharacterPoolId,
            DisplayName = principal.CharacterName.Trim(),
            SourceKind = CharacterSourceKind.EsiBound,
            EsiCharacterId = esiCharacterId,
            OperatorId = operatorId,
            CapabilityProfile = new CharacterCapabilityProfile
            {
                SkillProfile = "credential-bound-esi",
                CanParticipateInIndustryPlanning = true,
                CanProvideAuthenticatedMarketAccess = true,
                CanCoverLogisticsTasks = false,
                IsUnlimitedSkillSimulation = false
            },
            IsPrimaryAccountCharacter = record.Characters.Values.All(character => !character.IsPrimaryAccountCharacter),
            IsSelectableForIndustryPlanning = true,
            IsSelectableForMarketAccess = true,
            Notes = "Credential-bound ESI character attached to the workspace."
        };

        record.EsiCharacterIds[esiCharacterId] = characterId;
        MergeMarketAccessGrants(record, esiCharacterId);
    }

    private UseCaseResult<WorkspaceRecord> GetRecord(string workspaceId, string traceId, string useCaseName)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return UseCaseResult<WorkspaceRecord>.Failure(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires a workspace id.",
                traceId,
                ["WorkspaceId cannot be empty."]);
        }

        if (_recordsByWorkspaceId.TryGetValue(workspaceId, out var record))
        {
            return UseCaseResult<WorkspaceRecord>.Success(
                record,
                "Workspace context is available.",
                traceId);
        }

        return UseCaseResult<WorkspaceRecord>.Failure(
            UseCaseStatus.NotFound,
            $"{useCaseName} could not resolve the workspace context.",
            traceId,
            [$"Workspace '{workspaceId}' does not exist."]);
    }

    private void ReloadPersistedState()
    {
        if (_stateStore is null)
        {
            return;
        }

        _recordsByWorkspaceId.Clear();
        _recordsByPrincipalKey.Clear();
        _recordsByAccountKey.Clear();

        foreach (var persistedRecord in _stateStore.Load().Workspaces)
        {
            var record = new WorkspaceRecord
            {
                Workspace = persistedRecord.Workspace
            };

            foreach (var character in persistedRecord.Characters)
            {
                record.Characters[character.Key] = character.Value;
            }

            foreach (var operatorEntry in persistedRecord.Operators)
            {
                record.Operators[operatorEntry.Key] = operatorEntry.Value;
            }

            foreach (var esiCharacter in persistedRecord.EsiCharacterIds)
            {
                record.EsiCharacterIds[esiCharacter.Key] = esiCharacter.Value;
            }

            foreach (var grant in persistedRecord.StructureGrants)
            {
                record.StructureGrants[grant.StructureId] = grant;
            }

            _recordsByWorkspaceId[record.Workspace.WorkspaceId] = record;
            ReindexRecord(record);
        }
    }

    private void PersistState()
    {
        _stateStore?.Save(new WorkspaceRuntimeState
        {
            Workspaces = _recordsByWorkspaceId.Values
                .OrderBy(record => record.Workspace.WorkspaceId, StringComparer.OrdinalIgnoreCase)
                .Select(record => new PersistedWorkspaceRecord
                {
                    Workspace = record.Workspace,
                    Characters = record.Characters
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    Operators = record.Operators
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    EsiCharacterIds = record.EsiCharacterIds
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    StructureGrants = record.StructureGrants.Values
                        .OrderBy(grant => grant.StructureId)
                        .ToArray()
                })
                .ToArray()
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
            ["Shared runtime state changed repeatedly while this request was executing. Retry the request."]);
    }

    private void ReindexRecord(WorkspaceRecord record)
    {
        var existingWorkspaceIds = _recordsByPrincipalKey
            .Where(pair => string.Equals(pair.Value.Workspace.WorkspaceId, record.Workspace.WorkspaceId, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in existingWorkspaceIds)
        {
            _recordsByPrincipalKey.Remove(key);
        }

        var existingAccountKeys = _recordsByAccountKey
            .Where(pair => string.Equals(pair.Value.Workspace.WorkspaceId, record.Workspace.WorkspaceId, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in existingAccountKeys)
        {
            _recordsByAccountKey.Remove(key);
        }

        _recordsByPrincipalKey[BuildWorkspacePrincipalKey(record.Workspace)] = record;
        _recordsByPrincipalKey[BuildLegacyPrincipalKey(
            record.Workspace.Account.Principal.Provider,
            record.Workspace.Account.Principal.SubjectCharacterId)] = record;
        _recordsByAccountKey[record.Workspace.Account.IsolationKey] = record;
    }

    private static string BuildWorkspacePrincipalKey(WorkspaceContext workspace)
    {
        if (!string.IsNullOrWhiteSpace(workspace.Account.SubjectId))
        {
            return BuildSubjectPrincipalKey(workspace.Account.SubjectId);
        }

        if (!string.IsNullOrWhiteSpace(workspace.Account.Principal.PlatformSubjectId))
        {
            return BuildSubjectPrincipalKey(workspace.Account.Principal.PlatformSubjectId);
        }

        return BuildLegacyPrincipalKey(
            workspace.Account.Principal.Provider,
            workspace.Account.Principal.SubjectCharacterId);
    }

    private sealed record WorkspaceRecord
    {
        public required WorkspaceContext Workspace { get; set; }

        public Dictionary<string, CharacterPoolEntry> Characters { get; } = [];

        public Dictionary<string, OperatorPoolEntry> Operators { get; } = [];

        public Dictionary<string, string> EsiCharacterIds { get; } = [];

        public Dictionary<long, WorkspaceStructureGrant> StructureGrants { get; } = [];
    }

    private sealed record WorkspaceIdentityContext
    {
        public required string UserId { get; init; }

        public required string SubjectId { get; init; }

        public required string PrincipalKey { get; init; }

        public string? ActiveActorId { get; init; }

        public string? OperatorDisplayName { get; init; }
    }

    private sealed class StructureAccessAccumulator(
        long structureId,
        string grantSource,
        DateTimeOffset lastVerifiedAtUtc,
        bool isStale)
    {
        public long StructureId { get; } = structureId;

        public string GrantSource { get; set; } = grantSource;

        public DateTimeOffset LastVerifiedAtUtc { get; set; } = lastVerifiedAtUtc;

        public bool IsStale { get; set; } = isStale;

        public HashSet<string> AuthorizedEsiCharacterIds { get; } = new(StringComparer.Ordinal);
    }
}
