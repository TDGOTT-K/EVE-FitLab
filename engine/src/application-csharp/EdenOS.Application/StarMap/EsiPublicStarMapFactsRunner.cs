using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.StarMap;

public sealed class EsiPublicStarMapFactsRunner
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsFactory.CreateSnakeCase(
            writeIndented: true,
            propertyNameCaseInsensitive: true);

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

    private readonly HttpClient? _httpClient;
    private readonly TimeProvider _timeProvider;

    public EsiPublicStarMapFactsRunner(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UseCaseResult<EsiPublicStarMapFactsRefreshView>> RefreshAsync(
        EsiPublicStarMapFactsRefreshRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var traceId = $"starmap.esi.public_facts:{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(request.OutputDirectoryPath))
        {
            return UseCaseResult<EsiPublicStarMapFactsRefreshView>.Failure(
                UseCaseStatus.InvalidInput,
                "OutputDirectoryPath is required.",
                traceId,
                ["output_directory_path must not be empty."]);
        }

        var startedAtUtc = _timeProvider.GetUtcNow();
        var outputDirectoryPath = Path.GetFullPath(request.OutputDirectoryPath);
        Directory.CreateDirectory(outputDirectoryPath);

        var httpClient = CreateHttpClient(request);
        try
        {
            var partitions = new[]
            {
                await RefreshPartitionAsync(
                    httpClient,
                    request,
                    outputDirectoryPath,
                    new PartitionDefinition(
                        "system_jumps",
                        "system-jumps.json",
                        $"universe/system_jumps/?datasource={Uri.EscapeDataString(request.Datasource)}",
                        "esi.universe.system_jumps",
                        MapSystemJumpsItems),
                    cancellationToken),
                await RefreshPartitionAsync(
                    httpClient,
                    request,
                    outputDirectoryPath,
                    new PartitionDefinition(
                        "system_kills",
                        "system-kills.json",
                        $"universe/system_kills/?datasource={Uri.EscapeDataString(request.Datasource)}",
                        "esi.universe.system_kills",
                        MapSystemKillsItems),
                    cancellationToken),
                await RefreshPartitionAsync(
                    httpClient,
                    request,
                    outputDirectoryPath,
                    new PartitionDefinition(
                        "sovereignty_map",
                        "sovereignty-map.json",
                        $"sovereignty/map/?datasource={Uri.EscapeDataString(request.Datasource)}",
                        "esi.sovereignty.map",
                        MapSovereigntyMapItems),
                    cancellationToken)
            };

            var completedAtUtc = _timeProvider.GetUtcNow();
            var errors = partitions.SelectMany(partition => partition.Errors).Distinct(StringComparer.Ordinal).ToArray();
            var notes = partitions.SelectMany(partition => partition.Notes).Distinct(StringComparer.Ordinal).ToArray();
            var view = new EsiPublicStarMapFactsRefreshView
            {
                RequestedBy = request.RequestedBy,
                TriggerKind = request.TriggerKind,
                OutputDirectoryPath = outputDirectoryPath,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                Status = DetermineOverallStatus(partitions),
                Partitions = partitions,
                Errors = errors,
                Notes = notes
            };

            return errors.Length == 0
                ? UseCaseResult<EsiPublicStarMapFactsRefreshView>.Success(
                    view,
                    "Star map public fact refresh completed.",
                    traceId,
                    notes)
                : new UseCaseResult<EsiPublicStarMapFactsRefreshView>
                {
                    Status = UseCaseStatus.Error,
                    Summary = "Star map public fact refresh completed with partition failures.",
                    Data = view,
                    Errors = errors,
                    Warnings = notes,
                    TraceId = traceId
                };
        }
        finally
        {
            if (_httpClient is null)
            {
                httpClient.Dispose();
            }
        }
    }

    private HttpClient CreateHttpClient(EsiPublicStarMapFactsRefreshRequest request)
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

    private async Task<EsiPublicStarMapFactPartitionView> RefreshPartitionAsync(
        HttpClient httpClient,
        EsiPublicStarMapFactsRefreshRequest request,
        string outputDirectoryPath,
        PartitionDefinition partition,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _timeProvider.GetUtcNow();
        var outputPath = Path.Combine(outputDirectoryPath, partition.FileName);
        var priorSnapshot = ReadPriorSnapshot(outputPath);
        var requestCount = 0;
        var retryCount = 0;
        var notes = new List<string>();
        var errors = new List<string>();

        for (var attempt = 0; attempt <= Math.Max(0, request.MaxRetriesPerRequest); attempt++)
        {
            requestCount += 1;
            using var message = new HttpRequestMessage(HttpMethod.Get, partition.RelativeUri);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.UserAgent.ParseAdd(request.UserAgent);
            if (!string.IsNullOrWhiteSpace(request.CompatibilityDate))
            {
                message.Headers.TryAddWithoutValidation("x-compatibility-date", request.CompatibilityDate);
            }

            HttpResponseMessage? response = null;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.RequestTimeoutSeconds)));
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                if (response.StatusCode == HttpStatusCode.NotModified && priorSnapshot is not null)
                {
                    notes.Add($"partition={partition.PartitionKey}: ESI returned 304 not modified; preserved existing snapshot.");
                    return BuildPartitionView(
                        partition.PartitionKey,
                        outputPath,
                        "not_modified",
                        startedAtUtc,
                        _timeProvider.GetUtcNow(),
                        requestCount,
                        retryCount,
                        priorSnapshot.Items.Count,
                        priorSnapshot.ObservedAtUtc,
                        ReadETag(response),
                        ReadExpiresAtUtc(response),
                        Array.Empty<string>(),
                        notes);
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var observedAtUtc = _timeProvider.GetUtcNow();
                    var snapshot = new SnapshotEnvelope
                    {
                        SchemaVersion = $"star_map.{partition.PartitionKey}.v1",
                        GeneratedAtUtc = observedAtUtc,
                        ObservedAtUtc = observedAtUtc,
                        Source = partition.Source,
                        Datasource = request.Datasource,
                        Status = "success",
                        Items = partition.MapItems(body, observedAtUtc)
                    };

                    WriteJsonAtomically(outputPath, snapshot);
                    return BuildPartitionView(
                        partition.PartitionKey,
                        outputPath,
                        "success",
                        startedAtUtc,
                        _timeProvider.GetUtcNow(),
                        requestCount,
                        retryCount,
                        snapshot.Items.Count,
                        snapshot.ObservedAtUtc,
                        ReadETag(response),
                        ReadExpiresAtUtc(response),
                        Array.Empty<string>(),
                        notes);
                }

                if (attempt < request.MaxRetriesPerRequest && RetryableStatusCodes.Contains(response.StatusCode))
                {
                    retryCount += 1;
                    await Task.Delay(ComputeRetryDelay(request, attempt), cancellationToken);
                    continue;
                }

                errors.Add($"partition={partition.PartitionKey}: ESI returned {(int)response.StatusCode} {response.StatusCode}.");
                break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < request.MaxRetriesPerRequest)
            {
                retryCount += 1;
                await Task.Delay(ComputeRetryDelay(request, attempt), cancellationToken);
            }
            catch (Exception ex) when (attempt < request.MaxRetriesPerRequest)
            {
                retryCount += 1;
                notes.Add($"partition={partition.PartitionKey}: retrying after transient error '{ex.Message}'.");
                await Task.Delay(ComputeRetryDelay(request, attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"partition={partition.PartitionKey}: {ex.Message}");
                break;
            }
            finally
            {
                response?.Dispose();
            }
        }

        if (priorSnapshot is not null)
        {
            var staleSnapshot = priorSnapshot with
            {
                GeneratedAtUtc = _timeProvider.GetUtcNow(),
                Status = "stale"
            };
            WriteJsonAtomically(outputPath, staleSnapshot);
        }

        return BuildPartitionView(
            partition.PartitionKey,
            outputPath,
            priorSnapshot is null ? "failed" : "stale",
            startedAtUtc,
            _timeProvider.GetUtcNow(),
            requestCount,
            retryCount,
            priorSnapshot?.Items.Count ?? 0,
            priorSnapshot?.ObservedAtUtc,
            null,
            null,
            errors.Count == 0 ? [$"partition={partition.PartitionKey}: refresh failed."] : errors,
            notes);
    }

    private static EsiPublicStarMapFactPartitionView BuildPartitionView(
        string partitionKey,
        string outputPath,
        string status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        int requestCount,
        int retryCount,
        int itemCount,
        DateTimeOffset? observedAtUtc,
        string? eTag,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> notes)
    {
        return new EsiPublicStarMapFactPartitionView
        {
            PartitionKey = partitionKey,
            OutputPath = outputPath,
            Status = status,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RequestCount = requestCount,
            RetryCount = retryCount,
            ItemCount = itemCount,
            ObservedAtUtc = observedAtUtc,
            ETag = eTag,
            ExpiresAtUtc = expiresAtUtc,
            Errors = errors,
            Notes = notes
        };
    }

    private static IReadOnlyList<object> MapSystemJumpsItems(string body, DateTimeOffset observedAtUtc)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(item => (object)new
            {
                solar_system_id = item.GetProperty("system_id").GetInt64(),
                ship_jumps = item.GetProperty("ship_jumps").GetInt64(),
                observed_at_utc = observedAtUtc
            })
            .ToArray();
    }

    private static IReadOnlyList<object> MapSystemKillsItems(string body, DateTimeOffset observedAtUtc)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(item => (object)new
            {
                solar_system_id = item.GetProperty("system_id").GetInt64(),
                ship_kills = item.GetProperty("ship_kills").GetInt64(),
                npc_kills = item.GetProperty("npc_kills").GetInt64(),
                pod_kills = item.GetProperty("pod_kills").GetInt64(),
                observed_at_utc = observedAtUtc
            })
            .ToArray();
    }

    private static IReadOnlyList<object> MapSovereigntyMapItems(string body, DateTimeOffset observedAtUtc)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(item => (object)new
            {
                solar_system_id = item.GetProperty("system_id").GetInt64(),
                alliance_id = item.TryGetProperty("alliance_id", out var allianceId) && allianceId.ValueKind != JsonValueKind.Null ? allianceId.GetInt64() : (long?)null,
                corporation_id = item.TryGetProperty("corporation_id", out var corporationId) && corporationId.ValueKind != JsonValueKind.Null ? corporationId.GetInt64() : (long?)null,
                faction_id = item.TryGetProperty("faction_id", out var factionId) && factionId.ValueKind != JsonValueKind.Null ? factionId.GetInt64() : (long?)null,
                observed_at_utc = observedAtUtc
            })
            .ToArray();
    }

    private static SnapshotEnvelope? ReadPriorSnapshot(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SnapshotEnvelope>(File.ReadAllText(outputPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineOverallStatus(IReadOnlyList<EsiPublicStarMapFactPartitionView> partitions)
    {
        if (partitions.All(partition => partition.Status is "success" or "not_modified"))
        {
            return "completed";
        }

        if (partitions.Any(partition => partition.Status is "success" or "not_modified"))
        {
            return "partial_failure";
        }

        return "failed";
    }

    private static TimeSpan ComputeRetryDelay(EsiPublicStarMapFactsRefreshRequest request, int attempt)
    {
        return TimeSpan.FromMilliseconds(Math.Max(100, request.RetryBaseDelayMilliseconds) * Math.Max(1, attempt + 1));
    }

    private static string? ReadETag(HttpResponseMessage response)
    {
        return response.Headers.ETag?.ToString();
    }

    private static DateTimeOffset? ReadExpiresAtUtc(HttpResponseMessage response)
    {
        return response.Content.Headers.Expires;
    }

    private static void WriteJsonAtomically<T>(string path, T payload)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
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

    private sealed record PartitionDefinition(
        string PartitionKey,
        string FileName,
        string RelativeUri,
        string Source,
        Func<string, DateTimeOffset, IReadOnlyList<object>> MapItems);

    private sealed record SnapshotEnvelope
    {
        public string SchemaVersion { get; init; } = string.Empty;

        public DateTimeOffset GeneratedAtUtc { get; init; }

        public DateTimeOffset? ObservedAtUtc { get; init; }

        public string Source { get; init; } = string.Empty;

        public string Datasource { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public IReadOnlyList<object> Items { get; init; } = Array.Empty<object>();
    }
}
