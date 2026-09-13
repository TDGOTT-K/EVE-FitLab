using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class EsiMarketHistoryIngressCycleRunner
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
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

    public EsiMarketHistoryIngressCycleRunner(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UseCaseResult<EsiMarketHistoryIngressCycleView>> RunCycleAsync(
        EsiMarketHistoryIngressOptions options,
        string hostInstanceId,
        int cycleNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = _timeProvider.GetUtcNow();
        var cycleId = $"market-esi-history-ingress-{Guid.NewGuid():N}";
        var traceId = $"market.esi.history_ingress.cycle:{Guid.NewGuid():N}";
        var artifacts = ResolveArtifactPaths(options, cycleId);
        var state = EsiHistoryIngressStateStore.Load(artifacts.StatePath);
        var metadata = MetadataBootstrapCatalog.LoadDefault();
        var scopePlans = PlanScopes(options, state, metadata, startedAtUtc);
        var executedScopes = new List<EsiMarketHistoryIngressScopeRunView>();
        var notes = new List<string>();
        var errors = new List<string>();

        foreach (var scopePlan in scopePlans.Where(plan => plan.View.Enabled && plan.View.IsDue))
        {
            var pullResult = await ExecuteScopePullAsync(options, scopePlan, cancellationToken);
            executedScopes.Add(pullResult.View);
            if (pullResult.View.Errors.Count > 0)
            {
                errors.AddRange(pullResult.View.Errors);
            }

            if (pullResult.View.Notes.Count > 0)
            {
                notes.AddRange(pullResult.View.Notes);
            }

            state = EsiHistoryIngressStateStore.ApplyScopeResult(state, scopePlan.ScopeKey, pullResult.State);
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var cycle = new EsiMarketHistoryIngressCycleView
        {
            ServiceName = options.ServiceName,
            HostKind = "service_host",
            WorkerKind = "esi_public_market_history_ingress_worker",
            HostInstanceId = hostInstanceId,
            CycleNumber = cycleNumber,
            CycleId = cycleId,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = artifacts.HeartbeatPath,
            ArtifactPaths = artifacts,
            Status = DetermineCycleStatus(scopePlans.Select(plan => plan.View).ToArray(), executedScopes),
            PlannedAtUtc = startedAtUtc,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PollIntervalSeconds = options.PollIntervalSeconds,
            RetainedCycleCount = 0,
            PrunedCycleLogCount = 0,
            Scopes = scopePlans.Select(plan => plan.View).ToArray(),
            ExecutedScopes = executedScopes,
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };

        EsiHistoryIngressStateStore.Save(artifacts.StatePath, state);
        WriteJsonAtomically(artifacts.HeartbeatPath, cycle);
        WriteJsonAtomically(artifacts.CycleLogPath, cycle);
        var retention = PruneCycleLogs(artifacts.CycleLogsDirectoryPath, options.CycleHistoryLimit);
        cycle = cycle with
        {
            RetainedCycleCount = retention.RetainedCount,
            PrunedCycleLogCount = retention.PrunedCount
        };
        WriteJsonAtomically(artifacts.HeartbeatPath, cycle);
        WriteJsonAtomically(artifacts.CycleLogPath, cycle);

        if (cycle.Errors.Count > 0 && executedScopes.Count == 0)
        {
            return new UseCaseResult<EsiMarketHistoryIngressCycleView>
            {
                Status = UseCaseStatus.Error,
                Summary = "market ESI history ingress cycle failed before any scope completed.",
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            };
        }

        if (cycle.Errors.Count > 0)
        {
            return new UseCaseResult<EsiMarketHistoryIngressCycleView>
            {
                Status = UseCaseStatus.Error,
                Summary = "market ESI history ingress cycle completed with failures.",
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            };
        }

        return UseCaseResult<EsiMarketHistoryIngressCycleView>.Success(
            cycle,
            cycle.Status switch
            {
                "idle" => "market ESI history ingress cycle found no due scopes.",
                _ => $"market ESI history ingress cycle completed with {cycle.ExecutedScopes.Count} scope run(s)."
            },
            traceId,
            cycle.Notes);
    }

    private async Task<ScopePullResult> ExecuteScopePullAsync(
        EsiMarketHistoryIngressOptions options,
        ResolvedScopePlan scopePlan,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _timeProvider.GetUtcNow();
        var scope = scopePlan.Scope;
        var payloadPath = scopePlan.PayloadPath;
        var notes = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<string>();
        var throttle = new EsiThrottleCoordinator(_timeProvider, options);
        var httpClient = CreateHttpClient(options);
        var typeResults = new ConcurrentBag<TypePullResult>();

        try
        {
            var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRequests));
            var tasks = scopePlan.ExecutableTypeIds
                .Select(async typeId =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var targetState = scopePlan.ScopeState.Targets.TryGetValue(typeId.ToString(CultureInfo.InvariantCulture), out var existingTargetState)
                            ? existingTargetState
                            : new EsiHistoryTargetState();
                        var result = await PullTypeHistoryAsync(httpClient, options, scopePlan, typeId, targetState, throttle, cancellationToken);
                        typeResults.Add(result);
                        foreach (var note in result.Notes)
                        {
                            notes.Add(note);
                        }

                        foreach (var error in result.Errors)
                        {
                            errors.Add(error);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })
                .ToArray();

            await Task.WhenAll(tasks);

            var orderedResults = typeResults.OrderBy(result => result.TypeId).ToArray();
            var succeededResults = orderedResults.Where(result => string.Equals(result.Status, "completed", StringComparison.Ordinal)).ToArray();
            var notFoundCount = orderedResults.Count(result => string.Equals(result.Status, "not_found", StringComparison.Ordinal));
            var failedCount = orderedResults.Count(result => string.Equals(result.Status, "failed", StringComparison.Ordinal));
            var requestCount = orderedResults.Sum(result => result.RequestCount);
            var retryCount = orderedResults.Sum(result => result.RetryCount);
            var pointCount = succeededResults.Sum(result => result.OutputPointCount);
            var seriesCount = succeededResults.Length;
            string? archivePath = null;

            if (succeededResults.Length > 0)
            {
                var payload = BuildPayloadDocument(scopePlan, startedAtUtc, succeededResults, requestCount, retryCount);
                archivePath = WritePayloadArtifacts(payloadPath, scopePlan.ScopeName, startedAtUtc, payload);
                notes.Add($"Wrote latest payload to '{payloadPath}'.");
                notes.Add($"Archived run payload at '{archivePath}'.");
            }
            else if (File.Exists(payloadPath))
            {
                notes.Add($"No successful series were produced for scope '{scopePlan.ScopeName}'; preserved the existing payload at '{payloadPath}'.");
            }

            var completedAtUtc = _timeProvider.GetUtcNow();
            var updatedTargetStates = scopePlan.ScopeState.Targets.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);

            foreach (var result in orderedResults)
            {
                updatedTargetStates[result.TypeId.ToString(CultureInfo.InvariantCulture)] = result.State;
            }

            var updatedScopeState = CreateUpdatedScopeState(
                scopePlan,
                updatedTargetStates,
                completedAtUtc,
                payloadPath,
                archivePath,
                seriesCount,
                pointCount,
                requestCount,
                retryCount,
                failedCount,
                errors);

            var viewStatus = failedCount switch
            {
                > 0 when seriesCount > 0 || notFoundCount > 0 => "completed_with_failures",
                > 0 => "failed",
                _ when seriesCount > 0 => "completed",
                _ when notFoundCount > 0 => "not_found",
                _ => "idle"
            };

            return new ScopePullResult(
                new EsiMarketHistoryIngressScopeRunView
                {
                    ScopeName = scopePlan.ScopeName,
                    RegionId = scope.RegionId,
                    PayloadPath = payloadPath,
                    Status = viewStatus,
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    PlannedTargetCount = scopePlan.PlannedTypeIds.Count,
                    DueTargetCount = scopePlan.DueTypeIds.Count,
                    ExecutedTargetCount = orderedResults.Length,
                    SucceededTargetCount = seriesCount,
                    NotFoundTargetCount = notFoundCount,
                    FailedTargetCount = failedCount,
                    SeriesCount = seriesCount,
                    PointCount = pointCount,
                    RequestCount = requestCount,
                    RetryCount = retryCount,
                    RateLimit = throttle.CreateView(),
                    Errors = errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    Notes = notes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                },
                updatedScopeState);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var completedAtUtc = _timeProvider.GetUtcNow();
            var scopeErrors = errors.Append($"scope={scopePlan.ScopeName}: {ex.Message}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var updatedScopeState = scopePlan.ScopeState with
            {
                LastStatus = "failed",
                LastCompletedAtUtc = completedAtUtc,
                LastFailedAtUtc = completedAtUtc,
                LastError = string.Join(" | ", scopeErrors),
                NextDueAtUtc = completedAtUtc.AddSeconds(Math.Max(30, scope.FailureBackoffSeconds))
            };

            return new ScopePullResult(
                new EsiMarketHistoryIngressScopeRunView
                {
                    ScopeName = scopePlan.ScopeName,
                    RegionId = scope.RegionId,
                    PayloadPath = payloadPath,
                    Status = "failed",
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    PlannedTargetCount = scopePlan.PlannedTypeIds.Count,
                    DueTargetCount = scopePlan.DueTypeIds.Count,
                    ExecutedTargetCount = typeResults.Count,
                    SucceededTargetCount = typeResults.Count(result => string.Equals(result.Status, "completed", StringComparison.Ordinal)),
                    NotFoundTargetCount = typeResults.Count(result => string.Equals(result.Status, "not_found", StringComparison.Ordinal)),
                    FailedTargetCount = Math.Max(1, typeResults.Count(result => string.Equals(result.Status, "failed", StringComparison.Ordinal))),
                    SeriesCount = typeResults.Count(result => string.Equals(result.Status, "completed", StringComparison.Ordinal)),
                    PointCount = typeResults.Where(result => string.Equals(result.Status, "completed", StringComparison.Ordinal)).Sum(result => result.OutputPointCount),
                    RequestCount = typeResults.Sum(result => result.RequestCount),
                    RetryCount = typeResults.Sum(result => result.RetryCount),
                    RateLimit = throttle.CreateView(),
                    Errors = scopeErrors,
                    Notes = notes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                },
                updatedScopeState);
        }
        finally
        {
            if (_httpClient is null)
            {
                httpClient.Dispose();
            }
        }
    }

    private HttpClient CreateHttpClient(EsiMarketHistoryIngressOptions options)
    {
        if (_httpClient is not null)
        {
            return _httpClient;
        }

        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl), UriKind.Absolute)
        };
    }

    private async Task<TypePullResult> PullTypeHistoryAsync(
        HttpClient httpClient,
        EsiMarketHistoryIngressOptions options,
        ResolvedScopePlan scopePlan,
        long typeId,
        EsiHistoryTargetState targetState,
        EsiThrottleCoordinator throttle,
        CancellationToken cancellationToken)
    {
        var scope = scopePlan.Scope;
        var segmentPlans = ResolveSourceSegments(scope, typeId);
        var segmentResults = new List<SegmentFetchResult>(segmentPlans.Count);
        var notes = new List<string>();
        var errors = new List<string>();

        foreach (var segmentPlan in segmentPlans)
        {
            var segmentResult = await FetchSegmentHistoryAsync(
                httpClient,
                options,
                typeId,
                segmentPlan,
                throttle,
                cancellationToken);

            segmentResults.Add(segmentResult);
            if (segmentResult.Notes.Count > 0)
            {
                notes.AddRange(segmentResult.Notes);
            }

            if (segmentResult.Errors.Count > 0)
            {
                errors.AddRange(segmentResult.Errors);
            }

            if (string.Equals(segmentResult.Status, "failed", StringComparison.Ordinal))
            {
                var failedAtUtc = _timeProvider.GetUtcNow();
                var message = PrefixTypeError(scopePlan.ScopeName, typeId, $"segment fetch failed for source region {segmentPlan.SourceRegionId}.");
                errors.Add(message);
                return TypePullResult.Failed(
                    typeId,
                    segmentResults.Sum(result => result.RequestCount),
                    segmentResults.Sum(result => result.RetryCount),
                    errors.Distinct(StringComparer.Ordinal).ToArray(),
                    notes.Distinct(StringComparer.Ordinal).ToArray(),
                    targetState with
                    {
                        LastStatus = "failed",
                        LastCompletedAtUtc = failedAtUtc,
                        LastFailedAtUtc = failedAtUtc,
                        LastError = string.Join(" | ", errors.Distinct(StringComparer.Ordinal)),
                        NextDueAtUtc = failedAtUtc.AddSeconds(Math.Max(30, scope.FailureBackoffSeconds))
                    });
            }
        }

        var mergedPointsResult = MergeSegmentPoints(scopePlan.ScopeName, typeId, segmentResults);
        if (!mergedPointsResult.IsSuccess)
        {
            var failedAtUtc = _timeProvider.GetUtcNow();
            return TypePullResult.Failed(
                typeId,
                segmentResults.Sum(result => result.RequestCount),
                segmentResults.Sum(result => result.RetryCount),
                mergedPointsResult.Errors,
                notes.Distinct(StringComparer.Ordinal).ToArray(),
                targetState with
                {
                    LastStatus = "failed",
                    LastCompletedAtUtc = failedAtUtc,
                    LastFailedAtUtc = failedAtUtc,
                    LastError = string.Join(" | ", mergedPointsResult.Errors),
                    NextDueAtUtc = failedAtUtc.AddSeconds(Math.Max(30, scope.FailureBackoffSeconds))
                });
        }

        if (mergedPointsResult.Points.Count == 0)
        {
            var notFoundAtUtc = _timeProvider.GetUtcNow();
            var message = PrefixTypeNote(scopePlan.ScopeName, typeId, "no market history points were available in the configured source windows.");
            notes.Add(message);
            return TypePullResult.NotFound(
                typeId,
                segmentResults.Sum(result => result.RequestCount),
                segmentResults.Sum(result => result.RetryCount),
                mergedPointsResult.SegmentTraces,
                notes.Distinct(StringComparer.Ordinal).ToArray(),
                targetState with
                {
                    LastStatus = "not_found",
                    LastCompletedAtUtc = notFoundAtUtc,
                    LastNotFoundAtUtc = notFoundAtUtc,
                    LastError = null,
                    LastFullPointCount = 0,
                    LastOutputPointCount = 0,
                    LastRefreshKind = "not_found",
                    LastMarketDay = null,
                    LastRequestCount = segmentResults.Sum(result => result.RequestCount),
                    LastRetryCount = segmentResults.Sum(result => result.RetryCount),
                    NextDueAtUtc = notFoundAtUtc.AddHours(Math.Max(1, scope.NotFoundRecheckHours))
                });
        }

        var fullPointCount = mergedPointsResult.Points.Count;
        var ready = fullPointCount >= Math.Max(1, scope.MinimumHistoryDaysForReady);
        var refreshKind = targetState.LastSucceededAtUtc is not null && ready
            ? "latest_refresh"
            : "backfill";
        var outputPoints = string.Equals(refreshKind, "latest_refresh", StringComparison.Ordinal)
            ? new[] { mergedPointsResult.Points[^1] }
            : mergedPointsResult.Points;
        var completedAtUtc = _timeProvider.GetUtcNow();
        var lastMarketDay = mergedPointsResult.Points[^1].MarketDay;
        var nextDueAtUtc = ready
            ? completedAtUtc.AddHours(ResolveReadyRefreshIntervalHours(lastMarketDay, completedAtUtc, scope))
            : completedAtUtc.AddHours(Math.Max(1, scope.PartialCoverageRecheckHours));

        return TypePullResult.Success(
            typeId,
            scope.RegionId,
            refreshKind,
            fullPointCount,
            outputPoints.Count,
            outputPoints,
            mergedPointsResult.SegmentTraces,
            segmentResults.Sum(result => result.RequestCount),
            segmentResults.Sum(result => result.RetryCount),
            notes.Distinct(StringComparer.Ordinal).ToArray(),
            targetState with
            {
                LastStatus = "completed",
                LastCompletedAtUtc = completedAtUtc,
                LastSucceededAtUtc = completedAtUtc,
                LastError = null,
                LastFullPointCount = fullPointCount,
                LastOutputPointCount = outputPoints.Count,
                LastRefreshKind = refreshKind,
                LastMarketDay = lastMarketDay,
                LastRequestCount = segmentResults.Sum(result => result.RequestCount),
                LastRetryCount = segmentResults.Sum(result => result.RetryCount),
                NextDueAtUtc = nextDueAtUtc
            });
    }

    private async Task<SegmentFetchResult> FetchSegmentHistoryAsync(
        HttpClient httpClient,
        EsiMarketHistoryIngressOptions options,
        long typeId,
        SourceSegmentPlan segmentPlan,
        EsiThrottleCoordinator throttle,
        CancellationToken cancellationToken)
    {
        var relativePath = BuildHistoryPath(options.Datasource, segmentPlan.SourceRegionId, typeId);
        var requestUrl = new Uri(httpClient.BaseAddress!, relativePath).ToString();
        var notes = new List<string>();
        var requestCount = 0;
        var retryCount = 0;
        EsiResponseMetadata metadata = EsiResponseMetadata.Empty();

        for (var attempt = 0; attempt <= Math.Max(0, options.MaxRetriesPerRequest); attempt++)
        {
            await throttle.WaitForPermitAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(options.CompatibilityDate))
            {
                request.Headers.TryAddWithoutValidation("x-compatibility-date", options.CompatibilityDate);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)));

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < options.MaxRetriesPerRequest)
            {
                requestCount++;
                retryCount++;
                var delay = ComputeRetryDelay(options, attempt, retryAfterSeconds: null);
                notes.Add($"History request timed out for type {typeId} in source region {segmentPlan.SourceRegionId}; retrying after {delay.TotalMilliseconds:F0}ms.");
                await Task.Delay(delay, _timeProvider, cancellationToken);
                continue;
            }

            using (response)
            {
                requestCount++;
                metadata = EsiResponseMetadata.FromResponse(response);
                throttle.Observe(metadata);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return SegmentFetchResult.NotFound(
                        requestCount,
                        retryCount,
                        Array.Empty<HistoryPointPayload>(),
                        BuildSegmentTrace(segmentPlan, requestUrl, (int)response.StatusCode, requestCount, retryCount, Array.Empty<HistoryPointPayload>(), metadata));
                }

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var payload = await JsonSerializer.DeserializeAsync<List<EsiHistoryPointDto>>(stream, JsonOptions, cancellationToken)
                        ?? [];
                    var filteredPoints = FilterPoints(segmentPlan, payload, notes);
                    return SegmentFetchResult.Success(
                        requestCount,
                        retryCount,
                        filteredPoints,
                        BuildSegmentTrace(segmentPlan, requestUrl, (int)response.StatusCode, requestCount, retryCount, filteredPoints, metadata),
                        notes.ToArray());
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (attempt < options.MaxRetriesPerRequest && RetryableStatusCodes.Contains(response.StatusCode))
                {
                    retryCount++;
                    var delay = ComputeRetryDelay(options, attempt, metadata.RetryAfterSeconds);
                    notes.Add($"History request for type {typeId} in source region {segmentPlan.SourceRegionId} returned {(int)response.StatusCode}; retrying after {delay.TotalMilliseconds:F0}ms.");
                    await Task.Delay(delay, _timeProvider, cancellationToken);
                    continue;
                }

                var errorMessage = PrefixTypeError(
                    segmentPlan.ScopeName,
                    typeId,
                    $"source region {segmentPlan.SourceRegionId} returned {(int)response.StatusCode}: {Truncate(errorBody, 240)}");
                return SegmentFetchResult.Failed(
                    requestCount,
                    retryCount,
                    [errorMessage],
                    BuildSegmentTrace(segmentPlan, requestUrl, (int)response.StatusCode, requestCount, retryCount, Array.Empty<HistoryPointPayload>(), metadata));
            }
        }

        var exhaustedMessage = PrefixTypeError(
            segmentPlan.ScopeName,
            typeId,
            $"source region {segmentPlan.SourceRegionId} exhausted retry budget without a terminal response.");
        return SegmentFetchResult.Failed(
            requestCount,
            retryCount,
            [exhaustedMessage],
            BuildSegmentTrace(segmentPlan, requestUrl, null, requestCount, retryCount, Array.Empty<HistoryPointPayload>(), metadata));
    }

    private static IReadOnlyList<HistoryPointPayload> FilterPoints(
        SourceSegmentPlan segmentPlan,
        IReadOnlyList<EsiHistoryPointDto> payload,
        List<string> notes)
    {
        var points = new List<HistoryPointPayload>(payload.Count);
        foreach (var row in payload)
        {
            var marketDay = ParseMarketDay(row.Date);
            if (segmentPlan.StartDateInclusive is { } startDate && marketDay < startDate)
            {
                continue;
            }

            if (segmentPlan.EndDateExclusive is { } endDate && marketDay >= endDate)
            {
                continue;
            }

            points.Add(new HistoryPointPayload(
                marketDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Average,
                row.Highest,
                row.Lowest,
                row.OrderCount,
                row.Volume,
                segmentPlan.SourceRegionId,
                segmentPlan.MarketScope));
        }

        if (payload.Count > 0 && points.Count == 0)
        {
            notes.Add($"Configured source window for region {segmentPlan.SourceRegionId} filtered out all {payload.Count} returned history points.");
        }

        return points;
    }

    private static MergedPointResult MergeSegmentPoints(
        string scopeName,
        long typeId,
        IReadOnlyList<SegmentFetchResult> segmentResults)
    {
        var mergedPoints = new List<HistoryPointPayload>();
        var seenMarketDays = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segmentResult in segmentResults)
        {
            foreach (var point in segmentResult.Points.OrderBy(point => point.MarketDay, StringComparer.Ordinal))
            {
                if (!seenMarketDays.Add(point.MarketDay))
                {
                    return MergedPointResult.Failed(
                        PrefixTypeError(
                            scopeName,
                            typeId,
                            $"duplicate market day '{point.MarketDay}' appeared across configured source segments; segment boundaries overlap."));
                }

                mergedPoints.Add(point);
            }
        }

        mergedPoints.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.MarketDay, right.MarketDay));
        return MergedPointResult.Success(
            mergedPoints,
            segmentResults.Select(result => result.Trace).ToArray());
    }

    private static HistoryIngressPayload BuildPayloadDocument(
        ResolvedScopePlan scopePlan,
        DateTimeOffset startedAtUtc,
        IReadOnlyList<TypePullResult> succeededResults,
        int requestCount,
        int retryCount)
    {
        var series = succeededResults
            .Select(result => new HistorySeriesPayload(
                result.TypeId,
                result.LogicalRegionId,
                result.RefreshKind,
                result.FullPointCount,
                result.OutputPointCount,
                result.Points.Count > 0 ? result.Points[0].MarketDay : null,
                result.Points.Count > 0 ? result.Points[^1].MarketDay : null,
                result.SourceSegments,
                result.Points))
            .ToArray();

        return new HistoryIngressPayload(
            $"esi-public-market-history:{scopePlan.Scope.RegionId}",
            scopePlan.Scope.RegionId,
            scopePlan.ScopeName,
            startedAtUtc,
            new HistoryIngressTracePayload(
                scopePlan.PlannedTypeIds.Count,
                scopePlan.DueTypeIds.Count,
                scopePlan.ExecutableTypeIds.Count,
                succeededResults.Count,
                requestCount,
                retryCount,
                succeededResults.Sum(result => result.FullPointCount),
                succeededResults.Sum(result => result.OutputPointCount)),
            series);
    }

    private static EsiHistoryScopeState CreateUpdatedScopeState(
        ResolvedScopePlan scopePlan,
        IReadOnlyDictionary<string, EsiHistoryTargetState> updatedTargetStates,
        DateTimeOffset completedAtUtc,
        string payloadPath,
        string? archivePath,
        int seriesCount,
        int pointCount,
        int requestCount,
        int retryCount,
        int failedCount,
        IEnumerable<string> errors)
    {
        var relevantTargetStates = scopePlan.PlannedTypeIds
            .Select(typeId => updatedTargetStates.GetValueOrDefault(typeId.ToString(CultureInfo.InvariantCulture)))
            .Where(state => state is not null)
            .Cast<EsiHistoryTargetState>()
            .ToArray();

        var nextDueAtUtc = relevantTargetStates
            .Where(state => state.NextDueAtUtc is not null)
            .Select(state => state.NextDueAtUtc)
            .Order()
            .FirstOrDefault();

        var lastSucceededAtUtc = relevantTargetStates
            .Where(state => state.LastSucceededAtUtc is not null)
            .Select(state => state.LastSucceededAtUtc)
            .OrderDescending()
            .FirstOrDefault();

        var lastFailedAtUtc = relevantTargetStates
            .Where(state => state.LastFailedAtUtc is not null)
            .Select(state => state.LastFailedAtUtc)
            .OrderDescending()
            .FirstOrDefault();

        var lastStatus = failedCount switch
        {
            > 0 when seriesCount > 0 => "completed_with_failures",
            > 0 => "failed",
            _ when seriesCount > 0 => "completed",
            _ => "not_found"
        };

        return new EsiHistoryScopeState
        {
            LastStatus = lastStatus,
            LastCompletedAtUtc = completedAtUtc,
            LastSucceededAtUtc = lastSucceededAtUtc ?? scopePlan.ScopeState.LastSucceededAtUtc,
            LastFailedAtUtc = lastFailedAtUtc ?? scopePlan.ScopeState.LastFailedAtUtc,
            NextDueAtUtc = nextDueAtUtc,
            LastError = failedCount > 0 ? string.Join(" | ", errors.Distinct(StringComparer.Ordinal)) : null,
            LastPayloadPath = seriesCount > 0 ? payloadPath : scopePlan.ScopeState.LastPayloadPath,
            LastArchivePath = seriesCount > 0 ? archivePath : scopePlan.ScopeState.LastArchivePath,
            LastSeriesCount = seriesCount,
            LastPointCount = pointCount,
            LastRequestCount = requestCount,
            LastRetryCount = retryCount,
            Targets = updatedTargetStates
        };
    }

    private static IReadOnlyList<ResolvedScopePlan> PlanScopes(
        EsiMarketHistoryIngressOptions options,
        EsiHistoryIngressState state,
        MetadataBootstrapCatalog metadata,
        DateTimeOffset nowUtc)
    {
        var plans = new List<ResolvedScopePlan>(options.Scopes.Count);
        foreach (var scope in options.Scopes)
        {
            var scopeName = ResolveScopeName(scope);
            var scopeKey = BuildScopeKey(scope);
            var payloadPath = ResolvePayloadPath(options, scope);
            var scopeState = state.Scopes.TryGetValue(scopeKey, out var existingScopeState)
                ? existingScopeState
                : new EsiHistoryScopeState();
            var plannedTypeIds = ResolveTargetTypeIds(scope, metadata);
            var dueTypeIds = plannedTypeIds
                .Where(typeId => IsTargetDue(scopeState.Targets.GetValueOrDefault(typeId.ToString(CultureInfo.InvariantCulture)), nowUtc))
                .OrderBy(typeId => ComputeTargetPriority(scopeState.Targets.GetValueOrDefault(typeId.ToString(CultureInfo.InvariantCulture)), nowUtc, scope))
                .ThenBy(typeId => scopeState.Targets.GetValueOrDefault(typeId.ToString(CultureInfo.InvariantCulture))?.NextDueAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(typeId => typeId)
                .ToArray();
            var executableTypeIds = dueTypeIds
                .Take(Math.Max(1, scope.MaxTypesPerCycle))
                .ToArray();
            var enabled = scope.Enabled;
            var isDue = enabled && executableTypeIds.Length > 0;
            var decision = enabled switch
            {
                false => "disabled",
                _ when plannedTypeIds.Length == 0 => "no_targets",
                _ when dueTypeIds.Length == 0 => scopeState.NextDueAtUtc is { } nextDueAtUtc
                    ? $"waiting_until:{nextDueAtUtc:O}"
                    : "waiting_for_due_targets",
                _ => $"pulling:{executableTypeIds.Length}"
            };

            plans.Add(new ResolvedScopePlan(
                scopeKey,
                scopeName,
                scope,
                payloadPath,
                scopeState,
                plannedTypeIds,
                dueTypeIds,
                executableTypeIds,
                new EsiMarketHistoryIngressPlannedScopeView
                {
                    ScopeName = scopeName,
                    RegionId = scope.RegionId,
                    Enabled = enabled,
                    IsDue = isDue,
                    PlannedTargetCount = plannedTypeIds.Length,
                    DueTargetCount = dueTypeIds.Length,
                    MaxTypesPerCycle = Math.Max(1, scope.MaxTypesPerCycle),
                    PayloadPath = payloadPath,
                    Decision = decision,
                    LastStatus = scopeState.LastStatus,
                    LastSucceededAtUtc = scopeState.LastSucceededAtUtc,
                    LastFailedAtUtc = scopeState.LastFailedAtUtc,
                    NextDueAtUtc = scopeState.NextDueAtUtc
                }));
        }

        return plans;
    }

    private static long[] ResolveTargetTypeIds(EsiMarketHistoryIngressScopeOptions scope, MetadataBootstrapCatalog metadata)
    {
        IEnumerable<long> baseTypeIds = scope.IncludeTypeIds.Count > 0
            ? scope.IncludeTypeIds
            : metadata.ListPublishedTypes()
                .Where(type => type.MarketGroupId.HasValue)
                .Select(type => type.TypeId);

        return baseTypeIds
            .Where(typeId => !scope.ExcludeTypeIds.Contains(typeId))
            .Distinct()
            .Order()
            .ToArray();
    }

    private static bool IsTargetDue(EsiHistoryTargetState? state, DateTimeOffset nowUtc)
    {
        return state?.NextDueAtUtc is null || state.NextDueAtUtc <= nowUtc;
    }

    private static int ComputeTargetPriority(
        EsiHistoryTargetState? state,
        DateTimeOffset nowUtc,
        EsiMarketHistoryIngressScopeOptions scope)
    {
        if (!scope.EnableRecentActivityPrioritization)
        {
            return 0;
        }

        if (state is null || state.LastSucceededAtUtc is null)
        {
            return 0;
        }

        return IsRecentlyActive(state.LastMarketDay, nowUtc, scope) ? 0 : 1;
    }

    private static int ResolveReadyRefreshIntervalHours(
        string? lastMarketDay,
        DateTimeOffset nowUtc,
        EsiMarketHistoryIngressScopeOptions scope)
    {
        if (!scope.EnableRecentActivityPrioritization)
        {
            return Math.Max(1, scope.SuccessRefreshIntervalHours);
        }

        return IsRecentlyActive(lastMarketDay, nowUtc, scope)
            ? Math.Max(1, scope.SuccessRefreshIntervalHours)
            : Math.Max(1, scope.DormantRefreshIntervalHours);
    }

    private static bool IsRecentlyActive(
        string? lastMarketDay,
        DateTimeOffset nowUtc,
        EsiMarketHistoryIngressScopeOptions scope)
    {
        if (string.IsNullOrWhiteSpace(lastMarketDay))
        {
            return false;
        }

        DateOnly parsedDay;
        try
        {
            parsedDay = ParseMarketDay(lastMarketDay);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);
        var cutoff = today.AddDays(-Math.Max(1, scope.RecentActivityLookbackDays));
        return parsedDay >= cutoff;
    }

    private static IReadOnlyList<SourceSegmentPlan> ResolveSourceSegments(EsiMarketHistoryIngressScopeOptions scope, long typeId)
    {
        var overrideOptions = scope.HistorySourceOverrides.FirstOrDefault(candidate => candidate.TypeId == typeId);
        if (overrideOptions is null || overrideOptions.Segments.Count == 0)
        {
            return
            [
                new SourceSegmentPlan(
                    ResolveScopeName(scope),
                    scope.RegionId,
                    "regional",
                    StartDateInclusive: null,
                    EndDateExclusive: null)
            ];
        }

        return overrideOptions.Segments
            .Select(segment => new SourceSegmentPlan(
                ResolveScopeName(scope),
                segment.SourceRegionId,
                string.IsNullOrWhiteSpace(segment.MarketScope) ? "regional" : segment.MarketScope,
                string.IsNullOrWhiteSpace(segment.StartDateInclusive) ? null : ParseMarketDay(segment.StartDateInclusive),
                string.IsNullOrWhiteSpace(segment.EndDateExclusive) ? null : ParseMarketDay(segment.EndDateExclusive)))
            .ToArray();
    }

    private static string DetermineCycleStatus(
        IReadOnlyList<EsiMarketHistoryIngressPlannedScopeView> plannedScopes,
        IReadOnlyList<EsiMarketHistoryIngressScopeRunView> executedScopes)
    {
        if (plannedScopes.All(scope => !scope.IsDue))
        {
            return "idle";
        }

        if (executedScopes.Any(scope => string.Equals(scope.Status, "failed", StringComparison.Ordinal)))
        {
            return executedScopes.Any(scope =>
                    string.Equals(scope.Status, "completed", StringComparison.Ordinal) ||
                    string.Equals(scope.Status, "completed_with_failures", StringComparison.Ordinal) ||
                    string.Equals(scope.Status, "not_found", StringComparison.Ordinal))
                ? "completed_with_failures"
                : "failed";
        }

        if (executedScopes.Any(scope => string.Equals(scope.Status, "completed_with_failures", StringComparison.Ordinal)))
        {
            return "completed_with_failures";
        }

        return "completed";
    }

    private static EsiMarketHistoryIngressArtifactPathsView ResolveArtifactPaths(EsiMarketHistoryIngressOptions options, string cycleId)
    {
        var serviceStateDirectoryPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options.ServiceStateDirectoryPath)
                ? Path.Combine(options.OutputDirectoryPath, "_service_state")
                : options.ServiceStateDirectoryPath);
        var cycleLogsDirectoryPath = Path.Combine(serviceStateDirectoryPath, "cycle-logs");
        var heartbeatPath = string.IsNullOrWhiteSpace(options.HeartbeatPath)
            ? Path.Combine(serviceStateDirectoryPath, "heartbeat.json")
            : Path.GetFullPath(options.HeartbeatPath);
        var cycleLogPath = Path.Combine(cycleLogsDirectoryPath, $"{cycleId}.json");
        var statePath = Path.Combine(serviceStateDirectoryPath, "ingress-state.json");

        return new EsiMarketHistoryIngressArtifactPathsView
        {
            OutputDirectoryPath = Path.GetFullPath(options.OutputDirectoryPath),
            ServiceStateDirectoryPath = serviceStateDirectoryPath,
            HeartbeatPath = heartbeatPath,
            CycleLogsDirectoryPath = cycleLogsDirectoryPath,
            CycleLogPath = cycleLogPath,
            StatePath = statePath
        };
    }

    private static (int RetainedCount, int PrunedCount) PruneCycleLogs(string cycleLogsDirectoryPath, int cycleHistoryLimit)
    {
        Directory.CreateDirectory(cycleLogsDirectoryPath);
        var cycleLogs = new DirectoryInfo(cycleLogsDirectoryPath)
            .GetFiles("*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        if (cycleHistoryLimit <= 0)
        {
            cycleHistoryLimit = 1;
        }

        var prunedCount = 0;
        foreach (var file in cycleLogs.Skip(cycleHistoryLimit))
        {
            file.Delete();
            prunedCount++;
        }

        return (Math.Min(cycleLogs.Length, cycleHistoryLimit), prunedCount);
    }

    private static string ResolvePayloadPath(EsiMarketHistoryIngressOptions options, EsiMarketHistoryIngressScopeOptions scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.PayloadPath))
        {
            return Path.GetFullPath(scope.PayloadPath);
        }

        return Path.Combine(
            Path.GetFullPath(options.OutputDirectoryPath),
            "latest",
            $"{SanitizeScopeName(ResolveScopeName(scope))}.history-ingress.json");
    }

    private static string WritePayloadArtifacts(string payloadPath, string scopeName, DateTimeOffset startedAtUtc, HistoryIngressPayload payload)
    {
        WriteJsonAtomically(payloadPath, payload);
        var archivePath = Path.Combine(
            Path.GetDirectoryName(payloadPath) ?? string.Empty,
            "..",
            "archive",
            SanitizeScopeName(scopeName),
            $"{startedAtUtc:yyyyMMddTHHmmssfffZ}.history-ingress.json");
        archivePath = Path.GetFullPath(archivePath);
        WriteJsonAtomically(archivePath, payload);
        return archivePath;
    }

    private static string BuildHistoryPath(string datasource, long regionId, long typeId)
    {
        return $"markets/{regionId}/history/?datasource={Uri.EscapeDataString(datasource)}&type_id={typeId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ResolveScopeName(EsiMarketHistoryIngressScopeOptions scope)
    {
        return string.IsNullOrWhiteSpace(scope.ScopeName)
            ? $"region-{scope.RegionId}"
            : scope.ScopeName.Trim();
    }

    private static string BuildScopeKey(EsiMarketHistoryIngressScopeOptions scope)
    {
        return $"{ResolveScopeName(scope)}|{scope.RegionId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string PrefixTypeError(string scopeName, long typeId, string message)
        => $"scope={scopeName} type_id={typeId}: {message}";

    private static string PrefixTypeNote(string scopeName, long typeId, string message)
        => $"scope={scopeName} type_id={typeId}: {message}";

    private static string EnsureTrailingSlash(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("History ingress base URL must not be empty.");
        }

        return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
    }

    private static string SanitizeScopeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = value.Select(character => invalid.Contains(character) ? '-' : character).ToArray();
        return new string(buffer);
    }

    private static DateOnly ParseMarketDay(string value)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTimeOffset))
        {
            return DateOnly.FromDateTime(dateTimeOffset.UtcDateTime);
        }

        throw new InvalidOperationException($"Unable to parse market history day '{value}'.");
    }

    private TimeSpan ComputeRetryDelay(EsiMarketHistoryIngressOptions options, int attempt, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is > 0)
        {
            return TimeSpan.FromSeconds(retryAfterSeconds.Value);
        }

        var delayMilliseconds = Math.Max(100, options.RetryBaseDelayMilliseconds) * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(delayMilliseconds, 30_000));
    }

    private static HistorySourceSegmentPayload BuildSegmentTrace(
        SourceSegmentPlan segmentPlan,
        string requestUrl,
        int? statusCode,
        int requestCount,
        int retryCount,
        IReadOnlyList<HistoryPointPayload> points,
        EsiResponseMetadata metadata)
    {
        return new HistorySourceSegmentPayload(
            segmentPlan.SourceRegionId,
            segmentPlan.MarketScope,
            segmentPlan.StartDateInclusive?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            segmentPlan.EndDateExclusive?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            requestUrl,
            statusCode,
            requestCount,
            retryCount,
            points.Count > 0 ? points[0].MarketDay : null,
            points.Count > 0 ? points[^1].MarketDay : null,
            points.Count,
            metadata.ErrorLimitRemain,
            metadata.ErrorLimitResetSeconds,
            metadata.RateLimitRemain,
            metadata.RateLimitResetSeconds,
            metadata.RateLimitGroup);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : $"{value[..maxLength]}...";
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
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, payload, JsonOptions);
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return global::EdenOS.Application.JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);
    }

    private sealed record ResolvedScopePlan(
        string ScopeKey,
        string ScopeName,
        EsiMarketHistoryIngressScopeOptions Scope,
        string PayloadPath,
        EsiHistoryScopeState ScopeState,
        IReadOnlyList<long> PlannedTypeIds,
        IReadOnlyList<long> DueTypeIds,
        IReadOnlyList<long> ExecutableTypeIds,
        EsiMarketHistoryIngressPlannedScopeView View);

    private sealed record SourceSegmentPlan(
        string ScopeName,
        long SourceRegionId,
        string MarketScope,
        DateOnly? StartDateInclusive,
        DateOnly? EndDateExclusive);

    private sealed record ScopePullResult(EsiMarketHistoryIngressScopeRunView View, EsiHistoryScopeState State);

    private sealed record TypePullResult(
        long TypeId,
        string Status,
        long LogicalRegionId,
        string RefreshKind,
        int FullPointCount,
        int OutputPointCount,
        IReadOnlyList<HistoryPointPayload> Points,
        IReadOnlyList<HistorySourceSegmentPayload> SourceSegments,
        int RequestCount,
        int RetryCount,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes,
        EsiHistoryTargetState State)
    {
        public static TypePullResult Success(
            long typeId,
            long logicalRegionId,
            string refreshKind,
            int fullPointCount,
            int outputPointCount,
            IReadOnlyList<HistoryPointPayload> points,
            IReadOnlyList<HistorySourceSegmentPayload> sourceSegments,
            int requestCount,
            int retryCount,
            IReadOnlyList<string> notes,
            EsiHistoryTargetState state)
            => new(
                typeId,
                "completed",
                logicalRegionId,
                refreshKind,
                fullPointCount,
                outputPointCount,
                points,
                sourceSegments,
                requestCount,
                retryCount,
                Array.Empty<string>(),
                notes,
                state);

        public static TypePullResult NotFound(
            long typeId,
            int requestCount,
            int retryCount,
            IReadOnlyList<HistorySourceSegmentPayload> sourceSegments,
            IReadOnlyList<string> notes,
            EsiHistoryTargetState state)
            => new(
                typeId,
                "not_found",
                0,
                "not_found",
                0,
                0,
                Array.Empty<HistoryPointPayload>(),
                sourceSegments,
                requestCount,
                retryCount,
                Array.Empty<string>(),
                notes,
                state);

        public static TypePullResult Failed(
            long typeId,
            int requestCount,
            int retryCount,
            IReadOnlyList<string> errors,
            IReadOnlyList<string> notes,
            EsiHistoryTargetState state)
            => new(
                typeId,
                "failed",
                0,
                "failed",
                0,
                0,
                Array.Empty<HistoryPointPayload>(),
                Array.Empty<HistorySourceSegmentPayload>(),
                requestCount,
                retryCount,
                errors,
                notes,
                state);
    }

    private sealed record SegmentFetchResult(
        string Status,
        IReadOnlyList<HistoryPointPayload> Points,
        int RequestCount,
        int RetryCount,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes,
        HistorySourceSegmentPayload Trace)
    {
        public static SegmentFetchResult Success(
            int requestCount,
            int retryCount,
            IReadOnlyList<HistoryPointPayload> points,
            HistorySourceSegmentPayload trace,
            IReadOnlyList<string> notes)
            => new("completed", points, requestCount, retryCount, Array.Empty<string>(), notes, trace);

        public static SegmentFetchResult NotFound(
            int requestCount,
            int retryCount,
            IReadOnlyList<HistoryPointPayload> points,
            HistorySourceSegmentPayload trace)
            => new("not_found", points, requestCount, retryCount, Array.Empty<string>(), Array.Empty<string>(), trace);

        public static SegmentFetchResult Failed(
            int requestCount,
            int retryCount,
            IReadOnlyList<string> errors,
            HistorySourceSegmentPayload trace)
            => new("failed", Array.Empty<HistoryPointPayload>(), requestCount, retryCount, errors, Array.Empty<string>(), trace);
    }

    private sealed record MergedPointResult(bool IsSuccess, IReadOnlyList<HistoryPointPayload> Points, IReadOnlyList<HistorySourceSegmentPayload> SegmentTraces, IReadOnlyList<string> Errors)
    {
        public static MergedPointResult Success(IReadOnlyList<HistoryPointPayload> points, IReadOnlyList<HistorySourceSegmentPayload> segmentTraces)
            => new(true, points, segmentTraces, Array.Empty<string>());

        public static MergedPointResult Failed(params string[] errors)
            => new(false, Array.Empty<HistoryPointPayload>(), Array.Empty<HistorySourceSegmentPayload>(), errors);
    }

    private sealed record HistoryIngressPayload(
        string Source,
        long LogicalRegionId,
        string ScopeName,
        DateTimeOffset PulledAtUtc,
        HistoryIngressTracePayload IngressTrace,
        IReadOnlyList<HistorySeriesPayload> Series);

    private sealed record HistoryIngressTracePayload(
        int PlannedTargetCount,
        int DueTargetCount,
        int ExecutedTargetCount,
        int SucceededTargetCount,
        int RequestCount,
        int RetryCount,
        int FullPointCount,
        int OutputPointCount);

    private sealed record HistorySeriesPayload(
        long TypeId,
        long LogicalRegionId,
        string RefreshKind,
        int FullPointCount,
        int PointCount,
        string? FirstMarketDay,
        string? LastMarketDay,
        IReadOnlyList<HistorySourceSegmentPayload> SourceSegments,
        IReadOnlyList<HistoryPointPayload> Points);

    private sealed record HistorySourceSegmentPayload(
        long SourceRegionId,
        string MarketScope,
        string? StartDateInclusive,
        string? EndDateExclusive,
        string RequestUrl,
        int? StatusCode,
        int RequestCount,
        int RetryCount,
        string? FirstMarketDay,
        string? LastMarketDay,
        int PointCount,
        int? ErrorLimitRemain,
        int? ErrorLimitResetSeconds,
        int? RateLimitRemain,
        int? RateLimitResetSeconds,
        string? RateLimitGroup);

    private sealed record HistoryPointPayload(
        string MarketDay,
        decimal Average,
        decimal Highest,
        decimal Lowest,
        long OrderCount,
        long Volume,
        long SourceRegionId,
        string MarketScope);

    private sealed record EsiHistoryPointDto(
        decimal Average,
        string Date,
        decimal Highest,
        decimal Lowest,
        long OrderCount,
        long Volume);

    private sealed record EsiResponseMetadata
    {
        public int? RetryAfterSeconds { get; init; }
        public int? ErrorLimitRemain { get; init; }
        public int? ErrorLimitResetSeconds { get; init; }
        public int? RateLimitRemain { get; init; }
        public int? RateLimitResetSeconds { get; init; }
        public string? RateLimitGroup { get; init; }

        public static EsiResponseMetadata Empty() => new();

        public static EsiResponseMetadata FromResponse(HttpResponseMessage response)
        {
            return new EsiResponseMetadata
            {
                RetryAfterSeconds = TryParseRetryAfter(response.Headers.RetryAfter),
                ErrorLimitRemain = TryParseInt(response.Headers, "x-esi-error-limit-remain"),
                ErrorLimitResetSeconds = TryParseInt(response.Headers, "x-esi-error-limit-reset"),
                RateLimitRemain = TryParseInt(response.Headers, "x-ratelimit-remaining", "ratelimit-remaining", "x-rate-limit-remaining"),
                RateLimitResetSeconds = TryParseInt(response.Headers, "x-ratelimit-reset", "ratelimit-reset", "x-rate-limit-reset"),
                RateLimitGroup = TryGetValue(response.Headers, "x-ratelimit-group", "ratelimit-group", "x-rate-limit-group")
            };
        }

        private static int? TryParseRetryAfter(RetryConditionHeaderValue? retryAfter)
        {
            if (retryAfter is null)
            {
                return null;
            }

            if (retryAfter.Delta is { } delta)
            {
                return (int)Math.Ceiling(delta.TotalSeconds);
            }

            if (retryAfter.Date is { } retryDate)
            {
                return (int)Math.Ceiling((retryDate - DateTimeOffset.UtcNow).TotalSeconds);
            }

            return null;
        }

        private static string? TryGetValue(HttpResponseHeaders headers, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (headers.TryGetValues(key, out var values))
                {
                    return values.FirstOrDefault();
                }
            }

            return null;
        }

        private static int? TryParseInt(HttpResponseHeaders headers, params string[] keys)
        {
            var value = TryGetValue(headers, keys);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    private sealed class EsiThrottleCoordinator
    {
        private readonly TimeProvider _timeProvider;
        private readonly EsiMarketHistoryIngressOptions _options;
        private readonly object _sync = new();
        private DateTimeOffset? _pauseUntilUtc;
        private int? _errorLimitRemain;
        private int? _errorLimitResetSeconds;
        private int? _rateLimitRemain;
        private int? _rateLimitResetSeconds;
        private string? _rateLimitGroup;

        public EsiThrottleCoordinator(TimeProvider timeProvider, EsiMarketHistoryIngressOptions options)
        {
            _timeProvider = timeProvider;
            _options = options;
        }

        public async Task WaitForPermitAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                DateTimeOffset? pauseUntilUtc;
                lock (_sync)
                {
                    pauseUntilUtc = _pauseUntilUtc;
                }

                if (pauseUntilUtc is null || pauseUntilUtc <= _timeProvider.GetUtcNow())
                {
                    return;
                }

                await Task.Delay(pauseUntilUtc.Value - _timeProvider.GetUtcNow(), _timeProvider, cancellationToken);
            }
        }

        public void Observe(EsiResponseMetadata metadata)
        {
            lock (_sync)
            {
                _errorLimitRemain = metadata.ErrorLimitRemain ?? _errorLimitRemain;
                _errorLimitResetSeconds = metadata.ErrorLimitResetSeconds ?? _errorLimitResetSeconds;
                _rateLimitRemain = metadata.RateLimitRemain ?? _rateLimitRemain;
                _rateLimitResetSeconds = metadata.RateLimitResetSeconds ?? _rateLimitResetSeconds;
                _rateLimitGroup = metadata.RateLimitGroup ?? _rateLimitGroup;

                if (metadata.RetryAfterSeconds is > 0)
                {
                    ExtendPause(TimeSpan.FromSeconds(metadata.RetryAfterSeconds.Value));
                }

                if (metadata.ErrorLimitRemain is not null &&
                    metadata.ErrorLimitResetSeconds is not null &&
                    metadata.ErrorLimitRemain <= _options.ErrorLimitPauseThreshold)
                {
                    ExtendPause(TimeSpan.FromSeconds(metadata.ErrorLimitResetSeconds.Value));
                }

                if (metadata.RateLimitRemain is not null &&
                    metadata.RateLimitResetSeconds is not null &&
                    metadata.RateLimitRemain <= _options.RateLimitPauseThreshold)
                {
                    ExtendPause(TimeSpan.FromSeconds(metadata.RateLimitResetSeconds.Value));
                }
            }
        }

        public EsiMarketHistoryIngressRateLimitView CreateView()
        {
            lock (_sync)
            {
                return new EsiMarketHistoryIngressRateLimitView
                {
                    ErrorLimitRemain = _errorLimitRemain,
                    ErrorLimitResetSeconds = _errorLimitResetSeconds,
                    RateLimitRemain = _rateLimitRemain,
                    RateLimitResetSeconds = _rateLimitResetSeconds,
                    RateLimitGroup = _rateLimitGroup,
                    PauseUntilUtc = _pauseUntilUtc
                };
            }
        }

        private void ExtendPause(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            var candidate = _timeProvider.GetUtcNow().Add(duration);
            _pauseUntilUtc = _pauseUntilUtc is { } current && current > candidate ? current : candidate;
        }
    }

    private sealed record EsiHistoryIngressState(IReadOnlyDictionary<string, EsiHistoryScopeState> Scopes)
    {
        public static EsiHistoryIngressState Empty { get; } = new(new Dictionary<string, EsiHistoryScopeState>(StringComparer.Ordinal));
    }

    private sealed record EsiHistoryScopeState
    {
        public string? LastStatus { get; init; }
        public DateTimeOffset? LastCompletedAtUtc { get; init; }
        public DateTimeOffset? LastSucceededAtUtc { get; init; }
        public DateTimeOffset? LastFailedAtUtc { get; init; }
        public DateTimeOffset? NextDueAtUtc { get; init; }
        public string? LastError { get; init; }
        public string? LastPayloadPath { get; init; }
        public string? LastArchivePath { get; init; }
        public int LastSeriesCount { get; init; }
        public int LastPointCount { get; init; }
        public int LastRequestCount { get; init; }
        public int LastRetryCount { get; init; }
        public IReadOnlyDictionary<string, EsiHistoryTargetState> Targets { get; init; } = new Dictionary<string, EsiHistoryTargetState>(StringComparer.Ordinal);
    }

    private sealed record EsiHistoryTargetState
    {
        public string? LastStatus { get; init; }
        public DateTimeOffset? LastCompletedAtUtc { get; init; }
        public DateTimeOffset? LastSucceededAtUtc { get; init; }
        public DateTimeOffset? LastFailedAtUtc { get; init; }
        public DateTimeOffset? LastNotFoundAtUtc { get; init; }
        public DateTimeOffset? NextDueAtUtc { get; init; }
        public string? LastError { get; init; }
        public int LastFullPointCount { get; init; }
        public int LastOutputPointCount { get; init; }
        public string? LastRefreshKind { get; init; }
        public string? LastMarketDay { get; init; }
        public int LastRequestCount { get; init; }
        public int LastRetryCount { get; init; }
    }

    private static class EsiHistoryIngressStateStore
    {
        public static EsiHistoryIngressState Load(string path)
        {
            if (!File.Exists(path))
            {
                return EsiHistoryIngressState.Empty;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return EsiHistoryIngressState.Empty;
            }

            var payload = JsonSerializer.Deserialize<EsiHistoryIngressStateDocument>(text, JsonOptions);
            if (payload?.Scopes is null)
            {
                return EsiHistoryIngressState.Empty;
            }

            return new EsiHistoryIngressState(payload.Scopes.ToDictionary(
                entry => entry.Key,
                entry => entry.Value ?? new EsiHistoryScopeState(),
                StringComparer.Ordinal));
        }

        public static void Save(string path, EsiHistoryIngressState state)
        {
            var document = new EsiHistoryIngressStateDocument
            {
                Scopes = state.Scopes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
            };
            WriteJsonAtomically(path, document);
        }

        public static EsiHistoryIngressState ApplyScopeResult(EsiHistoryIngressState current, string scopeKey, EsiHistoryScopeState scopeState)
        {
            var scopes = current.Scopes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            scopes[scopeKey] = scopeState;
            return new EsiHistoryIngressState(scopes);
        }
    }

    private sealed record EsiHistoryIngressStateDocument
    {
        public Dictionary<string, EsiHistoryScopeState> Scopes { get; init; } = new(StringComparer.Ordinal);
    }
}
