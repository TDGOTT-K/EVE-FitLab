using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed partial class EsiMarketOrdersIngressCycleRunner
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

    public EsiMarketOrdersIngressCycleRunner(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UseCaseResult<EsiMarketOrdersIngressCycleView>> RunCycleAsync(
        EsiMarketOrdersIngressOptions options,
        string hostInstanceId,
        int cycleNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = _timeProvider.GetUtcNow();
        var cycleId = $"market-esi-ingress-{Guid.NewGuid():N}";
        var traceId = $"market.esi.ingress.cycle:{Guid.NewGuid():N}";
        var artifacts = ResolveArtifactPaths(options, cycleId);
        var state = EsiIngressStateStore.Load(artifacts.StatePath);
        var plannedScopes = PlanScopes(options, state, startedAtUtc);
        var executedScopes = new List<EsiMarketOrdersIngressScopeRunView>();
        var notes = new List<string>();
        var errors = new List<string>();

        foreach (var plannedScope in plannedScopes.Where(scope => scope.Enabled && scope.IsDue))
        {
            var scopeOptions = options.Scopes.Single(scope =>
                string.Equals(ResolveScopeName(scope), plannedScope.ScopeName, StringComparison.Ordinal));

            var pullResult = await ExecuteScopePullAsync(options, scopeOptions, state, cancellationToken);
            executedScopes.Add(pullResult.View);
            if (pullResult.View.Errors.Count > 0)
            {
                errors.AddRange(pullResult.View.Errors);
            }

            if (pullResult.View.Notes.Count > 0)
            {
                notes.AddRange(pullResult.View.Notes);
            }

            state = EsiIngressStateStore.ApplyScopeResult(state, scopeOptions, pullResult.State);
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var cycle = new EsiMarketOrdersIngressCycleView
        {
            ServiceName = options.ServiceName,
            HostKind = "service_host",
            WorkerKind = "esi_public_market_ingress_worker",
            HostInstanceId = hostInstanceId,
            CycleNumber = cycleNumber,
            CycleId = cycleId,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = artifacts.HeartbeatPath,
            ArtifactPaths = artifacts,
            Status = DetermineCycleStatus(plannedScopes, executedScopes),
            PlannedAtUtc = startedAtUtc,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PollIntervalSeconds = options.PollIntervalSeconds,
            RetainedCycleCount = 0,
            PrunedCycleLogCount = 0,
            Scopes = plannedScopes,
            ExecutedScopes = executedScopes,
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };

        EsiIngressStateStore.Save(artifacts.StatePath, state);
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
            return new UseCaseResult<EsiMarketOrdersIngressCycleView>
            {
                Status = UseCaseStatus.Error,
                Summary = "market ESI ingress cycle failed before any scope completed.",
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            };
        }

        if (cycle.Errors.Count > 0)
        {
            return new UseCaseResult<EsiMarketOrdersIngressCycleView>
            {
                Status = UseCaseStatus.Error,
                Summary = "market ESI ingress cycle completed with failures.",
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            };
        }

        return UseCaseResult<EsiMarketOrdersIngressCycleView>.Success(
            cycle,
            cycle.Status switch
            {
                "idle" => "market ESI ingress cycle found no due scopes.",
                _ => $"market ESI ingress cycle completed with {cycle.ExecutedScopes.Count} scope run(s)."
            },
            traceId,
            cycle.Notes);
    }

    private async Task<ScopePullResult> ExecuteScopePullAsync(
        EsiMarketOrdersIngressOptions options,
        EsiMarketOrdersIngressScopeOptions scope,
        EsiIngressState state,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _timeProvider.GetUtcNow();
        var payloadPath = ResolvePayloadPath(options, scope);
        var latestState = state.Scopes.TryGetValue(BuildScopeKey(scope), out var scopeState)
            ? scopeState
            : new EsiIngressScopeState();
        var notes = new List<string>();
        var errors = new List<string>();
        var existingPayloadPreserved = File.Exists(payloadPath);
        var routes = ResolveOrderSourceRoutes(scope);
        var scopeExpiresAtUtc = latestState.LastExpiresAtUtc;
        var throttle = new EsiThrottleCoordinator(_timeProvider, options);
        var httpClient = CreateHttpClient(options);

        if (routes.Count > 1)
        {
            foreach (var route in routes.Where(route => route.IsOverride))
            {
                notes.Add(
                    $"Type ids [{string.Join(", ", route.IncludedTypeIds)}] are fetched from source region {route.SourceRegionId} instead of canonical region {scope.RegionId}.");
            }
        }

        try
        {
            var routeResults = new List<RoutePullResult>(routes.Count);
            foreach (var route in routes)
            {
                var routeResult = await PullRouteAsync(
                    httpClient,
                    options,
                    scope,
                    route,
                    ifNoneMatch: routes.Count == 1 ? scopeState?.LastEtag : null,
                    allowNotModified: routes.Count == 1,
                    throttle,
                    cancellationToken);

                if (routeResult.Notes.Count > 0)
                {
                    notes.AddRange(routeResult.Notes);
                }

                if (routeResult.IsNotModified)
                {
                    var notModifiedAtUtc = _timeProvider.GetUtcNow();
                    scopeExpiresAtUtc = routeResult.Metadata.ExpiresAtUtc ?? latestState.LastExpiresAtUtc;
                    return new ScopePullResult(
                        new EsiMarketOrdersIngressScopeRunView
                        {
                            ScopeName = ResolveScopeName(scope),
                            RegionId = scope.RegionId,
                            OrderType = scope.OrderType,
                            PayloadPath = payloadPath,
                            Status = "not_modified",
                            StartedAtUtc = startedAtUtc,
                            CompletedAtUtc = notModifiedAtUtc,
                            NotModified = true,
                            ExistingPayloadPreserved = existingPayloadPreserved,
                            WaitedForFreshCache = routeResult.WaitedForFreshCache,
                            PageCount = 0,
                            RequestCount = routeResult.RequestCount,
                            OrderCount = 0,
                            SnapshotCount = 0,
                            SellOrderCount = 0,
                            BuyOrderCount = 0,
                            RetryCount = routeResult.RetryCount,
                            ETag = routeResult.Metadata.ETag,
                            ExpiresAtUtc = scopeExpiresAtUtc,
                            LastModifiedAtUtc = routeResult.Metadata.LastModifiedAtUtc,
                            RateLimit = throttle.CreateView(),
                            Notes = routeResult.Notes.Count > 0
                                ? routeResult.Notes
                                : ["ETag matched the latest successful payload; the previous payload remains current."]
                        },
                        latestState with
                        {
                            LastStatus = "not_modified",
                            LastCompletedAtUtc = notModifiedAtUtc,
                            LastSucceededAtUtc = notModifiedAtUtc,
                            LastNotModifiedAtUtc = notModifiedAtUtc,
                            LastError = null,
                            LastPayloadPath = payloadPath,
                            LastEtag = routeResult.Metadata.ETag ?? latestState.LastEtag,
                            LastModifiedAtUtc = routeResult.Metadata.LastModifiedAtUtc ?? latestState.LastModifiedAtUtc,
                            LastExpiresAtUtc = scopeExpiresAtUtc,
                            NextDueAtUtc = ComputeNextDueAt(notModifiedAtUtc, scope.IntervalMinutes, scopeExpiresAtUtc)
                        });
                }

                if (!routeResult.IsSuccess)
                {
                    errors.AddRange(routeResult.Errors);
                    var failedAtUtc = _timeProvider.GetUtcNow();
                    return CreateFailedScopeResult(
                        scope,
                        payloadPath,
                        startedAtUtc,
                        failedAtUtc,
                        existingPayloadPreserved,
                        routeResult.Metadata,
                        routeResults.Sum(result => result.PageCount) + routeResult.PageCount,
                        routeResults.Sum(result => result.RequestCount) + routeResult.RequestCount,
                        routeResults.Sum(result => result.RetryCount) + routeResult.RetryCount,
                        routeResults.Any(result => result.WaitedForFreshCache) || routeResult.WaitedForFreshCache,
                        throttle,
                        errors,
                        notes,
                        latestState);
                }

                routeResults.Add(routeResult);
            }

            var completedAtUtc = _timeProvider.GetUtcNow();
            var aggregation = AggregateOrders(routeResults.SelectMany(result => result.Orders), completedAtUtc);
            var totalPageCount = routeResults.Sum(result => result.PageCount);
            var totalRequestCount = routeResults.Sum(result => result.RequestCount);
            var totalRetryCount = routeResults.Sum(result => result.RetryCount);
            var waitedForFreshCache = routeResults.Any(result => result.WaitedForFreshCache);
            var canonicalMetadata = routeResults[0].Metadata;
            scopeExpiresAtUtc = ComputeScopeExpiresAtUtc(routeResults.Select(result => result.Metadata.ExpiresAtUtc)) ?? latestState.LastExpiresAtUtc;
            var archivePath = WritePayloadArtifacts(
                options,
                scope,
                payloadPath,
                startedAtUtc,
                completedAtUtc,
                canonicalMetadata,
                aggregation,
                waitedForFreshCache,
                throttle.CreateView(),
                totalPageCount,
                routeResults.Select(result => result.Trace).ToArray());

            notes.Add($"Wrote latest payload to '{payloadPath}'.");
            notes.Add($"Archived run payload at '{archivePath}'.");

            return new ScopePullResult(
                new EsiMarketOrdersIngressScopeRunView
                {
                    ScopeName = ResolveScopeName(scope),
                    RegionId = scope.RegionId,
                    OrderType = scope.OrderType,
                    PayloadPath = payloadPath,
                    Status = "completed",
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    NotModified = false,
                    ExistingPayloadPreserved = false,
                    WaitedForFreshCache = waitedForFreshCache,
                    PageCount = totalPageCount,
                    RequestCount = totalRequestCount,
                    OrderCount = aggregation.OrderCount,
                    SnapshotCount = aggregation.SnapshotCount,
                    SellOrderCount = aggregation.SellOrderCount,
                    BuyOrderCount = aggregation.BuyOrderCount,
                    RetryCount = totalRetryCount,
                    ETag = routes.Count == 1 ? canonicalMetadata.ETag : null,
                    ExpiresAtUtc = scopeExpiresAtUtc,
                    LastModifiedAtUtc = routes.Count == 1 ? canonicalMetadata.LastModifiedAtUtc : null,
                    RateLimit = throttle.CreateView(),
                    Notes = notes
                },
                latestState with
                {
                    LastStatus = "completed",
                    LastCompletedAtUtc = completedAtUtc,
                    LastSucceededAtUtc = completedAtUtc,
                    LastFailedAtUtc = latestState.LastFailedAtUtc,
                    LastError = null,
                    LastPayloadPath = payloadPath,
                    LastArchivePath = archivePath,
                    LastEtag = routes.Count == 1 ? canonicalMetadata.ETag : null,
                    LastModifiedAtUtc = routes.Count == 1 ? canonicalMetadata.LastModifiedAtUtc : latestState.LastModifiedAtUtc,
                    LastExpiresAtUtc = scopeExpiresAtUtc,
                    LastPageCount = totalPageCount,
                    LastOrderCount = aggregation.OrderCount,
                    LastSnapshotCount = aggregation.SnapshotCount,
                    LastRetryCount = totalRetryCount,
                    NextDueAtUtc = ComputeNextDueAt(completedAtUtc, scope.IntervalMinutes, scopeExpiresAtUtc)
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            var failedAtUtc = _timeProvider.GetUtcNow();
            return CreateFailedScopeResult(
                scope,
                payloadPath,
                startedAtUtc,
                failedAtUtc,
                existingPayloadPreserved,
                metadata: null,
                pageCount: 0,
                requestCount: 0,
                retryCount: 0,
                waitedForFreshCache: false,
                throttle,
                errors,
                notes,
                latestState);
        }
        finally
        {
            if (_httpClient is null)
            {
                httpClient.Dispose();
            }
        }
    }

    private HttpClient CreateHttpClient(EsiMarketOrdersIngressOptions options)
    {
        if (_httpClient is not null)
        {
            return _httpClient;
        }

        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute)
        };
    }

    private async Task<RoutePullResult> PullRouteAsync(
        HttpClient httpClient,
        EsiMarketOrdersIngressOptions options,
        EsiMarketOrdersIngressScopeOptions scope,
        OrderSourceRoute route,
        string? ifNoneMatch,
        bool allowNotModified,
        EsiThrottleCoordinator throttle,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var errors = new List<string>();
        var firstPage = await FetchPageAsync(
            httpClient,
            options,
            route.SourceRegionId,
            scope.OrderType,
            page: 1,
            ifNoneMatch: allowNotModified ? ifNoneMatch : null,
            throttle,
            cancellationToken);

        if (firstPage.IsNotModified)
        {
            return RoutePullResult.NotModified(
                route,
                firstPage.Metadata,
                firstPage.RequestCount,
                firstPage.RetryCount,
                waitedForFreshCache: false,
                notes.Count > 0
                    ? notes.ToArray()
                    : ["ETag matched the latest successful payload; the previous payload remains current."]);
        }

        if (!firstPage.IsSuccess)
        {
            errors.Add(PrefixRouteError(route, firstPage.ErrorMessage ?? "Initial ESI orders page fetch failed."));
            return RoutePullResult.Failed(
                route,
                firstPage.Metadata,
                firstPage.TotalPages,
                firstPage.RequestCount,
                firstPage.RetryCount,
                waitedForFreshCache: false,
                errors.ToArray(),
                notes.ToArray());
        }

        var firstMetadata = firstPage.Metadata;
        var waitedForFreshCache = false;
        if (firstPage.TotalPages > 1 &&
            firstMetadata.ExpiresAtUtc is { } expiresAtUtc &&
            expiresAtUtc - _timeProvider.GetUtcNow() <= TimeSpan.FromSeconds(options.NearExpiryDelayThresholdSeconds))
        {
            waitedForFreshCache = true;
            var delay = expiresAtUtc - _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(1);
            if (delay > TimeSpan.Zero)
            {
                notes.Add(PrefixRouteNote(route, $"First page was close to expiry; waited {delay.TotalSeconds:F0}s before refetching for a stable multipage snapshot."));
                await Task.Delay(delay, _timeProvider, cancellationToken);
            }

            firstPage = await FetchPageAsync(
                httpClient,
                options,
                route.SourceRegionId,
                scope.OrderType,
                page: 1,
                ifNoneMatch: allowNotModified ? ifNoneMatch : null,
                throttle,
                cancellationToken);

            if (firstPage.IsNotModified)
            {
                notes.Add(PrefixRouteNote(route, "Post-expiry refetch returned 304 Not Modified; reused the previous payload."));
                return RoutePullResult.NotModified(
                    route,
                    firstPage.Metadata,
                    firstPage.RequestCount,
                    firstPage.RetryCount,
                    waitedForFreshCache: true,
                    notes.ToArray());
            }

            if (!firstPage.IsSuccess)
            {
                errors.Add(PrefixRouteError(route, firstPage.ErrorMessage ?? "Refetch after expiry failed."));
                return RoutePullResult.Failed(
                    route,
                    firstPage.Metadata,
                    firstPage.TotalPages,
                    firstPage.RequestCount,
                    firstPage.RetryCount,
                    waitedForFreshCache,
                    errors.ToArray(),
                    notes.ToArray());
            }

            firstMetadata = firstPage.Metadata;
        }

        var allOrders = new ConcurrentBag<EsiPublicMarketOrder>();
        AddMatchingOrders(allOrders, firstPage.Orders, route);

        var pageResults = new ConcurrentBag<EsiPageFetchResult>();
        if (firstPage.TotalPages > 1)
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(2, firstPage.TotalPages - 1),
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = options.MaxConcurrentRequests
                },
                async (pageNumber, token) =>
                {
                    var pageResult = await FetchPageAsync(
                        httpClient,
                        options,
                        route.SourceRegionId,
                        scope.OrderType,
                        pageNumber,
                        ifNoneMatch: null,
                        throttle,
                        token);
                    pageResults.Add(pageResult);
                });
        }

        var pageList = pageResults.ToArray();
        var failedPage = pageList.FirstOrDefault(result => !result.IsSuccess);
        if (failedPage is not null)
        {
            errors.Add(PrefixRouteError(route, failedPage.ErrorMessage ?? "At least one ESI orders page failed."));
            return RoutePullResult.Failed(
                route,
                failedPage.Metadata,
                firstPage.TotalPages,
                firstPage.RequestCount + pageList.Sum(result => result.RequestCount),
                firstPage.RetryCount + pageList.Sum(result => result.RetryCount),
                waitedForFreshCache,
                errors.ToArray(),
                notes.ToArray());
        }

        foreach (var pageResult in pageList)
        {
            if (firstMetadata.LastModifiedAtUtc is not null &&
                pageResult.Metadata.LastModifiedAtUtc is not null &&
                pageResult.Metadata.LastModifiedAtUtc != firstMetadata.LastModifiedAtUtc)
            {
                errors.Add(PrefixRouteError(route, $"Page {pageResult.PageNumber} last-modified drifted from the first page."));
            }

            AddMatchingOrders(allOrders, pageResult.Orders, route);
        }

        if (errors.Count > 0)
        {
            return RoutePullResult.Failed(
                route,
                firstMetadata,
                firstPage.TotalPages,
                firstPage.RequestCount + pageList.Sum(result => result.RequestCount),
                firstPage.RetryCount + pageList.Sum(result => result.RetryCount),
                waitedForFreshCache,
                errors.ToArray(),
                notes.ToArray());
        }

        var orders = allOrders.ToArray();
        return RoutePullResult.Success(
            route,
            firstMetadata,
            firstPage.TotalPages,
            orders,
            firstPage.RequestCount + pageList.Sum(result => result.RequestCount),
            firstPage.RetryCount + pageList.Sum(result => result.RetryCount),
            waitedForFreshCache,
            notes.ToArray());
    }

    private async Task<EsiPageFetchResult> FetchPageAsync(
        HttpClient httpClient,
        EsiMarketOrdersIngressOptions options,
        long regionId,
        string orderType,
        int page,
        string? ifNoneMatch,
        EsiThrottleCoordinator throttle,
        CancellationToken cancellationToken)
    {
        var requestCount = 0;
        var retryCount = 0;

        for (var attempt = 0; attempt <= options.MaxRetriesPerRequest; attempt++)
        {
            await throttle.WaitForPermitAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildOrdersUri(regionId, options.Datasource, orderType, page));
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(options.CompatibilityDate))
            {
                request.Headers.TryAddWithoutValidation("x-compatibility-date", options.CompatibilityDate);
            }

            if (!string.IsNullOrWhiteSpace(ifNoneMatch))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            }

            HttpResponseMessage? response = null;
            try
            {
                requestCount++;
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var metadata = EsiResponseMetadata.FromResponse(response);
                throttle.Observe(metadata);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return EsiPageFetchResult.NotModified(page, metadata, requestCount, retryCount);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (RetryableStatusCodes.Contains(response.StatusCode) && attempt < options.MaxRetriesPerRequest)
                    {
                        retryCount++;
                        await DelayForRetryAsync(options, metadata, attempt, cancellationToken);
                        continue;
                    }

                    return EsiPageFetchResult.Failed(
                        page,
                        metadata,
                        $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                        requestCount,
                        retryCount);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var orders = document.RootElement.EnumerateArray()
                    .Select(ParseOrder)
                    .ToArray();
                var totalPages = metadata.TotalPages ?? 1;
                return EsiPageFetchResult.Success(page, totalPages, metadata, orders, requestCount, retryCount);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < options.MaxRetriesPerRequest)
            {
                retryCount++;
                await DelayForRetryAsync(options, null, attempt, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < options.MaxRetriesPerRequest)
            {
                retryCount++;
                await DelayForRetryAsync(options, null, attempt, cancellationToken);
                if (attempt == options.MaxRetriesPerRequest)
                {
                    return EsiPageFetchResult.Failed(page, EsiResponseMetadata.Empty(), ex.Message, requestCount, retryCount);
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        return EsiPageFetchResult.Failed(page, EsiResponseMetadata.Empty(), "ESI request exhausted retries.", requestCount, retryCount);
    }

    private static async Task DelayForRetryAsync(
        EsiMarketOrdersIngressOptions options,
        EsiResponseMetadata? metadata,
        int attempt,
        CancellationToken cancellationToken)
    {
        var retryAfterSeconds = metadata?.RetryAfterSeconds
            ?? metadata?.ErrorLimitResetSeconds
            ?? metadata?.RateLimitResetSeconds;
        var delay = retryAfterSeconds is > 0
            ? TimeSpan.FromSeconds(retryAfterSeconds.Value)
            : TimeSpan.FromMilliseconds(options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt));
        await Task.Delay(delay, cancellationToken);
    }

    private static Uri BuildOrdersUri(long regionId, string datasource, string orderType, int page)
    {
        var query = new Dictionary<string, string?>
        {
            ["datasource"] = datasource,
            ["order_type"] = orderType,
            ["page"] = page.ToString(CultureInfo.InvariantCulture)
        };
        var queryString = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
        return new Uri($"/markets/{regionId}/orders/?{queryString}", UriKind.Relative);
    }

    private static IReadOnlyList<OrderSourceRoute> ResolveOrderSourceRoutes(EsiMarketOrdersIngressScopeOptions scope)
    {
        var overrideTypeIdsByRegion = new Dictionary<long, HashSet<long>>();
        foreach (var overrideOptions in scope.OrderSourceOverrides)
        {
            if (overrideOptions.SourceRegionId == scope.RegionId)
            {
                continue;
            }

            var normalizedTypeIds = overrideOptions.TypeIds
                .Where(typeId => typeId > 0)
                .Distinct()
                .ToArray();
            if (normalizedTypeIds.Length == 0)
            {
                continue;
            }

            if (!overrideTypeIdsByRegion.TryGetValue(overrideOptions.SourceRegionId, out var typeIds))
            {
                typeIds = [];
                overrideTypeIdsByRegion[overrideOptions.SourceRegionId] = typeIds;
            }

            foreach (var typeId in normalizedTypeIds)
            {
                typeIds.Add(typeId);
            }
        }

        var routedAwayTypeIds = overrideTypeIdsByRegion.Values
            .SelectMany(typeIds => typeIds)
            .ToHashSet();

        var routes = new List<OrderSourceRoute>
        {
            new(
                scope.RegionId,
                IncludedTypeIds: Array.Empty<long>(),
                ExcludedTypeIds: routedAwayTypeIds.OrderBy(typeId => typeId).ToArray())
        };

        foreach (var entry in overrideTypeIdsByRegion.OrderBy(entry => entry.Key))
        {
            routes.Add(new(
                entry.Key,
                IncludedTypeIds: entry.Value.OrderBy(typeId => typeId).ToArray(),
                ExcludedTypeIds: Array.Empty<long>()));
        }

        return routes;
    }

    private static void AddMatchingOrders(
        ConcurrentBag<EsiPublicMarketOrder> destination,
        IEnumerable<EsiPublicMarketOrder> source,
        OrderSourceRoute route)
    {
        foreach (var order in source)
        {
            if (route.ShouldInclude(order.TypeId))
            {
                destination.Add(order);
            }
        }
    }

    private static string PrefixRouteError(OrderSourceRoute route, string message)
    {
        return $"source region {route.SourceRegionId}: {message}";
    }

    private static string PrefixRouteNote(OrderSourceRoute route, string message)
    {
        return $"source region {route.SourceRegionId}: {message}";
    }

    private static DateTimeOffset? ComputeScopeExpiresAtUtc(IEnumerable<DateTimeOffset?> expiresAtUtc)
    {
        return expiresAtUtc
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
    }

    private static EsiPublicMarketOrder ParseOrder(JsonElement element)
    {
        return new EsiPublicMarketOrder(
            OrderId: element.GetProperty("order_id").GetInt64(),
            TypeId: element.GetProperty("type_id").GetInt64(),
            LocationId: element.GetProperty("location_id").GetInt64(),
            IsBuyOrder: element.GetProperty("is_buy_order").GetBoolean(),
            Price: element.GetProperty("price").GetDecimal(),
            VolumeRemain: element.GetProperty("volume_remain").GetInt64(),
            MinVolume: element.TryGetProperty("min_volume", out var minVolumeElement) ? minVolumeElement.GetInt64() : 1,
            Range: element.TryGetProperty("range", out var rangeElement) ? rangeElement.GetString() ?? "region" : "region",
            IssuedAtUtc: DateTimeOffset.Parse(
                element.GetProperty("issued").GetString() ?? throw new InvalidOperationException("issued is required."),
                CultureInfo.InvariantCulture));
    }

    private static AggregatedOrderPayload AggregateOrders(IEnumerable<EsiPublicMarketOrder> orders, DateTimeOffset observedAtUtc)
    {
        var grouped = new Dictionary<(long TypeId, long LocationId), MutableOrderBook>();
        var orderCount = 0;
        var sellOrderCount = 0;
        var buyOrderCount = 0;

        foreach (var order in orders)
        {
            orderCount++;
            if (order.IsBuyOrder)
            {
                buyOrderCount++;
            }
            else
            {
                sellOrderCount++;
            }

            var key = (order.TypeId, order.LocationId);
            if (!grouped.TryGetValue(key, out var book))
            {
                book = new MutableOrderBook(order.TypeId, order.LocationId, observedAtUtc);
                grouped[key] = book;
            }

            book.Add(order);
        }

        return new AggregatedOrderPayload(
            grouped.Values
                .OrderBy(book => book.TypeId)
                .ThenBy(book => book.LocationId)
                .Select(book => book.Build())
                .ToArray(),
            orderCount,
            sellOrderCount,
            buyOrderCount);
    }

    private string WritePayloadArtifacts(
        EsiMarketOrdersIngressOptions options,
        EsiMarketOrdersIngressScopeOptions scope,
        string latestPayloadPath,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        EsiResponseMetadata metadata,
        AggregatedOrderPayload payload,
        bool waitedForFreshCache,
        EsiMarketOrdersIngressRateLimitView? rateLimit,
        int totalPageCount,
        IReadOnlyList<EsiPayloadSourceRouteTrace> sourceRoutes)
    {
        var outputDirectoryPath = Path.GetDirectoryName(latestPayloadPath)
            ?? throw new InvalidOperationException($"Could not resolve payload directory for '{latestPayloadPath}'.");
        Directory.CreateDirectory(outputDirectoryPath);

        var archiveDirectoryPath = Path.Combine(options.OutputDirectoryPath, "history");
        Directory.CreateDirectory(archiveDirectoryPath);
        var archiveFileName = $"{completedAtUtc:yyyyMMddTHHmmssZ}-region-{scope.RegionId}-{scope.OrderType}.orders-import.json";
        var archivePath = Path.Combine(archiveDirectoryPath, archiveFileName);

        WritePayloadJson(latestPayloadPath, options, scope, startedAtUtc, completedAtUtc, metadata, payload, waitedForFreshCache, rateLimit, totalPageCount, sourceRoutes);
        WritePayloadJson(archivePath, options, scope, startedAtUtc, completedAtUtc, metadata, payload, waitedForFreshCache, rateLimit, totalPageCount, sourceRoutes);
        return archivePath;
    }

    private void WritePayloadJson(
        string path,
        EsiMarketOrdersIngressOptions options,
        EsiMarketOrdersIngressScopeOptions scope,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        EsiResponseMetadata metadata,
        AggregatedOrderPayload payload,
        bool waitedForFreshCache,
        EsiMarketOrdersIngressRateLimitView? rateLimit,
        int totalPageCount,
        IReadOnlyList<EsiPayloadSourceRouteTrace> sourceRoutes)
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
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("source", $"esi-public-region-orders:{scope.RegionId}:{scope.OrderType}");
                writer.WriteStartObject("ingress_trace");
                writer.WriteString("service_name", options.ServiceName);
                writer.WriteString("base_url", options.BaseUrl);
                writer.WriteString("datasource", options.Datasource);
                writer.WriteString("scope_name", ResolveScopeName(scope));
                writer.WriteNumber("region_id", scope.RegionId);
                writer.WriteString("order_type", scope.OrderType);
                writer.WriteString("started_at_utc", FormatInstant(startedAtUtc));
                writer.WriteString("completed_at_utc", FormatInstant(completedAtUtc));
                if (metadata.ExpiresAtUtc is not null)
                {
                    writer.WriteString("expires_at_utc", FormatInstant(metadata.ExpiresAtUtc.Value));
                }

                if (metadata.LastModifiedAtUtc is not null)
                {
                    writer.WriteString("last_modified_at_utc", FormatInstant(metadata.LastModifiedAtUtc.Value));
                }

                if (!string.IsNullOrWhiteSpace(metadata.ETag))
                {
                    writer.WriteString("etag", metadata.ETag);
                }

                writer.WriteNumber("page_count", totalPageCount);
                writer.WriteNumber("order_count", payload.OrderCount);
                writer.WriteNumber("snapshot_count", payload.SnapshotCount);
                writer.WriteNumber("sell_order_count", payload.SellOrderCount);
                writer.WriteNumber("buy_order_count", payload.BuyOrderCount);
                writer.WriteBoolean("waited_for_fresh_cache", waitedForFreshCache);
                writer.WriteString("payload_sha256", ComputePayloadSha256(payload));
                writer.WritePropertyName("source_routes");
                writer.WriteStartArray();
                foreach (var sourceRoute in sourceRoutes)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("source_region_id", sourceRoute.SourceRegionId);
                    writer.WriteString("route_kind", sourceRoute.RouteKind);
                    writer.WriteNumber("page_count", sourceRoute.PageCount);
                    writer.WriteNumber("request_count", sourceRoute.RequestCount);
                    writer.WriteNumber("order_count", sourceRoute.OrderCount);
                    if (!string.IsNullOrWhiteSpace(sourceRoute.ETag))
                    {
                        writer.WriteString("etag", sourceRoute.ETag);
                    }

                    if (sourceRoute.ExpiresAtUtc is not null)
                    {
                        writer.WriteString("expires_at_utc", FormatInstant(sourceRoute.ExpiresAtUtc.Value));
                    }

                    if (sourceRoute.LastModifiedAtUtc is not null)
                    {
                        writer.WriteString("last_modified_at_utc", FormatInstant(sourceRoute.LastModifiedAtUtc.Value));
                    }

                    writer.WritePropertyName("included_type_ids");
                    writer.WriteStartArray();
                    foreach (var typeId in sourceRoute.IncludedTypeIds)
                    {
                        writer.WriteNumberValue(typeId);
                    }

                    writer.WriteEndArray();
                    writer.WritePropertyName("excluded_type_ids");
                    writer.WriteStartArray();
                    foreach (var typeId in sourceRoute.ExcludedTypeIds)
                    {
                        writer.WriteNumberValue(typeId);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                if (rateLimit is not null)
                {
                    writer.WriteStartObject("rate_limit");
                    if (rateLimit.ErrorLimitRemain is not null)
                    {
                        writer.WriteNumber("error_limit_remain", rateLimit.ErrorLimitRemain.Value);
                    }

                    if (rateLimit.ErrorLimitResetSeconds is not null)
                    {
                        writer.WriteNumber("error_limit_reset_seconds", rateLimit.ErrorLimitResetSeconds.Value);
                    }

                    if (rateLimit.RateLimitRemain is not null)
                    {
                        writer.WriteNumber("rate_limit_remain", rateLimit.RateLimitRemain.Value);
                    }

                    if (rateLimit.RateLimitResetSeconds is not null)
                    {
                        writer.WriteNumber("rate_limit_reset_seconds", rateLimit.RateLimitResetSeconds.Value);
                    }

                    if (!string.IsNullOrWhiteSpace(rateLimit.RateLimitGroup))
                    {
                        writer.WriteString("rate_limit_group", rateLimit.RateLimitGroup);
                    }

                    if (rateLimit.PauseUntilUtc is not null)
                    {
                        writer.WriteString("pause_until_utc", FormatInstant(rateLimit.PauseUntilUtc.Value));
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WritePropertyName("order_books");
                writer.WriteStartArray();
                foreach (var snapshot in payload.Snapshots)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(snapshot.TypeId);
                    writer.WriteNumberValue(snapshot.LocationId);
                    writer.WriteStringValue(FormatInstant(snapshot.ObservedAtUtc));
                    WriteOrderRows(writer, snapshot.SellOrders);
                    WriteOrderRows(writer, snapshot.BuyOrders);
                    writer.WriteEndArray();
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

    private static void WriteOrderRows(Utf8JsonWriter writer, IReadOnlyList<EsiPayloadOrderRow> rows)
    {
        writer.WriteStartArray();
        foreach (var row in rows)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(row.OrderId);
            writer.WriteNumberValue(row.UnitPrice);
            writer.WriteNumberValue(row.RemainingVolume);
            writer.WriteNumberValue(row.MinVolume);
            writer.WriteStringValue(row.Range);
            writer.WriteStringValue(FormatInstant(row.IssuedAtUtc));
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static string ComputePayloadSha256(AggregatedOrderPayload payload)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var snapshot in payload.Snapshots)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(snapshot.TypeId);
                writer.WriteNumberValue(snapshot.LocationId);
                writer.WriteStringValue(FormatInstant(snapshot.ObservedAtUtc));
                WriteOrderRows(writer, snapshot.SellOrders);
                WriteOrderRows(writer, snapshot.BuyOrders);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static string FormatInstant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static ScopePullResult CreateFailedScopeResult(
        EsiMarketOrdersIngressScopeOptions scope,
        string payloadPath,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        bool existingPayloadPreserved,
        EsiResponseMetadata? metadata,
        int pageCount,
        int requestCount,
        int retryCount,
        bool waitedForFreshCache,
        EsiThrottleCoordinator throttle,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> notes,
        EsiIngressScopeState latestState)
    {
        var view = new EsiMarketOrdersIngressScopeRunView
        {
            ScopeName = ResolveScopeName(scope),
            RegionId = scope.RegionId,
            OrderType = scope.OrderType,
            PayloadPath = payloadPath,
            Status = "failed",
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ExistingPayloadPreserved = existingPayloadPreserved,
            WaitedForFreshCache = waitedForFreshCache,
            PageCount = pageCount,
            RequestCount = requestCount,
            RetryCount = retryCount,
            ETag = metadata?.ETag,
            ExpiresAtUtc = metadata?.ExpiresAtUtc,
            LastModifiedAtUtc = metadata?.LastModifiedAtUtc,
            RateLimit = throttle.CreateView(),
            Errors = errors,
            Notes = notes
        };

        var state = latestState with
        {
            LastStatus = "failed",
            LastCompletedAtUtc = completedAtUtc,
            LastFailedAtUtc = completedAtUtc,
            LastError = string.Join(" | ", errors),
            LastPayloadPath = payloadPath,
            LastEtag = metadata?.ETag ?? latestState.LastEtag,
            LastModifiedAtUtc = metadata?.LastModifiedAtUtc ?? latestState.LastModifiedAtUtc,
            LastExpiresAtUtc = metadata?.ExpiresAtUtc ?? latestState.LastExpiresAtUtc,
            LastRetryCount = retryCount,
            NextDueAtUtc = completedAtUtc
        };

        return new ScopePullResult(view, state);
    }

    private static IReadOnlyList<EsiMarketOrdersIngressPlannedScopeView> PlanScopes(
        EsiMarketOrdersIngressOptions options,
        EsiIngressState state,
        DateTimeOffset plannedAtUtc)
    {
        return options.Scopes
            .Select(scope =>
            {
                var scopeKey = BuildScopeKey(scope);
                state.Scopes.TryGetValue(scopeKey, out var scopeState);
                var nextDueAtUtc = scopeState?.NextDueAtUtc;
                var isDue = scope.Enabled && (!nextDueAtUtc.HasValue || plannedAtUtc >= nextDueAtUtc.Value);
                return new EsiMarketOrdersIngressPlannedScopeView
                {
                    ScopeName = ResolveScopeName(scope),
                    RegionId = scope.RegionId,
                    OrderType = scope.OrderType,
                    Enabled = scope.Enabled,
                    IsDue = isDue,
                    IntervalMinutes = scope.IntervalMinutes,
                    PayloadPath = ResolvePayloadPath(options, scope),
                    Decision = !scope.Enabled
                        ? "disabled"
                        : isDue
                            ? "due_now"
                            : "waiting_for_next_due",
                    LastStatus = scopeState?.LastStatus,
                    LastSucceededAtUtc = scopeState?.LastSucceededAtUtc,
                    LastFailedAtUtc = scopeState?.LastFailedAtUtc,
                    NextDueAtUtc = nextDueAtUtc
                };
            })
            .ToArray();
    }

    private static string DetermineCycleStatus(
        IReadOnlyList<EsiMarketOrdersIngressPlannedScopeView> plannedScopes,
        IReadOnlyList<EsiMarketOrdersIngressScopeRunView> executedScopes)
    {
        if (plannedScopes.All(scope => !scope.IsDue))
        {
            return "idle";
        }

        if (executedScopes.Any(scope => string.Equals(scope.Status, "failed", StringComparison.Ordinal)))
        {
            return executedScopes.Any(scope => string.Equals(scope.Status, "completed", StringComparison.Ordinal) || string.Equals(scope.Status, "not_modified", StringComparison.Ordinal))
                ? "completed_with_failures"
                : "failed";
        }

        return "completed";
    }

    private static EsiMarketOrdersIngressArtifactPathsView ResolveArtifactPaths(EsiMarketOrdersIngressOptions options, string cycleId)
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
        return new EsiMarketOrdersIngressArtifactPathsView
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

    private static string ResolvePayloadPath(EsiMarketOrdersIngressOptions options, EsiMarketOrdersIngressScopeOptions scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.PayloadPath))
        {
            return Path.GetFullPath(scope.PayloadPath);
        }

        return Path.Combine(
            Path.GetFullPath(options.OutputDirectoryPath),
            $"region-{scope.RegionId}-{scope.OrderType}.orders-import.json");
    }

    private static string ResolveScopeName(EsiMarketOrdersIngressScopeOptions scope)
    {
        return string.IsNullOrWhiteSpace(scope.ScopeName)
            ? $"region-{scope.RegionId}-{scope.OrderType}"
            : scope.ScopeName;
    }

    private static string BuildScopeKey(EsiMarketOrdersIngressScopeOptions scope)
    {
        return $"{ResolveScopeName(scope)}:{scope.RegionId}:{scope.OrderType}:{BuildOrderSourceOverrideFingerprint(scope)}";
    }

    private static string BuildOrderSourceOverrideFingerprint(EsiMarketOrdersIngressScopeOptions scope)
    {
        var routes = ResolveOrderSourceRoutes(scope);
        if (routes.Count == 1)
        {
            return "default";
        }

        return string.Join(
            ";",
            routes.Select(route =>
            {
                var included = route.IncludedTypeIds.Count == 0
                    ? "-"
                    : string.Join(",", route.IncludedTypeIds);
                var excluded = route.ExcludedTypeIds.Count == 0
                    ? "-"
                    : string.Join(",", route.ExcludedTypeIds);
                return $"{route.SourceRegionId}:{included}:{excluded}";
            }));
    }

    private static DateTimeOffset ComputeNextDueAt(DateTimeOffset completedAtUtc, int intervalMinutes, DateTimeOffset? expiresAtUtc)
    {
        var intervalDueAt = completedAtUtc.AddMinutes(Math.Max(intervalMinutes, 1));
        if (expiresAtUtc is null)
        {
            return intervalDueAt;
        }

        return expiresAtUtc.Value > intervalDueAt ? expiresAtUtc.Value : intervalDueAt;
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

    private sealed record OrderSourceRoute(
        long SourceRegionId,
        IReadOnlyList<long> IncludedTypeIds,
        IReadOnlyList<long> ExcludedTypeIds)
    {
        public bool IsOverride => IncludedTypeIds.Count > 0;

        public bool ShouldInclude(long typeId)
        {
            if (IncludedTypeIds.Count > 0)
            {
                return IncludedTypeIds.Contains(typeId);
            }

            return !ExcludedTypeIds.Contains(typeId);
        }
    }

    private sealed record ScopePullResult(EsiMarketOrdersIngressScopeRunView View, EsiIngressScopeState State);

    private sealed record RoutePullResult(
        OrderSourceRoute Route,
        bool IsSuccess,
        bool IsNotModified,
        EsiResponseMetadata Metadata,
        IReadOnlyList<EsiPublicMarketOrder> Orders,
        int PageCount,
        int RequestCount,
        int RetryCount,
        bool WaitedForFreshCache,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes,
        EsiPayloadSourceRouteTrace Trace)
    {
        public static RoutePullResult Success(
            OrderSourceRoute route,
            EsiResponseMetadata metadata,
            int pageCount,
            IReadOnlyList<EsiPublicMarketOrder> orders,
            int requestCount,
            int retryCount,
            bool waitedForFreshCache,
            IReadOnlyList<string> notes)
            => new(
                route,
                true,
                false,
                metadata,
                orders,
                pageCount,
                requestCount,
                retryCount,
                waitedForFreshCache,
                Array.Empty<string>(),
                notes,
                BuildTrace(route, metadata, pageCount, requestCount, orders.Count));

        public static RoutePullResult NotModified(
            OrderSourceRoute route,
            EsiResponseMetadata metadata,
            int requestCount,
            int retryCount,
            bool waitedForFreshCache,
            IReadOnlyList<string> notes)
            => new(
                route,
                true,
                true,
                metadata,
                Array.Empty<EsiPublicMarketOrder>(),
                0,
                requestCount,
                retryCount,
                waitedForFreshCache,
                Array.Empty<string>(),
                notes,
                BuildTrace(route, metadata, 0, requestCount, 0));

        public static RoutePullResult Failed(
            OrderSourceRoute route,
            EsiResponseMetadata metadata,
            int pageCount,
            int requestCount,
            int retryCount,
            bool waitedForFreshCache,
            IReadOnlyList<string> errors,
            IReadOnlyList<string> notes)
            => new(
                route,
                false,
                false,
                metadata,
                Array.Empty<EsiPublicMarketOrder>(),
                pageCount,
                requestCount,
                retryCount,
                waitedForFreshCache,
                errors,
                notes,
                BuildTrace(route, metadata, pageCount, requestCount, 0));

        private static EsiPayloadSourceRouteTrace BuildTrace(
            OrderSourceRoute route,
            EsiResponseMetadata metadata,
            int pageCount,
            int requestCount,
            int orderCount)
        {
            return new EsiPayloadSourceRouteTrace(
                route.SourceRegionId,
                route.IsOverride ? "type_override" : "canonical_region",
                route.IncludedTypeIds,
                route.ExcludedTypeIds,
                pageCount,
                requestCount,
                orderCount,
                metadata.ETag,
                metadata.ExpiresAtUtc,
                metadata.LastModifiedAtUtc);
        }
    }

    private sealed record EsiPublicMarketOrder(
        long OrderId,
        long TypeId,
        long LocationId,
        bool IsBuyOrder,
        decimal Price,
        long VolumeRemain,
        long MinVolume,
        string Range,
        DateTimeOffset IssuedAtUtc);

    private sealed record EsiPayloadOrderRow(
        long OrderId,
        decimal UnitPrice,
        long RemainingVolume,
        long MinVolume,
        string Range,
        DateTimeOffset IssuedAtUtc);

    private sealed record EsiPayloadOrderSnapshot(
        long TypeId,
        long LocationId,
        DateTimeOffset ObservedAtUtc,
        IReadOnlyList<EsiPayloadOrderRow> SellOrders,
        IReadOnlyList<EsiPayloadOrderRow> BuyOrders);

    private sealed record AggregatedOrderPayload(
        IReadOnlyList<EsiPayloadOrderSnapshot> Snapshots,
        int OrderCount,
        int SellOrderCount,
        int BuyOrderCount)
    {
        public int SnapshotCount => Snapshots.Count;
    }

    private sealed record EsiPayloadSourceRouteTrace(
        long SourceRegionId,
        string RouteKind,
        IReadOnlyList<long> IncludedTypeIds,
        IReadOnlyList<long> ExcludedTypeIds,
        int PageCount,
        int RequestCount,
        int OrderCount,
        string? ETag,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? LastModifiedAtUtc);

    private sealed class MutableOrderBook
    {
        private readonly List<EsiPayloadOrderRow> _sellOrders = [];
        private readonly List<EsiPayloadOrderRow> _buyOrders = [];

        public MutableOrderBook(long typeId, long locationId, DateTimeOffset observedAtUtc)
        {
            TypeId = typeId;
            LocationId = locationId;
            ObservedAtUtc = observedAtUtc;
        }

        public long TypeId { get; }

        public long LocationId { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        public void Add(EsiPublicMarketOrder order)
        {
            var row = new EsiPayloadOrderRow(
                order.OrderId,
                order.Price,
                order.VolumeRemain,
                order.MinVolume,
                order.Range,
                order.IssuedAtUtc);

            if (order.IsBuyOrder)
            {
                _buyOrders.Add(row);
            }
            else
            {
                _sellOrders.Add(row);
            }
        }

        public EsiPayloadOrderSnapshot Build()
        {
            return new EsiPayloadOrderSnapshot(
                TypeId,
                LocationId,
                ObservedAtUtc,
                _sellOrders.OrderBy(row => row.UnitPrice).ThenBy(row => row.IssuedAtUtc).ToArray(),
                _buyOrders.OrderByDescending(row => row.UnitPrice).ThenBy(row => row.IssuedAtUtc).ToArray());
        }
    }

    private sealed record EsiPageFetchResult(
        int PageNumber,
        bool IsSuccess,
        bool IsNotModified,
        int TotalPages,
        EsiResponseMetadata Metadata,
        IReadOnlyList<EsiPublicMarketOrder> Orders,
        string? ErrorMessage,
        int RequestCount,
        int RetryCount)
    {
        public static EsiPageFetchResult Success(int pageNumber, int totalPages, EsiResponseMetadata metadata, IReadOnlyList<EsiPublicMarketOrder> orders, int requestCount, int retryCount)
            => new(pageNumber, true, false, totalPages, metadata, orders, null, requestCount, retryCount);

        public static EsiPageFetchResult NotModified(int pageNumber, EsiResponseMetadata metadata, int requestCount, int retryCount)
            => new(pageNumber, true, true, 0, metadata, Array.Empty<EsiPublicMarketOrder>(), null, requestCount, retryCount);

        public static EsiPageFetchResult Failed(int pageNumber, EsiResponseMetadata metadata, string errorMessage, int requestCount, int retryCount)
            => new(pageNumber, false, false, metadata.TotalPages ?? 0, metadata, Array.Empty<EsiPublicMarketOrder>(), errorMessage, requestCount, retryCount);
    }

    private sealed record EsiResponseMetadata
    {
        public int? TotalPages { get; init; }

        public string? ETag { get; init; }

        public DateTimeOffset? ExpiresAtUtc { get; init; }

        public DateTimeOffset? LastModifiedAtUtc { get; init; }

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
                TotalPages = TryParseInt(response.Headers, "x-pages"),
                ETag = response.Headers.ETag?.Tag ?? TryGetValue(response.Headers, "etag"),
                ExpiresAtUtc = TryParseHttpDate(TryGetValue(response.Headers, "expires")),
                LastModifiedAtUtc = response.Content.Headers.LastModified ?? TryParseHttpDate(TryGetValue(response.Headers, "last-modified")),
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

        private static DateTimeOffset? TryParseHttpDate(string? value)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
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
        private readonly EsiMarketOrdersIngressOptions _options;
        private readonly object _sync = new();
        private DateTimeOffset? _pauseUntilUtc;
        private int? _errorLimitRemain;
        private int? _errorLimitResetSeconds;
        private int? _rateLimitRemain;
        private int? _rateLimitResetSeconds;
        private string? _rateLimitGroup;

        public EsiThrottleCoordinator(TimeProvider timeProvider, EsiMarketOrdersIngressOptions options)
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

        public EsiMarketOrdersIngressRateLimitView CreateView()
        {
            lock (_sync)
            {
                return new EsiMarketOrdersIngressRateLimitView
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

    private sealed record EsiIngressState(IReadOnlyDictionary<string, EsiIngressScopeState> Scopes)
    {
        public static EsiIngressState Empty { get; } = new(new Dictionary<string, EsiIngressScopeState>(StringComparer.Ordinal));
    }

    private sealed record EsiIngressScopeState
    {
        public string? LastStatus { get; init; }
        public DateTimeOffset? LastCompletedAtUtc { get; init; }
        public DateTimeOffset? LastSucceededAtUtc { get; init; }
        public DateTimeOffset? LastFailedAtUtc { get; init; }
        public DateTimeOffset? LastNotModifiedAtUtc { get; init; }
        public DateTimeOffset? NextDueAtUtc { get; init; }
        public string? LastError { get; init; }
        public string? LastPayloadPath { get; init; }
        public string? LastArchivePath { get; init; }
        public string? LastEtag { get; init; }
        public DateTimeOffset? LastModifiedAtUtc { get; init; }
        public DateTimeOffset? LastExpiresAtUtc { get; init; }
        public int LastPageCount { get; init; }
        public int LastOrderCount { get; init; }
        public int LastSnapshotCount { get; init; }
        public int LastRetryCount { get; init; }
    }

    private static class EsiIngressStateStore
    {
        public static EsiIngressState Load(string path)
        {
            if (!File.Exists(path))
            {
                return EsiIngressState.Empty;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return EsiIngressState.Empty;
            }

            var payload = JsonSerializer.Deserialize<EsiIngressStateDocument>(text, JsonOptions);
            if (payload?.Scopes is null)
            {
                return EsiIngressState.Empty;
            }

            return new EsiIngressState(payload.Scopes.ToDictionary(
                entry => entry.Key,
                entry => entry.Value ?? new EsiIngressScopeState(),
                StringComparer.Ordinal));
        }

        public static void Save(string path, EsiIngressState state)
        {
            var document = new EsiIngressStateDocument
            {
                Scopes = state.Scopes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
            };
            WriteJsonAtomically(path, document);
        }

        public static EsiIngressState ApplyScopeResult(EsiIngressState current, EsiMarketOrdersIngressScopeOptions scope, EsiIngressScopeState scopeState)
        {
            var scopes = current.Scopes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            scopes[BuildScopeKey(scope)] = scopeState;
            return new EsiIngressState(scopes);
        }
    }

    private sealed record EsiIngressStateDocument
    {
        public Dictionary<string, EsiIngressScopeState> Scopes { get; init; } = new(StringComparer.Ordinal);
    }
}
