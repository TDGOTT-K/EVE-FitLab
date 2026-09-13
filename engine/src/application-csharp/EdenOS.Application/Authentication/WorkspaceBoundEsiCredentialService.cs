using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EdenOS.Application.Configuration;
using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;
using Microsoft.Extensions.Options;

namespace EdenOS.Application.Authentication;

public sealed class WorkspaceBoundEsiCredentialService : IEsiCredentialService
{
    private const int PersistenceRetryCount = 3;
    private const string DefaultTokenEndpoint = "https://login.eveonline.com/v2/oauth/token";

    private readonly IWorkspaceService _workspaceService;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<RuntimeOptions> _runtimeOptions;
    private readonly LocalJsonStateStore<EsiCredentialRuntimeState>? _stateStore;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly Dictionary<string, PersistedEsiCredentialBindingRecord> _recordsByKey = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceBoundEsiCredentialService(
        IWorkspaceService workspaceService,
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<RuntimeOptions> runtimeOptions)
        : this(workspaceService, timeProvider, httpClientFactory, runtimeOptions, stateStore: null)
    {
    }

    internal WorkspaceBoundEsiCredentialService(
        IWorkspaceService workspaceService,
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<RuntimeOptions> runtimeOptions,
        LocalJsonStateStore<EsiCredentialRuntimeState>? stateStore)
    {
        _workspaceService = workspaceService;
        _timeProvider = timeProvider;
        _httpClientFactory = httpClientFactory;
        _runtimeOptions = runtimeOptions;
        _stateStore = stateStore;
        ReloadPersistedState();
    }

    public Task<UseCaseResult<EsiCredentialStatusView>> BindAsync(
        BindEsiCredentialRequest request,
        CancellationToken cancellationToken)
    {
        return ExecutePersistedMutationAsync(
            "esi_auth.bind",
            cancellationToken,
            () => BindCore(request));
    }

    public Task<UseCaseResult<EsiCredentialStatusView>> GetStatusAsync(
        GetEsiCredentialStatusRequest request,
        CancellationToken cancellationToken)
    {
        return ExecutePersistedMutationAsync(
            "esi_auth.get_status",
            cancellationToken,
            () => Task.FromResult(GetStatusCore(request)));
    }

    public Task<UseCaseResult<EsiAccessTokenEnvelope>> ResolveAccessTokenAsync(
        ResolveEsiAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        return ExecutePersistedMutationAsync(
            "esi_auth.resolve_access_token",
            cancellationToken,
            () => ResolveAccessTokenCoreAsync(request, cancellationToken));
    }

