using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EdenOS.Application.Market;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Authentication;

public sealed class EsiAuthenticatedCharacterSkillsRunner
{
    private const string RequiredScope = "esi-skills.read_skills.v1";

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        (HttpStatusCode)420
    ];

    private readonly IEsiCredentialService _esiCredentialService;
    private readonly ICharacterPoolService _characterPoolService;
    private readonly MetadataBootstrapCatalog _metadata;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient? _httpClient;

    public EsiAuthenticatedCharacterSkillsRunner(
        IEsiCredentialService esiCredentialService,
        ICharacterPoolService characterPoolService,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
        : this(
            esiCredentialService,
            characterPoolService,
            httpClient,
            timeProvider,
            metadata: null)
    {
    }

    internal EsiAuthenticatedCharacterSkillsRunner(
        IEsiCredentialService esiCredentialService,
        ICharacterPoolService characterPoolService,
        HttpClient? httpClient,
        TimeProvider? timeProvider,
        MetadataBootstrapCatalog? metadata)
    {
        _esiCredentialService = esiCredentialService;
        _characterPoolService = characterPoolService;
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metadata = metadata ?? MetadataBootstrapCatalog.LoadDefault();
    }

    public async Task<UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>> PullAsync(
        EsiAuthenticatedCharacterSkillsPullRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var traceId = $"esi.auth.pull_character_skills:{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>.Failure(
                UseCaseStatus.InvalidInput,
                "WorkspaceId is required.",
                traceId,
                ["workspace_id must not be empty."]);
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectoryPath))
        {
            return UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>.Failure(
                UseCaseStatus.InvalidInput,
                "OutputDirectoryPath is required.",
                traceId,
                ["output_directory_path must not be empty."]);
        }

        var characterPoolResult = _characterPoolService.List(new ListCharacterPoolRequest
        {
            WorkspaceId = request.WorkspaceId
        });
        if (!characterPoolResult.IsSuccess)
        {
            return UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>.Failure(
                characterPoolResult.Status,
                characterPoolResult.Summary,
                traceId,
                characterPoolResult.Errors);
        }

        var requestedIds = request.EsiCharacterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var characters = characterPoolResult.Data!.Characters
            .Where(character =>
                character.SourceKind == CharacterSourceKind.EsiBound
                && !string.IsNullOrWhiteSpace(character.EsiCharacterId)
                && (requestedIds.Length == 0 || requestedIds.Contains(character.EsiCharacterId, StringComparer.Ordinal)))
            .OrderByDescending(character => character.IsPrimaryAccountCharacter)
            .ThenBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (characters.Length == 0)
        {
            return UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>.Failure(
                UseCaseStatus.NotFound,
                "No attached ESI character matched the request.",
                traceId,
                requestedIds.Length == 0
                    ? ["The workspace does not contain any attached ESI character."]
                    : ["None of the requested ESI character ids is attached to this workspace."]);
        }

        var startedAtUtc = _timeProvider.GetUtcNow();
        var outputDirectoryPath = Path.GetFullPath(request.OutputDirectoryPath);
        var latestDirectoryPath = Path.Combine(outputDirectoryPath, "latest");
        var historyDirectoryPath = Path.Combine(outputDirectoryPath, "history");
        Directory.CreateDirectory(latestDirectoryPath);
        Directory.CreateDirectory(historyDirectoryPath);

        var notes = new List<string>();
        var errors = new List<string>();
        var views = new List<EsiAuthenticatedCharacterSkillsCharacterView>();
        var httpClient = CreateHttpClient(request);

        try
        {
            foreach (var character in characters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await PullCharacterAsync(httpClient, request, character, latestDirectoryPath, historyDirectoryPath, cancellationToken);
                views.Add(result.View);
                if (result.View.Errors.Count > 0)
                {
                    errors.AddRange(result.View.Errors);
                }

                if (result.View.Notes.Count > 0)
                {
                    notes.AddRange(result.View.Notes);
                }
            }
        }
        finally
        {
            if (_httpClient is null)
            {
                httpClient.Dispose();
            }
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var status = errors.Count == 0 ? "completed" : views.Any(view => view.SkillCount > 0) ? "partial_failure" : "failed";
        var view = new EsiAuthenticatedCharacterSkillsPullView
        {
            WorkspaceId = request.WorkspaceId,
            RequestedBy = request.RequestedBy,
            TriggerKind = request.TriggerKind,
            OutputDirectoryPath = outputDirectoryPath,
            Status = status,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RequestedCharacterCount = characters.Length,
            PulledCharacterCount = views.Count(item => string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase)),
            FailedCharacterCount = views.Count(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase)),
            Characters = views,
            Errors = errors,
            Notes = notes
        };

        return status == "failed"
            ? new UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>
            {
                Status = UseCaseStatus.DependencyUnavailable,
                Summary = "Character skill pull failed for all requested characters.",
                Data = view,
                TraceId = traceId,
                Errors = errors.Count > 0 ? errors : ["Character skill pull produced no successful result."],
                Warnings = notes
            }
            : UseCaseResult<EsiAuthenticatedCharacterSkillsPullView>.Success(
                view,
                errors.Count == 0
                    ? $"Pulled character skills for {view.PulledCharacterCount} character(s)."
                    : $"Pulled character skills with {view.FailedCharacterCount} character failure(s).",
                traceId,
                warnings: notes.Concat(errors).ToArray());
    }

    private HttpClient CreateHttpClient(EsiAuthenticatedCharacterSkillsPullRequest request)
    {
        if (_httpClient is not null)
        {
            return _httpClient;
        }

        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new Uri(request.BaseUrl, UriKind.Absolute)
        };
    }

    private async Task<CharacterPullResult> PullCharacterAsync(
        HttpClient httpClient,
        EsiAuthenticatedCharacterSkillsPullRequest request,
        CharacterPoolEntry character,
        string latestDirectoryPath,
        string historyDirectoryPath,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var errors = new List<string>();
        var esiCharacterId = character.EsiCharacterId!;

        var credentialStatus = await _esiCredentialService.GetStatusAsync(
            new GetEsiCredentialStatusRequest
            {
                WorkspaceId = request.WorkspaceId,
                EsiCharacterId = esiCharacterId
            },
            cancellationToken);
        if (!credentialStatus.IsSuccess)
        {
            return new CharacterPullResult(BuildFailedView(character, errors: credentialStatus.Errors, notes: notes), null);
        }

        if (!credentialStatus.Data!.Scopes.Contains(RequiredScope, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Bound credential for '{character.DisplayName}' is missing required scope '{RequiredScope}'.");
            notes.Add("Re-run EVE SSO with the skills scope and bind the refreshed token again.");
            return new CharacterPullResult(BuildFailedView(character, errors, notes), null);
        }

        var tokenResult = await _esiCredentialService.ResolveAccessTokenAsync(
            new ResolveEsiAccessTokenRequest
            {
                WorkspaceId = request.WorkspaceId,
                EsiCharacterId = esiCharacterId,
                MinimumValiditySeconds = request.MinimumTokenValiditySeconds,
                AllowRefresh = true
            },
            cancellationToken);
        if (!tokenResult.IsSuccess)
        {
            return new CharacterPullResult(BuildFailedView(character, tokenResult.Errors, notes), null);
        }

        var observedAtUtc = _timeProvider.GetUtcNow();
        var response = await SendGetAsync(
            httpClient,
            request,
            BuildRelativeUri($"characters/{esiCharacterId}/skills/", request.Datasource),
            tokenResult.Data!.AccessToken,
            cancellationToken);
        if (!response.IsSuccess)
        {
            errors.Add(response.ErrorSummary ?? "Unknown ESI error while fetching character skills.");
            return new CharacterPullResult(BuildFailedView(character, errors, notes), null);
        }

        var parsed = ParseSkillPayload(response.Body!, observedAtUtc);
        var normalizedSkillLevels = parsed.Skills
            .Where(skill => !string.IsNullOrWhiteSpace(skill.SkillKey))
            .Select(skill => new CharacterIndustrySkillLevel
            {
                SkillKey = skill.SkillKey,
                Level = skill.TrainedLevel
            })
            .OrderBy(skill => skill.SkillKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (request.UpdateCharacterPool)
        {
            var updatedProfile = EsiCharacterIndustryCapabilityProjector.Project(
                character.CapabilityProfile,
                normalizedSkillLevels,
                observedAtUtc);

            var updateResult = _characterPoolService.UpdateEsiCharacterCapabilityProfile(new UpdateEsiCharacterCapabilityProfileRequest
            {
                WorkspaceId = request.WorkspaceId,
                EsiCharacterId = esiCharacterId,
                CapabilityProfile = updatedProfile,
                Notes = character.Notes
            });
            if (!updateResult.IsSuccess)
            {
                errors.AddRange(updateResult.Errors);
                return new CharacterPullResult(BuildFailedView(character, errors, notes), null);
            }
        }

        var payloadPaths = WritePayloadArtifacts(
            character,
            latestDirectoryPath,
            historyDirectoryPath,
            observedAtUtc,
            parsed);
        if (request.UpdateCharacterPool)
        {
            notes.Add("Updated the attached character capability profile with refreshed ESI skill levels.");
        }

        var successView = new EsiAuthenticatedCharacterSkillsCharacterView
        {
            EsiCharacterId = esiCharacterId,
            CharacterName = character.DisplayName,
            Status = "completed",
            PayloadPath = payloadPaths.LatestPath,
            ArchivePath = payloadPaths.ArchivePath,
            SelectedScope = RequiredScope,
            SkillCount = parsed.Skills.Count,
            TotalSkillPoints = parsed.TotalSkillPoints,
            UnallocatedSkillPoints = parsed.UnallocatedSkillPoints,
            ObservedAtUtc = observedAtUtc,
            Skills = parsed.Skills,
            Errors = Array.Empty<string>(),
            Notes = notes
        };

        return new CharacterPullResult(successView, parsed);
    }

    private EsiAuthenticatedCharacterSkillsCharacterView BuildFailedView(
        CharacterPoolEntry character,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> notes)
    {
        return new EsiAuthenticatedCharacterSkillsCharacterView
        {
            EsiCharacterId = character.EsiCharacterId!,
            CharacterName = character.DisplayName,
            Status = "failed",
            Errors = errors,
            Notes = notes
        };
    }

    private ParsedSkillPayload ParseSkillPayload(string body, DateTimeOffset observedAtUtc)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var skills = root.GetProperty("skills")
            .EnumerateArray()
            .Select(entry =>
            {
                var skillTypeId = entry.GetProperty("skill_id").GetInt64();
                var skillName = _metadata.TryGetType(skillTypeId, out var type)
                    ? type.Name
                    : $"type_{skillTypeId.ToString(CultureInfo.InvariantCulture)}";
                var trainedLevel = entry.TryGetProperty("trained_skill_level", out var trainedElement)
                    ? trainedElement.GetInt32()
                    : entry.TryGetProperty("active_skill_level", out var activeFallbackElement)
                        ? activeFallbackElement.GetInt32()
                        : 0;
                var activeLevel = entry.TryGetProperty("active_skill_level", out var activeElement)
                    ? activeElement.GetInt32()
                    : trainedLevel;
                var skillPointsInSkill = entry.TryGetProperty("skillpoints_in_skill", out var spElement)
                    ? spElement.GetInt64()
                    : 0L;

                return new EsiAuthenticatedCharacterSkillView
                {
                    SkillTypeId = skillTypeId,
                    SkillName = skillName,
                    SkillKey = NormalizeSkillKey(skillName),
                    TrainedLevel = trainedLevel,
                    ActiveLevel = activeLevel,
                    SkillPointsInSkill = skillPointsInSkill
                };
            })
            .OrderBy(skill => skill.SkillName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ParsedSkillPayload(
            observedAtUtc,
            root.TryGetProperty("total_sp", out var totalSpElement) ? totalSpElement.GetInt64() : 0L,
            root.TryGetProperty("unallocated_sp", out var unallocatedElement) ? unallocatedElement.GetInt64() : 0L,
            skills);
    }

    private PayloadPaths WritePayloadArtifacts(
        CharacterPoolEntry character,
        string latestDirectoryPath,
        string historyDirectoryPath,
        DateTimeOffset observedAtUtc,
        ParsedSkillPayload payload)
    {
        var latestPath = Path.Combine(latestDirectoryPath, $"character-{character.EsiCharacterId}.skills.json");
        var archivePath = Path.Combine(historyDirectoryPath, $"{observedAtUtc:yyyyMMddTHHmmssZ}-character-{character.EsiCharacterId}.skills.json");

        WritePayloadJson(latestPath, character, observedAtUtc, payload);
        WritePayloadJson(archivePath, character, observedAtUtc, payload);
        return new PayloadPaths(latestPath, archivePath);
    }

    private void WritePayloadJson(
        string path,
        CharacterPoolEntry character,
        DateTimeOffset observedAtUtc,
        ParsedSkillPayload payload)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("source", $"esi-authenticated-character-skills:{character.EsiCharacterId}");
                writer.WriteStartObject("character");
                writer.WriteString("esi_character_id", character.EsiCharacterId);
                writer.WriteString("character_name", character.DisplayName);
                writer.WriteString("observed_at_utc", FormatInstant(observedAtUtc));
                writer.WriteNumber("total_sp", payload.TotalSkillPoints);
                writer.WriteNumber("unallocated_sp", payload.UnallocatedSkillPoints);
                writer.WriteNumber("skill_count", payload.Skills.Count);
                writer.WriteEndObject();
                writer.WritePropertyName("skills");
                writer.WriteStartArray();
                foreach (var skill in payload.Skills)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("skill_type_id", skill.SkillTypeId);
                    writer.WriteString("skill_name", skill.SkillName);
                    writer.WriteString("skill_key", skill.SkillKey);
                    writer.WriteNumber("trained_level", skill.TrainedLevel);
                    writer.WriteNumber("active_level", skill.ActiveLevel);
                    writer.WriteNumber("skill_points_in_skill", skill.SkillPointsInSkill);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task<HttpFetchResult> SendGetAsync(
        HttpClient httpClient,
        EsiAuthenticatedCharacterSkillsPullRequest request,
        string relativeUri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var requestCount = 0;
        var retryCount = 0;
        for (var attempt = 0; attempt <= request.MaxRetriesPerRequest; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.RequestTimeoutSeconds));

            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(httpClient.BaseAddress!, relativeUri));
            message.Headers.UserAgent.ParseAdd(request.UserAgent);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            if (!string.IsNullOrWhiteSpace(request.CompatibilityDate))
            {
                message.Headers.TryAddWithoutValidation("x-compatibility-date", request.CompatibilityDate);
            }

            HttpResponseMessage? response = null;
            try
            {
                requestCount++;
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return HttpFetchResult.Success(body, requestCount, retryCount);
                }

                if (RetryableStatusCodes.Contains(response.StatusCode) && attempt < request.MaxRetriesPerRequest)
                {
                    retryCount++;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(request.RetryBaseDelayMilliseconds, 100) * retryCount), cancellationToken);
                    continue;
                }

                return HttpFetchResult.Failure($"{(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(body)}", requestCount, retryCount);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < request.MaxRetriesPerRequest)
            {
                retryCount++;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(request.RetryBaseDelayMilliseconds, 100) * retryCount), cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }

        return HttpFetchResult.Failure("Request failed after exhausting retries.", requestCount, retryCount);
    }

    private static string BuildRelativeUri(string path, string datasource)
    {
        return $"{path}?datasource={Uri.EscapeDataString(datasource)}";
    }

    private static string NormalizeSkillKey(string skillName)
    {
        var normalized = skillName.Trim().ToLowerInvariant();
        var buffer = new List<char>(normalized.Length);
        var lastWasSeparator = false;
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Add(character);
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
            {
                continue;
            }

            buffer.Add('_');
            lastWasSeparator = true;
        }

        return new string(buffer.ToArray()).Trim('_');
    }

    private static string FormatInstant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string TrimBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response body";
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 240 ? trimmed : trimmed[..240];
    }

    private sealed record ParsedSkillPayload(
        DateTimeOffset ObservedAtUtc,
        long TotalSkillPoints,
        long UnallocatedSkillPoints,
        IReadOnlyList<EsiAuthenticatedCharacterSkillView> Skills);

    private sealed record PayloadPaths(
        string LatestPath,
        string ArchivePath);

    private sealed record CharacterPullResult(
        EsiAuthenticatedCharacterSkillsCharacterView View,
        ParsedSkillPayload? Payload);

    private sealed record HttpFetchResult(
        string? Body,
        int RequestCount,
        int RetryCount,
        string? ErrorSummary)
    {
        public bool IsSuccess => ErrorSummary is null;

        public static HttpFetchResult Success(string body, int requestCount, int retryCount)
            => new(body, requestCount, retryCount, null);

        public static HttpFetchResult Failure(string errorSummary, int requestCount, int retryCount)
            => new(null, requestCount, retryCount, errorSummary);
    }
}
