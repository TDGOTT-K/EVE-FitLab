using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using EdenOS.Application.Accounts;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class EsiAuthenticatedStructureOrdersRunner
{
    private static readonly string[] RequiredScopes =
    [
        "esi-markets.structure_markets.v1",
        "esi-universe.read_structures.v1"
    ];

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
    private readonly IWorkspaceBoundMarketAccessService _workspaceBoundMarketAccessService;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient? _httpClient;

    public EsiAuthenticatedStructureOrdersRunner(
        IEsiCredentialService esiCredentialService,
        IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        _esiCredentialService = esiCredentialService;
        _workspaceBoundMarketAccessService = workspaceBoundMarketAccessService;
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UseCaseResult<EsiAuthenticatedStructureOrdersPullView>> PullAsync(
        EsiAuthenticatedStructureOrdersPullRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var traceId = $"market.esi.authenticated_structure_orders:{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return UseCaseResult<EsiAuthenticatedStructureOrdersPullView>.Failure(
                UseCaseStatus.InvalidInput,
                "WorkspaceId is required.",
                traceId,
                ["workspace_id must not be empty."]);
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectoryPath))
        {
            return UseCaseResult<EsiAuthenticatedStructureOrdersPullView>.Failure(
                UseCaseStatus.InvalidInput,
                "OutputDirectoryPath is required.",
                traceId,
                ["output_directory_path must not be empty."]);
        }

        var accessCatalogResult = _workspaceBoundMarketAccessService.GetStructureAccessCatalog(request.WorkspaceId);
        if (!accessCatalogResult.IsSuccess)
        {
            return UseCaseResult<EsiAuthenticatedStructureOrdersPullView>.Failure(
                accessCatalogResult.Status,
                accessCatalogResult.Summary,
                traceId,
                accessCatalogResult.Errors);
        }

        var selectedStructures = accessCatalogResult.Data!.Structures
            .Where(structure => request.StructureIds.Count == 0 || request.StructureIds.Contains(structure.StructureId))
            .OrderBy(structure => structure.StructureId)
            .ToArray();
        if (selectedStructures.Length == 0)
        {
            return UseCaseResult<EsiAuthenticatedStructureOrdersPullView>.Failure(
                UseCaseStatus.NotFound,
                "No accessible structure matched the request.",
                traceId,
                request.StructureIds.Count == 0
                    ? ["The workspace does not currently expose any authenticated structure grants."]
                    : ["None of the requested structure ids is currently authorized for this workspace."]);
        }

        var startedAtUtc = _timeProvider.GetUtcNow();
        var outputDirectoryPath = Path.GetFullPath(request.OutputDirectoryPath);
        var marketFactsDirectoryPath = Path.GetFullPath(request.MarketFactsDirectoryPath ?? outputDirectoryPath);
        Directory.CreateDirectory(outputDirectoryPath);
        MarketFactsDataPaths.EnsureLayoutInDirectory(marketFactsDirectoryPath);

        var structureDirectoryStore = new StructureDirectoryOverlayStore(
            MarketFactsDataPaths.GetStructureDirectoryOverlayPath(marketFactsDirectoryPath));
        var existingStructureDirectory = structureDirectoryStore.Load();
        var notes = new List<string>();
        var errors = new List<string>();
        var structureViews = new List<EsiAuthenticatedStructureOrdersStructureView>();
        var structureDirectoryEntries = new List<StructureDirectoryEntry>();
        var aggregatedOrders = new List<EsiStructureOrder>();

        var httpClient = CreateHttpClient(request);
        try
        {
            foreach (var structure in selectedStructures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var structureResult = await PullStructureAsync(
                    httpClient,
                    request,
                    structure,
                    existingStructureDirectory,
                    cancellationToken);
                structureViews.Add(structureResult.View);

                if (structureResult.Success is null)
                {
                    errors.AddRange(structureResult.View.Errors);
                    continue;
                }

                aggregatedOrders.AddRange(structureResult.Success.Orders);
                structureDirectoryEntries.Add(structureResult.Success.StructureDirectoryEntry);
                existingStructureDirectory = new Dictionary<long, StructureDirectoryEntry>(existingStructureDirectory)
                {
                    [structureResult.Success.StructureDirectoryEntry.StructureId] = structureResult.Success.StructureDirectoryEntry
                };

                if (structureResult.View.Notes.Count > 0)
                {
                    notes.AddRange(structureResult.View.Notes);
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

        if (aggregatedOrders.Count == 0)
        {
            var failedView = BuildPullView(
                request,
                marketFactsDirectoryPath,
                structureDirectoryStore.Path,
                payloadPath: Path.Combine(outputDirectoryPath, "latest", $"workspace-{request.WorkspaceId}.authenticated-structure.orders-import.json"),
                archivePath: Path.Combine(outputDirectoryPath, "history", $"workspace-{request.WorkspaceId}.authenticated-structure.orders-import.json"),
                status: "failed",
                startedAtUtc,
                completedAtUtc: _timeProvider.GetUtcNow(),
                source: $"esi-authenticated-structure-orders:workspace:{request.WorkspaceId}",
                orderCount: 0,
                snapshotCount: 0,
                directoryWriteResult: new StructureDirectoryOverlayWriteResult(structureDirectoryStore.Path, 0, existingStructureDirectory.Count),
                importRun: null,
                structures: structureViews,
                notes,
                errors);

            return new UseCaseResult<EsiAuthenticatedStructureOrdersPullView>
            {
                Status = UseCaseStatus.DependencyUnavailable,
                Summary = "Authenticated structure order pull did not produce any payload.",
                Data = failedView,
                TraceId = traceId,
                Errors = errors.Count > 0 ? errors : ["No authenticated structure order payload could be produced."],
                Warnings = notes
            };
        }

        var aggregatedPayload = AggregateOrders(aggregatedOrders, startedAtUtc);
        var payloadPaths = WritePayloadArtifacts(outputDirectoryPath, request, aggregatedPayload, structureViews, startedAtUtc, _timeProvider.GetUtcNow());
        var directoryWriteResult = structureDirectoryStore.Upsert(structureDirectoryEntries);

        MarketOrdersRuntimeRunView? importRun = null;
        if (request.ImportIntoMarketOrdersRuntime)
        {
            var importResult = new LocalMarketOrdersRuntime(marketFactsDirectoryPath).Import(new MarketOrdersRuntimeRunRequest
            {
                PayloadPath = payloadPaths.LatestPayloadPath,
                MarketFactsDirectoryPath = marketFactsDirectoryPath,
                RequestedBy = request.RequestedBy,
                Cursor = startedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmss'Z'", CultureInfo.InvariantCulture)
            });

            if (!importResult.IsSuccess)
            {
                var failedView = BuildPullView(
                    request,
                    marketFactsDirectoryPath,
                    directoryWriteResult.Path,
                    payloadPaths.LatestPayloadPath,
                    payloadPaths.ArchivePayloadPath,
                    status: "failed",
                    startedAtUtc,
                    _timeProvider.GetUtcNow(),
                    $"esi-authenticated-structure-orders:workspace:{request.WorkspaceId}",
                    aggregatedPayload.OrderCount,
                    aggregatedPayload.SnapshotCount,
                    directoryWriteResult,
                    importResult.Data,
                    structureViews,
                    notes,
                    [.. errors, .. importResult.Errors]);

                return new UseCaseResult<EsiAuthenticatedStructureOrdersPullView>
                {
                    Status = UseCaseStatus.Error,
                    Summary = "Authenticated structure orders were fetched but local import failed.",
                    Data = failedView,
                    TraceId = traceId,
                    Errors = importResult.Errors,
                    Warnings = [.. notes, .. importResult.Warnings]
                };
            }

            importRun = importResult.Data;
            notes.Add($"Imported authenticated structure payload into '{marketFactsDirectoryPath}'.");
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var finalStatus = errors.Count == 0 ? "completed" : "partial_failure";
        var view = BuildPullView(
            request,
            marketFactsDirectoryPath,
            directoryWriteResult.Path,
            payloadPaths.LatestPayloadPath,
            payloadPaths.ArchivePayloadPath,
            finalStatus,
            startedAtUtc,
            completedAtUtc,
            $"esi-authenticated-structure-orders:workspace:{request.WorkspaceId}",
            aggregatedPayload.OrderCount,
            aggregatedPayload.SnapshotCount,
            directoryWriteResult,
            importRun,
            structureViews,
            notes,
            errors);

        return UseCaseResult<EsiAuthenticatedStructureOrdersPullView>.Success(
            view,
            errors.Count == 0
                ? $"Pulled authenticated market orders for {view.PulledStructureCount} structure(s)."
                : $"Pulled authenticated market orders with {view.FailedStructureCount} structure failure(s).",
            traceId,
            warnings: notes.Concat(errors).ToArray());
    }

    private HttpClient CreateHttpClient(EsiAuthenticatedStructureOrdersPullRequest request)
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

    private async Task<StructurePullResult> PullStructureAsync(
        HttpClient httpClient,
        EsiAuthenticatedStructureOrdersPullRequest request,
        WorkspaceStructureAccessBinding structureAccess,
        IReadOnlyDictionary<long, StructureDirectoryEntry> existingStructureDirectory,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var errors = new List<string>();
        var credential = await ResolveCredentialAsync(
            request.WorkspaceId,
            structureAccess,
            request.MinimumTokenValiditySeconds,
            cancellationToken);
        if (!credential.IsSuccess)
        {
            var failedView = new EsiAuthenticatedStructureOrdersStructureView
            {
                StructureId = structureAccess.StructureId,
                Status = "failed",
                Errors = credential.Errors,
                Notes = credential.Notes
            };
            return new StructurePullResult(failedView, null);
        }

        notes.AddRange(credential.Notes);
        var structureMetadata = await ResolveStructureDirectoryEntryAsync(
            httpClient,
            request,
            structureAccess.StructureId,
            credential.Data!.Envelope,
            existingStructureDirectory,
            cancellationToken);
        if (!structureMetadata.IsSuccess)
        {
            errors.AddRange(structureMetadata.Errors);
            var failedView = new EsiAuthenticatedStructureOrdersStructureView
            {
                StructureId = structureAccess.StructureId,
                Status = "failed",
                SelectedEsiCharacterId = credential.Data.EsiCharacterId,
                Errors = errors,
                Notes = [.. notes, .. structureMetadata.Notes]
            };
            return new StructurePullResult(failedView, null);
        }

        notes.AddRange(structureMetadata.Notes);
        var ordersResult = await FetchStructureOrdersAsync(
            httpClient,
            request,
            structureAccess.StructureId,
            credential.Data.Envelope.AccessToken,
            cancellationToken);
        if (!ordersResult.IsSuccess)
        {
            errors.AddRange(ordersResult.Errors);
            var failedView = new EsiAuthenticatedStructureOrdersStructureView
            {
                StructureId = structureAccess.StructureId,
                StructureName = structureMetadata.Data!.Entry.Name,
                SolarSystemId = structureMetadata.Data.Entry.SolarSystemId,
                Status = "failed",
                SelectedEsiCharacterId = credential.Data.EsiCharacterId,
                PageCount = ordersResult.PageCount,
                RequestCount = ordersResult.RequestCount,
                RetryCount = ordersResult.RetryCount,
                Errors = errors,
                Notes = notes
            };
            return new StructurePullResult(failedView, null);
        }

        var orders = ordersResult.Orders;
        var successfulView = new EsiAuthenticatedStructureOrdersStructureView
        {
            StructureId = structureAccess.StructureId,
            StructureName = structureMetadata.Data!.Entry.Name,
            SolarSystemId = structureMetadata.Data.Entry.SolarSystemId,
            Status = "completed",
            SelectedEsiCharacterId = credential.Data.EsiCharacterId,
            PageCount = ordersResult.PageCount,
            RequestCount = ordersResult.RequestCount + structureMetadata.Data.RequestCount,
            RetryCount = ordersResult.RetryCount + structureMetadata.Data.RetryCount,
            OrderCount = orders.Count,
            SnapshotCount = orders.Select(order => (order.TypeId, order.LocationId)).Distinct().Count(),
            ETag = ordersResult.Metadata?.ETag,
            ExpiresAtUtc = ordersResult.Metadata?.ExpiresAtUtc,
            LastModifiedAtUtc = ordersResult.Metadata?.LastModifiedAtUtc,
            Errors = Array.Empty<string>(),
            Notes = notes
        };

        return new StructurePullResult(
            successfulView,
            new StructurePullSuccess(
                structureMetadata.Data.Entry,
                orders,
                successfulView.OrderCount,
                successfulView.SnapshotCount));
    }

    private async Task<CredentialResolutionResult> ResolveCredentialAsync(
        string workspaceId,
        WorkspaceStructureAccessBinding structureAccess,
        int minimumValiditySeconds,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var esiCharacterId in structureAccess.AuthorizedEsiCharacterIds)
        {
            var statusResult = await _esiCredentialService.GetStatusAsync(
                new GetEsiCredentialStatusRequest
                {
                    WorkspaceId = workspaceId,
                    EsiCharacterId = esiCharacterId
                },
                cancellationToken);
            if (!statusResult.IsSuccess)
            {
                notes.Add($"Credential status lookup failed for ESI character '{esiCharacterId}'.");
                continue;
            }

            var missingScopes = RequiredScopes
                .Where(requiredScope => !statusResult.Data!.Scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingScopes.Length > 0)
            {
                notes.Add($"Skipped ESI character '{esiCharacterId}' because the bound credential is missing required scope(s): {string.Join(", ", missingScopes)}.");
                continue;
            }

            var tokenResult = await _esiCredentialService.ResolveAccessTokenAsync(
                new ResolveEsiAccessTokenRequest
                {
                    WorkspaceId = workspaceId,
                    EsiCharacterId = esiCharacterId,
                    MinimumValiditySeconds = minimumValiditySeconds,
                    AllowRefresh = true
                },
                cancellationToken);
            if (tokenResult.IsSuccess)
            {
                return CredentialResolutionResult.Success(
                    new ResolvedCredential(esiCharacterId, tokenResult.Data!),
                    notes);
            }

            notes.Add($"Access token resolution failed for ESI character '{esiCharacterId}': {tokenResult.Summary}");
        }

        return CredentialResolutionResult.Failure(
            ["No bound ESI credential with structure-market and structure-directory scopes is currently available for this structure."],
            notes);
    }

    private async Task<StructureDirectoryResolutionResult> ResolveStructureDirectoryEntryAsync(
        HttpClient httpClient,
        EsiAuthenticatedStructureOrdersPullRequest request,
        long structureId,
        EsiAccessTokenEnvelope credential,
        IReadOnlyDictionary<long, StructureDirectoryEntry> existingStructureDirectory,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var response = await SendGetAsync(
            httpClient,
            request,
            BuildRelativeUri($"universe/structures/{structureId}/", request.Datasource),
            bearerToken: credential.AccessToken,
            cancellationToken);
        if (response.IsSuccess)
        {
            using var document = JsonDocument.Parse(response.Body!);
            var root = document.RootElement;
            var solarSystemId = root.GetProperty("solar_system_id").GetInt64();
            var ownerCorporationId = root.GetProperty("owner_id").GetInt64();
            var ownerCorporationName = await ResolveCorporationNameAsync(
                httpClient,
                request,
                ownerCorporationId,
                cancellationToken);

            return StructureDirectoryResolutionResult.Success(
                new StructureDirectoryEntry(
                    structureId,
                    solarSystemId,
                    root.GetProperty("name").GetString() ?? structureId.ToString(CultureInfo.InvariantCulture),
                    ownerCorporationName,
                    "account_scoped"),
                response.RequestCount,
                response.RetryCount,
                notes);
        }

        if (existingStructureDirectory.TryGetValue(structureId, out var existingEntry))
        {
            notes.Add($"Reused cached structure directory entry for '{structureId}' because live structure directory refresh failed.");
            return StructureDirectoryResolutionResult.Success(existingEntry, response.RequestCount, response.RetryCount, notes);
        }

        return StructureDirectoryResolutionResult.Failure(
            [$"Unable to resolve structure directory metadata for '{structureId}': {response.ErrorSummary}"],
            notes);
    }

    private async Task<string> ResolveCorporationNameAsync(
        HttpClient httpClient,
        EsiAuthenticatedStructureOrdersPullRequest request,
        long corporationId,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(
            httpClient,
            request,
            BuildRelativeUri($"corporations/{corporationId}/", request.Datasource),
            bearerToken: null,
            cancellationToken);
        if (!response.IsSuccess)
        {
            return $"Corporation {corporationId}";
        }

        using var document = JsonDocument.Parse(response.Body!);
        return document.RootElement.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? $"Corporation {corporationId}"
            : $"Corporation {corporationId}";
    }

    private async Task<StructureOrdersFetchResult> FetchStructureOrdersAsync(
        HttpClient httpClient,
        EsiAuthenticatedStructureOrdersPullRequest request,
        long structureId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var firstPage = await SendGetAsync(
            httpClient,
            request,
            BuildRelativeUri($"markets/structures/{structureId}/", request.Datasource, page: 1),
            accessToken,
            cancellationToken);
        if (!firstPage.IsSuccess)
        {
            return StructureOrdersFetchResult.Failure(
                firstPage.ErrorSummary is null ? ["Unknown ESI failure."] : [firstPage.ErrorSummary],
                firstPage.RequestCount,
                firstPage.RetryCount,
                0,
                null,
                []);
        }

        var allOrders = new ConcurrentBag<EsiStructureOrder>(ParseOrders(firstPage.Body!, structureId));
        var totalPages = ParsePageCount(firstPage.ResponseHeaders);
        var requestCount = firstPage.RequestCount;
        var retryCount = firstPage.RetryCount;
        var errors = new ConcurrentBag<string>();

        if (totalPages > 1)
        {
            var semaphore = new SemaphoreSlim(Math.Max(1, request.MaxConcurrentRequests));
            var tasks = Enumerable.Range(2, totalPages - 1)
                .Select(async page =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var pageResponse = await SendGetAsync(
                            httpClient,
                            request,
                            BuildRelativeUri($"markets/structures/{structureId}/", request.Datasource, page),
                            accessToken,
                            cancellationToken);
                        Interlocked.Add(ref requestCount, pageResponse.RequestCount);
                        Interlocked.Add(ref retryCount, pageResponse.RetryCount);

                        if (!pageResponse.IsSuccess)
                        {
                            errors.Add(pageResponse.ErrorSummary ?? $"Page {page} failed.");
                            return;
                        }

                        foreach (var order in ParseOrders(pageResponse.Body!, structureId))
                        {
                            allOrders.Add(order);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                })
                .ToArray();

            await Task.WhenAll(tasks);
        }

        if (!errors.IsEmpty)
        {
            return StructureOrdersFetchResult.Failure(
                errors.ToArray(),
                requestCount,
                retryCount,
                totalPages,
                EsiResponseMetadata.FromHeaders(firstPage.ResponseHeaders),
                allOrders.ToArray());
        }

        return StructureOrdersFetchResult.Success(
            allOrders.ToArray(),
            requestCount,
            retryCount,
            totalPages,
            EsiResponseMetadata.FromHeaders(firstPage.ResponseHeaders));
    }

    private async Task<HttpFetchResult> SendGetAsync(
        HttpClient httpClient,
        EsiAuthenticatedStructureOrdersPullRequest request,
        string relativeUri,
        string? bearerToken,
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
            if (!string.IsNullOrWhiteSpace(request.CompatibilityDate))
            {
                message.Headers.TryAddWithoutValidation("x-compatibility-date", request.CompatibilityDate);
            }

            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            HttpResponseMessage? response = null;
            try
            {
                requestCount++;
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return HttpFetchResult.Success(body, response.Headers, requestCount, retryCount);
                }

                if (RetryableStatusCodes.Contains(response.StatusCode) && attempt < request.MaxRetriesPerRequest)
                {
                    retryCount++;
                    await Task.Delay(ComputeRetryDelay(request.RetryBaseDelayMilliseconds, retryCount), cancellationToken);
                    continue;
                }

                return HttpFetchResult.Failure(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}: {TrimBody(body)}",
                    response.Headers,
                    requestCount,
                    retryCount);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < request.MaxRetriesPerRequest)
            {
                retryCount++;
                await Task.Delay(ComputeRetryDelay(request.RetryBaseDelayMilliseconds, retryCount), cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }

        return HttpFetchResult.Failure(
            "Request failed after exhausting retries.",
            CreateEmptyHeaders(),
            requestCount,
            retryCount);
    }

    private static TimeSpan ComputeRetryDelay(int baseDelayMilliseconds, int retryCount)
    {
        return TimeSpan.FromMilliseconds(Math.Max(baseDelayMilliseconds, 100) * retryCount);
    }

    private static string BuildRelativeUri(string path, string datasource, int? page = null)
    {
        var builder = $"{path}?datasource={Uri.EscapeDataString(datasource)}";
        if (page is not null)
        {
            builder += $"&page={page.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        return builder;
    }

    private static int ParsePageCount(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("X-Pages", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return 1;
    }

    private static IReadOnlyList<EsiStructureOrder> ParseOrders(string body, long structureId)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .EnumerateArray()
            .Select(element => new EsiStructureOrder(
                element.GetProperty("order_id").GetInt64(),
                element.GetProperty("type_id").GetInt64(),
                element.TryGetProperty("location_id", out var locationIdElement)
                    ? locationIdElement.GetInt64()
                    : structureId,
                element.GetProperty("is_buy_order").GetBoolean(),
                element.GetProperty("price").GetDecimal(),
                element.GetProperty("volume_remain").GetInt64(),
                element.GetProperty("min_volume").GetInt64(),
                element.GetProperty("range").GetString() ?? "station",
                DateTimeOffset.Parse(element.GetProperty("issued").GetString()!, CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static AggregatedOrderPayload AggregateOrders(
        IReadOnlyList<EsiStructureOrder> orders,
        DateTimeOffset observedAtUtc)
    {
        var snapshots = orders
            .GroupBy(order => (order.TypeId, order.LocationId))
            .OrderBy(group => group.Key.TypeId)
            .ThenBy(group => group.Key.LocationId)
            .Select(group =>
            {
                var sellOrders = group
                    .Where(order => !order.IsBuyOrder)
                    .OrderBy(order => order.Price)
                    .ThenBy(order => order.OrderId)
                    .Select(ToPayloadRow)
                    .ToArray();
                var buyOrders = group
                    .Where(order => order.IsBuyOrder)
                    .OrderByDescending(order => order.Price)
                    .ThenBy(order => order.OrderId)
                    .Select(ToPayloadRow)
                    .ToArray();

                return new EsiPayloadOrderSnapshot(
                    group.Key.TypeId,
                    group.Key.LocationId,
                    observedAtUtc,
                    sellOrders,
                    buyOrders);
            })
            .ToArray();

        return new AggregatedOrderPayload(
            snapshots,
            orders.Count,
            snapshots.Length);
    }

    private static EsiPayloadOrderRow ToPayloadRow(EsiStructureOrder order)
    {
        return new EsiPayloadOrderRow(
            order.OrderId,
            order.Price,
            order.RemainingVolume,
            order.MinVolume,
            order.Range,
            order.IssuedAtUtc);
    }

    private static PayloadArtifacts WritePayloadArtifacts(
        string outputDirectoryPath,
        EsiAuthenticatedStructureOrdersPullRequest request,
        AggregatedOrderPayload payload,
        IReadOnlyList<EsiAuthenticatedStructureOrdersStructureView> structures,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        var latestDirectoryPath = Path.Combine(outputDirectoryPath, "latest");
        var historyDirectoryPath = Path.Combine(outputDirectoryPath, "history");
        Directory.CreateDirectory(latestDirectoryPath);
        Directory.CreateDirectory(historyDirectoryPath);

        var latestPayloadPath = Path.Combine(latestDirectoryPath, $"workspace-{request.WorkspaceId}.authenticated-structure.orders-import.json");
        var archivePayloadPath = Path.Combine(
            historyDirectoryPath,
            $"{completedAtUtc:yyyyMMddTHHmmssZ}-workspace-{request.WorkspaceId}.authenticated-structure.orders-import.json");

        WritePayloadJson(latestPayloadPath, request, payload, structures, startedAtUtc, completedAtUtc);
        WritePayloadJson(archivePayloadPath, request, payload, structures, startedAtUtc, completedAtUtc);
        return new PayloadArtifacts(latestPayloadPath, archivePayloadPath);
    }

    private static void WritePayloadJson(
        string path,
        EsiAuthenticatedStructureOrdersPullRequest request,
        AggregatedOrderPayload payload,
        IReadOnlyList<EsiAuthenticatedStructureOrdersStructureView> structures,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
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
                writer.WriteString("source", $"esi-authenticated-structure-orders:workspace:{request.WorkspaceId}");
                writer.WriteStartObject("ingress_trace");
                writer.WriteString("workspace_id", request.WorkspaceId);
                writer.WriteString("requested_by", request.RequestedBy);
                writer.WriteString("trigger_kind", request.TriggerKind);
                writer.WriteString("started_at_utc", FormatInstant(startedAtUtc));
                writer.WriteString("completed_at_utc", FormatInstant(completedAtUtc));
                writer.WriteNumber("order_count", payload.OrderCount);
                writer.WriteNumber("snapshot_count", payload.SnapshotCount);
                writer.WriteString("payload_sha256", ComputePayloadSha256(payload));
                writer.WritePropertyName("structures");
                writer.WriteStartArray();
                foreach (var structure in structures.Where(structure => string.Equals(structure.Status, "completed", StringComparison.OrdinalIgnoreCase)))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("structure_id", structure.StructureId);
                    if (!string.IsNullOrWhiteSpace(structure.StructureName))
                    {
                        writer.WriteString("name", structure.StructureName);
                    }

                    if (structure.SolarSystemId is not null)
                    {
                        writer.WriteNumber("solar_system_id", structure.SolarSystemId.Value);
                    }

                    writer.WriteString("selected_esi_character_id", structure.SelectedEsiCharacterId);
                    writer.WriteNumber("page_count", structure.PageCount);
                    writer.WriteNumber("request_count", structure.RequestCount);
                    writer.WriteNumber("order_count", structure.OrderCount);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
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

    private static EsiAuthenticatedStructureOrdersPullView BuildPullView(
        EsiAuthenticatedStructureOrdersPullRequest request,
        string marketFactsDirectoryPath,
        string structureDirectoryPath,
        string payloadPath,
        string archivePath,
        string status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string source,
        int orderCount,
        int snapshotCount,
        StructureDirectoryOverlayWriteResult directoryWriteResult,
        MarketOrdersRuntimeRunView? importRun,
        IReadOnlyList<EsiAuthenticatedStructureOrdersStructureView> structures,
        IReadOnlyList<string> notes,
        IReadOnlyList<string> errors)
    {
        return new EsiAuthenticatedStructureOrdersPullView
        {
            WorkspaceId = request.WorkspaceId,
            RequestedBy = request.RequestedBy,
            TriggerKind = request.TriggerKind,
            Status = status,
            PayloadPath = payloadPath,
            ArchivePath = archivePath,
            MarketFactsDirectoryPath = marketFactsDirectoryPath,
            StructureDirectoryPath = structureDirectoryPath,
            Source = source,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RequestedStructureCount = structures.Count,
            PulledStructureCount = structures.Count(structure => string.Equals(structure.Status, "completed", StringComparison.OrdinalIgnoreCase)),
            FailedStructureCount = structures.Count(structure => string.Equals(structure.Status, "failed", StringComparison.OrdinalIgnoreCase)),
            OrderCount = orderCount,
            SnapshotCount = snapshotCount,
            StructureDirectoryUpsertedCount = directoryWriteResult.UpsertedEntryCount,
            StructureDirectoryTotalCount = directoryWriteResult.TotalEntryCount,
            ImportRun = importRun,
            Structures = structures,
            Notes = notes,
            Errors = errors
        };
    }

    private sealed record ResolvedCredential(
        string EsiCharacterId,
        EsiAccessTokenEnvelope Envelope);

    private sealed record EsiStructureOrder(
        long OrderId,
        long TypeId,
        long LocationId,
        bool IsBuyOrder,
        decimal Price,
        long RemainingVolume,
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
        int SnapshotCount);

    private sealed record PayloadArtifacts(
        string LatestPayloadPath,
        string ArchivePayloadPath);

    private sealed record StructurePullSuccess(
        StructureDirectoryEntry StructureDirectoryEntry,
        IReadOnlyList<EsiStructureOrder> Orders,
        int OrderCount,
        int SnapshotCount);

    private sealed record StructurePullResult(
        EsiAuthenticatedStructureOrdersStructureView View,
        StructurePullSuccess? Success);

    private sealed record CredentialResolutionResult(
        ResolvedCredential? Data,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes)
    {
        public bool IsSuccess => Data is not null;

        public static CredentialResolutionResult Success(ResolvedCredential data, IReadOnlyList<string> notes)
            => new(data, Array.Empty<string>(), notes);

        public static CredentialResolutionResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string> notes)
            => new(null, errors, notes);
    }

    private sealed record StructureDirectoryResolutionResult(
        StructureDirectoryResolutionPayload? Data,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes)
    {
        public bool IsSuccess => Data is not null;

        public static StructureDirectoryResolutionResult Success(
            StructureDirectoryEntry entry,
            int requestCount,
            int retryCount,
            IReadOnlyList<string> notes)
            => new(new StructureDirectoryResolutionPayload(entry, requestCount, retryCount), Array.Empty<string>(), notes);

        public static StructureDirectoryResolutionResult Failure(IReadOnlyList<string> errors, IReadOnlyList<string> notes)
            => new(null, errors, notes);
    }

    private sealed record StructureDirectoryResolutionPayload(
        StructureDirectoryEntry Entry,
        int RequestCount,
        int RetryCount);

    private sealed record StructureOrdersFetchResult(
        IReadOnlyList<EsiStructureOrder> Orders,
        IReadOnlyList<string> Errors,
        int RequestCount,
        int RetryCount,
        int PageCount,
        EsiResponseMetadata? Metadata)
    {
        public bool IsSuccess => Errors.Count == 0;

        public static StructureOrdersFetchResult Success(
            IReadOnlyList<EsiStructureOrder> orders,
            int requestCount,
            int retryCount,
            int pageCount,
            EsiResponseMetadata? metadata)
            => new(orders, Array.Empty<string>(), requestCount, retryCount, pageCount, metadata);

        public static StructureOrdersFetchResult Failure(
            IReadOnlyList<string> errors,
            int requestCount,
            int retryCount,
            int pageCount,
            EsiResponseMetadata? metadata,
            IReadOnlyList<EsiStructureOrder> partialOrders)
            => new(partialOrders, errors, requestCount, retryCount, pageCount, metadata);
    }

    private sealed record HttpFetchResult(
        string? Body,
        HttpResponseHeaders ResponseHeaders,
        string? ErrorSummary,
        int RequestCount,
        int RetryCount)
    {
        public bool IsSuccess => ErrorSummary is null;

        public static HttpFetchResult Success(
            string body,
            HttpResponseHeaders responseHeaders,
            int requestCount,
            int retryCount)
            => new(body, responseHeaders, null, requestCount, retryCount);

        public static HttpFetchResult Failure(
            string errorSummary,
            HttpResponseHeaders responseHeaders,
            int requestCount,
            int retryCount)
            => new(null, responseHeaders, errorSummary, requestCount, retryCount);
    }

    private sealed record EsiResponseMetadata(
        string? ETag,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? LastModifiedAtUtc)
    {
        public static EsiResponseMetadata FromHeaders(HttpResponseHeaders headers)
        {
            return new EsiResponseMetadata(
                headers.ETag?.Tag,
                headers.TryGetValues("Expires", out var expiresValues)
                && DateTimeOffset.TryParse(expiresValues.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAtUtc)
                    ? expiresAtUtc
                    : null,
                headers.TryGetValues("Last-Modified", out var lastModifiedValues)
                && DateTimeOffset.TryParse(lastModifiedValues.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lastModifiedAtUtc)
                    ? lastModifiedAtUtc
                    : null);
        }
    }

    private static HttpResponseHeaders CreateEmptyHeaders()
    {
        return new HttpResponseMessage(HttpStatusCode.OK).Headers;
    }
}