    private Task<UseCaseResult<EsiCredentialStatusView>> BindCore(BindEsiCredentialRequest request)
    {
        var traceId = CreateTraceId("esi_auth.bind");
        var bindingScopeResult = ResolveBindingScope(request.WorkspaceId, request.EsiCharacterId, traceId, "esi_auth.bind");
        if (!bindingScopeResult.IsSuccess)
        {
            return Task.FromResult(FailureFrom<EsiCredentialBindingScope, EsiCredentialStatusView>(bindingScopeResult, traceId));
        }

        if (string.IsNullOrWhiteSpace(request.CharacterName)
            || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Task.FromResult(UseCaseResult<EsiCredentialStatusView>.Failure(
                UseCaseStatus.InvalidInput,
                "ESI credential binding requires character name and access token.",
                traceId,
                ["CharacterName and AccessToken cannot be empty."]));
        }

        if (request.AccessTokenExpiresAtUtc == default)
        {
            return Task.FromResult(UseCaseResult<EsiCredentialStatusView>.Failure(
                UseCaseStatus.InvalidInput,
                "ESI credential binding requires an access token expiration time.",
                traceId,
                ["AccessTokenExpiresAtUtc must be provided."]));
        }

        var bindingScope = bindingScopeResult.Data!;
        var now = _timeProvider.GetUtcNow();
        var existing = FindExistingRecord(bindingScope);
        var normalizedScopes = NormalizeScopes(request.Scopes);
        var tokenEndpoint = NormalizeOptionalText(request.TokenEndpoint)
            ?? existing?.TokenEndpoint
            ?? _runtimeOptions.Value.EsiSso.TokenEndpoint
            ?? DefaultTokenEndpoint;

        var persisted = new PersistedEsiCredentialBindingRecord
        {
            WorkspaceId = bindingScope.WorkspaceId,
            UserId = bindingScope.UserId ?? existing?.UserId,
            SubjectId = bindingScope.SubjectId ?? existing?.SubjectId,
            EsiCharacterId = bindingScope.EsiCharacterId,
            CharacterName = request.CharacterName.Trim(),
            AccessToken = request.AccessToken.Trim(),
            RefreshToken = NormalizeOptionalText(request.RefreshToken) ?? existing?.RefreshToken,
            AccessTokenExpiresAtUtc = request.AccessTokenExpiresAtUtc,
            Scopes = normalizedScopes.Count == 0 ? existing?.Scopes ?? Array.Empty<string>() : normalizedScopes,
            OwnerHash = NormalizeOptionalText(request.OwnerHash) ?? existing?.OwnerHash,
            OauthClientId = NormalizeOptionalText(request.OauthClientId) ?? existing?.OauthClientId,
            TokenEndpoint = tokenEndpoint,
            TokenType = NormalizeOptionalText(request.TokenType) ?? existing?.TokenType ?? "Bearer",
            CredentialSource = request.CredentialSource,
            BoundAtUtc = existing?.BoundAtUtc ?? now,
            UpdatedAtUtc = now,
            LastRefreshAtUtc = existing?.LastRefreshAtUtc,
            LastRefreshAttemptAtUtc = existing?.LastRefreshAttemptAtUtc,
            LastRefreshError = existing?.LastRefreshError,
            Notes = NormalizeOptionalText(request.Notes) ?? existing?.Notes
        };

        StoreRecord(bindingScope, persisted);
        PersistState();

        return Task.FromResult(UseCaseResult<EsiCredentialStatusView>.Success(
            BuildStatusView(persisted, now, _runtimeOptions.Value.EsiSso.RefreshSkewSeconds),
            existing is null
                ? "Bound ESI credentials to the authenticated character."
                : "Updated bound ESI credentials for the authenticated character.",
            traceId));
    }

    private UseCaseResult<EsiCredentialStatusView> GetStatusCore(GetEsiCredentialStatusRequest request)
    {
        var traceId = CreateTraceId("esi_auth.get_status");
        var bindingScopeResult = ResolveBindingScope(request.WorkspaceId, request.EsiCharacterId, traceId, "esi_auth.get_status");
        if (!bindingScopeResult.IsSuccess)
        {
            return FailureFrom<EsiCredentialBindingScope, EsiCredentialStatusView>(bindingScopeResult, traceId);
        }

        var recordResult = GetRecord(bindingScopeResult.Data!, traceId, "esi_auth.get_status");
        if (!recordResult.IsSuccess)
        {
            return FailureFrom<PersistedEsiCredentialBindingRecord, EsiCredentialStatusView>(recordResult, traceId);
        }

        return UseCaseResult<EsiCredentialStatusView>.Success(
            BuildStatusView(recordResult.Data!, _timeProvider.GetUtcNow(), request.ExpiringSoonWindowSeconds),
            "Loaded bound ESI credential status.",
            traceId);
    }

    private async Task<UseCaseResult<EsiAccessTokenEnvelope>> ResolveAccessTokenCoreAsync(
        ResolveEsiAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = CreateTraceId("esi_auth.resolve_access_token");
        var bindingScopeResult = ResolveBindingScope(request.WorkspaceId, request.EsiCharacterId, traceId, "esi_auth.resolve_access_token");
        if (!bindingScopeResult.IsSuccess)
        {
            return FailureFrom<EsiCredentialBindingScope, EsiAccessTokenEnvelope>(bindingScopeResult, traceId);
        }

        var bindingScope = bindingScopeResult.Data!;
        var recordResult = GetRecord(bindingScope, traceId, "esi_auth.resolve_access_token");
        if (!recordResult.IsSuccess)
        {
            return FailureFrom<PersistedEsiCredentialBindingRecord, EsiAccessTokenEnvelope>(recordResult, traceId);
        }

        var record = recordResult.Data!;
        var now = _timeProvider.GetUtcNow();
        if (!NeedsRefresh(record, now, request.MinimumValiditySeconds))
        {
            return UseCaseResult<EsiAccessTokenEnvelope>.Success(
                BuildAccessTokenEnvelope(record, wasRefreshed: false, now),
                "Resolved access token from bound ESI credentials.",
                traceId);
        }

        if (!request.AllowRefresh)
        {
            return UseCaseResult<EsiAccessTokenEnvelope>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "The bound ESI access token is too close to expiry and refresh was disabled.",
                traceId,
                ["AllowRefresh must be true to refresh an expiring or expired token."]);
        }

        if (string.IsNullOrWhiteSpace(record.RefreshToken))
        {
            var unavailable = record with
            {
                WorkspaceId = bindingScope.WorkspaceId,
                UserId = bindingScope.UserId ?? record.UserId,
                SubjectId = bindingScope.SubjectId ?? record.SubjectId,
                LastRefreshAttemptAtUtc = now,
                LastRefreshError = "No refresh token is stored for this authenticated ESI credential.",
                UpdatedAtUtc = now
            };
            StoreRecord(bindingScope, unavailable);
            PersistState();

            return UseCaseResult<EsiAccessTokenEnvelope>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "The bound ESI credential cannot be refreshed because no refresh token is available.",
                traceId,
                ["RefreshToken is missing for this authenticated ESI credential."]);
        }

        var refreshResult = await RefreshTokenAsync(record, cancellationToken);
        if (!refreshResult.IsSuccess)
        {
            var failed = record with
            {
                WorkspaceId = bindingScope.WorkspaceId,
                UserId = bindingScope.UserId ?? record.UserId,
                SubjectId = bindingScope.SubjectId ?? record.SubjectId,
                LastRefreshAttemptAtUtc = now,
                LastRefreshError = refreshResult.ErrorSummary,
                UpdatedAtUtc = now
            };
            StoreRecord(bindingScope, failed);
            PersistState();

            return UseCaseResult<EsiAccessTokenEnvelope>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Refreshing the bound ESI credential failed.",
                traceId,
                [refreshResult.ErrorSummary ?? "ESI SSO refresh failed for an unknown reason."]);
        }

        var refreshedRecord = record with
        {
            WorkspaceId = bindingScope.WorkspaceId,
            UserId = bindingScope.UserId ?? record.UserId,
            SubjectId = bindingScope.SubjectId ?? record.SubjectId,
            AccessToken = refreshResult.AccessToken!,
            RefreshToken = refreshResult.RefreshToken ?? record.RefreshToken,
            AccessTokenExpiresAtUtc = refreshResult.AccessTokenExpiresAtUtc!.Value,
            Scopes = refreshResult.Scopes.Count == 0 ? record.Scopes : refreshResult.Scopes,
            TokenType = refreshResult.TokenType ?? record.TokenType,
            CredentialSource = record.CredentialSource == EsiCredentialSourceKind.AuthorizationCodePkce
                ? EsiCredentialSourceKind.AuthorizationCodePkce : EsiCredentialSourceKind.InternalRefresh,
            UpdatedAtUtc = now,
            LastRefreshAtUtc = now,
            LastRefreshAttemptAtUtc = now,
            LastRefreshError = null
        };

        StoreRecord(bindingScope, refreshedRecord);
        PersistState();

        return UseCaseResult<EsiAccessTokenEnvelope>.Success(
            BuildAccessTokenEnvelope(refreshedRecord, wasRefreshed: true, now),
            "Refreshed and resolved access token from bound ESI credentials.",
            traceId);
    }

    private async Task<RefreshTokenResult> RefreshTokenAsync(
        PersistedEsiCredentialBindingRecord record,
        CancellationToken cancellationToken)
    {
        var options = _runtimeOptions.Value.EsiSso;
        var isPkce = record.CredentialSource == EsiCredentialSourceKind.AuthorizationCodePkce;
        var tokenEndpoint = (isPkce ? NormalizeOptionalText(record.TokenEndpoint) : null)
            ?? NormalizeOptionalText(options.TokenEndpoint)
            ?? NormalizeOptionalText(record.TokenEndpoint)
            ?? DefaultTokenEndpoint;
        var oauthClientId = (isPkce ? NormalizeOptionalText(record.OauthClientId) : null)
            ?? NormalizeOptionalText(options.ClientId)
            ?? NormalizeOptionalText(record.OauthClientId);
        var clientSecret = isPkce ? null : NormalizeOptionalText(options.ClientSecret);

        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return RefreshTokenResult.Failure("No ESI SSO token endpoint is configured for refresh.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret) && string.IsNullOrWhiteSpace(oauthClientId))
        {
            return RefreshTokenResult.Failure("Refreshing this ESI credential requires an OAuth client id.");
        }

        var formValues = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = record.RefreshToken!
        };

        var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            var authBytes = Encoding.ASCII.GetBytes($"{oauthClientId}:{clientSecret}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(authBytes));
        }
        else
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>(formValues)
            {
                ["client_id"] = oauthClientId!
            });
        }

        using var client = _httpClientFactory.CreateClient("esi-sso");
        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return RefreshTokenResult.Failure(
                $"ESI SSO refresh returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var accessTokenElement)
                || string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
            {
                return RefreshTokenResult.Failure("ESI SSO refresh response did not include access_token.");
            }

            if (!root.TryGetProperty("expires_in", out var expiresInElement)
                || !expiresInElement.TryGetInt32(out var expiresInSeconds))
            {
                return RefreshTokenResult.Failure("ESI SSO refresh response did not include expires_in.");
            }

            var now = _timeProvider.GetUtcNow();
            return RefreshTokenResult.Success(
                accessTokenElement.GetString()!,
                root.TryGetProperty("refresh_token", out var refreshTokenElement) ? refreshTokenElement.GetString() : null,
                now.AddSeconds(expiresInSeconds),
                ParseScopeList(root.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : null),
                root.TryGetProperty("token_type", out var tokenTypeElement) ? tokenTypeElement.GetString() : null);
        }
        catch (JsonException exception)
        {
            return RefreshTokenResult.Failure($"ESI SSO refresh returned invalid JSON: {exception.Message}");
        }
    }

    private static IReadOnlyList<string> ParseScopeList(string? scopeValue)
    {
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            return Array.Empty<string>();
        }

        return NormalizeScopes(scopeValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private UseCaseResult<EsiCredentialBindingScope> ResolveBindingScope(
        string workspaceId,
        string esiCharacterId,
        string traceId,
        string useCaseName)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(esiCharacterId))
        {
            return UseCaseResult<EsiCredentialBindingScope>.Failure(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires workspace id and ESI character id.",
                traceId,
                ["WorkspaceId and EsiCharacterId cannot be empty."]);
        }

        var summary = _workspaceService.GetSummary(new GetWorkspaceSummaryRequest
        {
            WorkspaceId = workspaceId
        });
        if (!summary.IsSuccess || summary.Data is null)
        {
            return UseCaseResult<EsiCredentialBindingScope>.Failure(
                summary.Status,
                summary.Summary,
                traceId,
                summary.Errors);
        }

        if (!summary.Data.Characters.Any(character =>
                string.Equals(character.EsiCharacterId, esiCharacterId, StringComparison.OrdinalIgnoreCase)))
        {
            return UseCaseResult<EsiCredentialBindingScope>.Failure(
                UseCaseStatus.NotFound,
                $"{useCaseName} requires the ESI character to already be attached to the workspace.",
                traceId,
                [$"Workspace '{workspaceId}' does not have attached ESI character '{esiCharacterId}'."]);
        }

        var normalizedWorkspaceId = summary.Data.WorkspaceId.Trim();
        var normalizedCharacterId = esiCharacterId.Trim();
        var userId = NormalizeOptionalText(summary.Data.Account.UserId);
        var subjectId = NormalizeOptionalText(summary.Data.Account.SubjectId);
        var primaryKey = BuildBindingKey(normalizedWorkspaceId, userId, normalizedCharacterId);
        var legacyKey = userId is null
            ? null
            : BuildLegacyBindingKey(normalizedWorkspaceId, normalizedCharacterId);

        return UseCaseResult<EsiCredentialBindingScope>.Success(
            new EsiCredentialBindingScope
            {
                WorkspaceId = normalizedWorkspaceId,
                UserId = userId,
                SubjectId = subjectId,
                EsiCharacterId = normalizedCharacterId,
                PrimaryKey = primaryKey,
                LegacyKey = legacyKey
            },
            "ESI credential binding scope is valid.",
            traceId);
    }

    private UseCaseResult<PersistedEsiCredentialBindingRecord> GetRecord(
        EsiCredentialBindingScope bindingScope,
        string traceId,
        string useCaseName)
    {
        if (_recordsByKey.TryGetValue(bindingScope.PrimaryKey, out var record))
        {
            var aligned = AlignRecordToScope(record, bindingScope);
            if (aligned != record || HasLegacyAlias(bindingScope))
            {
                StoreRecord(bindingScope, aligned);
                PersistState();
            }

            return UseCaseResult<PersistedEsiCredentialBindingRecord>.Success(
                aligned,
                "Resolved bound ESI credential.",
                traceId);
        }

        if (!string.IsNullOrWhiteSpace(bindingScope.LegacyKey)
            && _recordsByKey.TryGetValue(bindingScope.LegacyKey, out record))
        {
            var migrated = AlignRecordToScope(record, bindingScope);
            StoreRecord(bindingScope, migrated);
            PersistState();

            return UseCaseResult<PersistedEsiCredentialBindingRecord>.Success(
                migrated,
                "Resolved bound ESI credential from legacy workspace scope.",
                traceId);
        }

        return UseCaseResult<PersistedEsiCredentialBindingRecord>.Failure(
            UseCaseStatus.NotFound,
            $"{useCaseName} could not resolve a bound ESI credential.",
            traceId,
            [$"Workspace '{bindingScope.WorkspaceId}' does not have ESI credentials bound for character '{bindingScope.EsiCharacterId}'."]);
    }

    private static EsiCredentialStatusView BuildStatusView(
        PersistedEsiCredentialBindingRecord record,
        DateTimeOffset now,
        int expiringSoonWindowSeconds)
    {
        return new EsiCredentialStatusView
        {
            WorkspaceId = record.WorkspaceId,
            UserId = record.UserId,
            SubjectId = record.SubjectId,
            EsiCharacterId = record.EsiCharacterId,
            CharacterName = record.CharacterName,
            AccessTokenExpiresAtUtc = record.AccessTokenExpiresAtUtc,
            AccessTokenState = DetermineState(record, now, expiringSoonWindowSeconds),
            HasRefreshToken = !string.IsNullOrWhiteSpace(record.RefreshToken),
            Scopes = record.Scopes,
            OwnerHash = record.OwnerHash,
            OauthClientId = record.OauthClientId,
            TokenEndpoint = record.TokenEndpoint,
            TokenType = record.TokenType,
            CredentialSource = record.CredentialSource,
            BoundAtUtc = record.BoundAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            LastRefreshAtUtc = record.LastRefreshAtUtc,
            LastRefreshAttemptAtUtc = record.LastRefreshAttemptAtUtc,
            LastRefreshError = record.LastRefreshError,
            Notes = record.Notes
        };
    }

    private static EsiAccessTokenEnvelope BuildAccessTokenEnvelope(
        PersistedEsiCredentialBindingRecord record,
        bool wasRefreshed,
        DateTimeOffset resolvedAtUtc)
    {
        return new EsiAccessTokenEnvelope
        {
            WorkspaceId = record.WorkspaceId,
            UserId = record.UserId,
            SubjectId = record.SubjectId,
            EsiCharacterId = record.EsiCharacterId,
            CharacterName = record.CharacterName,
            AccessToken = record.AccessToken,
            AccessTokenExpiresAtUtc = record.AccessTokenExpiresAtUtc,
            Scopes = record.Scopes,
            OwnerHash = record.OwnerHash,
            TokenType = record.TokenType,
            WasRefreshed = wasRefreshed,
            ResolvedAtUtc = resolvedAtUtc
        };
    }

    private static EsiAccessTokenStateKind DetermineState(
        PersistedEsiCredentialBindingRecord record,
        DateTimeOffset now,
        int expiringSoonWindowSeconds)
    {
        if (record.AccessTokenExpiresAtUtc <= now)
        {
            if (string.IsNullOrWhiteSpace(record.RefreshToken))
            {
                return EsiAccessTokenStateKind.RefreshUnavailable;
            }

            return string.IsNullOrWhiteSpace(record.LastRefreshError)
                ? EsiAccessTokenStateKind.Expired
                : EsiAccessTokenStateKind.RefreshFailed;
        }

        if (record.AccessTokenExpiresAtUtc <= now.AddSeconds(Math.Max(expiringSoonWindowSeconds, 0)))
        {
            return EsiAccessTokenStateKind.ExpiringSoon;
        }

        return EsiAccessTokenStateKind.Fresh;
    }

    private static bool NeedsRefresh(
        PersistedEsiCredentialBindingRecord record,
        DateTimeOffset now,
        int minimumValiditySeconds)
    {
        return record.AccessTokenExpiresAtUtc <= now.AddSeconds(Math.Max(minimumValiditySeconds, 0));
    }

    private static string BuildBindingKey(string workspaceId, string? userId, string esiCharacterId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return BuildUserBindingKey(userId, esiCharacterId);
        }

        return BuildLegacyBindingKey(workspaceId, esiCharacterId);
    }

    private static string BuildUserBindingKey(string userId, string esiCharacterId)
    {
        return $"user:{userId.Trim()}::{esiCharacterId.Trim()}";
    }

    private static string BuildLegacyBindingKey(string workspaceId, string esiCharacterId)
    {
        return $"{workspaceId.Trim()}::{esiCharacterId.Trim()}";
    }

    private void ReloadPersistedState()
    {
        if (_stateStore is null)
        {
            return;
        }

        _recordsByKey.Clear();
        foreach (var record in _stateStore.Load().Bindings)
        {
            _recordsByKey[BuildBindingKey(record.WorkspaceId, record.UserId, record.EsiCharacterId)] = record;
        }
    }

    private void PersistState()
    {
        _stateStore?.Save(new EsiCredentialRuntimeState
        {
            Bindings = _recordsByKey.Values
                .OrderBy(record => record.WorkspaceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.EsiCharacterId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        });
    }

    private async Task<UseCaseResult<T>> ExecutePersistedMutationAsync<T>(
        string useCaseName,
        CancellationToken cancellationToken,
        Func<Task<UseCaseResult<T>>> operation)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
            {
                if (_stateStore is not null)
                {
                    ReloadPersistedState();
                }

                try
                {
                    return await operation();
                }
                catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
                {
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }

        return UseCaseResult<T>.Failure(
            UseCaseStatus.Conflict,
            $"{useCaseName} detected a concurrent runtime state update.",
            CreateTraceId(useCaseName),
            ["Shared ESI credential state changed repeatedly while this request was executing. Retry the request."]);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> NormalizeScopes(IEnumerable<string> scopes)
    {
        return scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UseCaseResult<TTarget> FailureFrom<TSource, TTarget>(
        UseCaseResult<TSource> source,
        string traceId)
    {
        return UseCaseResult<TTarget>.Failure(
            source.Status,
            source.Summary,
            traceId,
            source.Errors,
            source.Warnings);
    }

    private static string CreateTraceId(string useCaseName) => $"{useCaseName}:{Guid.NewGuid():N}";

    private PersistedEsiCredentialBindingRecord? FindExistingRecord(EsiCredentialBindingScope bindingScope)
    {
        if (_recordsByKey.TryGetValue(bindingScope.PrimaryKey, out var primaryRecord))
        {
            return primaryRecord;
        }

        if (!string.IsNullOrWhiteSpace(bindingScope.LegacyKey)
            && _recordsByKey.TryGetValue(bindingScope.LegacyKey, out var legacyRecord))
        {
            return legacyRecord;
        }

        return null;
    }

    private bool HasLegacyAlias(EsiCredentialBindingScope bindingScope)
    {
        return !string.IsNullOrWhiteSpace(bindingScope.LegacyKey)
            && _recordsByKey.ContainsKey(bindingScope.LegacyKey);
    }

    private void StoreRecord(
        EsiCredentialBindingScope bindingScope,
        PersistedEsiCredentialBindingRecord record)
    {
        if (!string.IsNullOrWhiteSpace(bindingScope.LegacyKey)
            && !string.Equals(bindingScope.LegacyKey, bindingScope.PrimaryKey, StringComparison.OrdinalIgnoreCase))
        {
            _recordsByKey.Remove(bindingScope.LegacyKey);
        }

        _recordsByKey[bindingScope.PrimaryKey] = record;
    }

    private static PersistedEsiCredentialBindingRecord AlignRecordToScope(
        PersistedEsiCredentialBindingRecord record,
        EsiCredentialBindingScope bindingScope)
    {
        return record with
        {
            WorkspaceId = bindingScope.WorkspaceId,
            UserId = bindingScope.UserId ?? record.UserId,
            SubjectId = bindingScope.SubjectId ?? record.SubjectId
        };
    }

    private sealed record RefreshTokenResult
    {
        public string? AccessToken { get; init; }

        public string? RefreshToken { get; init; }

        public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }

        public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

        public string? TokenType { get; init; }

        public string? ErrorSummary { get; init; }

        public bool IsSuccess => !string.IsNullOrWhiteSpace(AccessToken) && AccessTokenExpiresAtUtc.HasValue;

        public static RefreshTokenResult Success(
            string accessToken,
            string? refreshToken,
            DateTimeOffset accessTokenExpiresAtUtc,
            IReadOnlyList<string> scopes,
            string? tokenType)
        {
            return new RefreshTokenResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
                Scopes = scopes,
                TokenType = tokenType
            };
        }

        public static RefreshTokenResult Failure(string summary)
        {
            return new RefreshTokenResult
            {
                ErrorSummary = summary
            };
        }
    }

    private sealed record EsiCredentialBindingScope
    {
        public required string WorkspaceId { get; init; }

        public string? UserId { get; init; }

        public string? SubjectId { get; init; }

        public required string EsiCharacterId { get; init; }

        public required string PrimaryKey { get; init; }

        public string? LegacyKey { get; init; }
    }
}
