using EdenOS.Application.Accounts;
using EdenOS.Contracts.Market;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class InMemoryMarketFactsService : IMarketFactsService
{
    private static readonly MarketFactSource SharedMetadataSource = new(
        "shared-metadata-bootstrap",
        IsCanonicalSurface: true,
        UsesPlaceholderData: false);

    private readonly MetadataBootstrapCatalog _metadata;
    private readonly IWorkspaceBoundMarketAccessService _workspaceBoundMarketAccessService;
    private readonly IMarketSnapshotStore _snapshotStore;
    private readonly IMarketHistoryStore _historyStore;
    private readonly IMarketStatisticsStore _statisticsStore;
    private readonly IMarketOrderStore _orderStore;
    private readonly IAdjustedPriceStore _adjustedPriceStore;

    public InMemoryMarketFactsService()
        : this(
            MetadataBootstrapCatalog.LoadDefault(),
            new InMemoryAccountCharacterPoolService(),
            new LocalMarketSnapshotStore(),
            new LocalMarketHistoryStore(),
            new LocalMarketStatisticsStore(),
            new LocalMarketOrderStore(),
            new LocalAdjustedPriceStore())
    {
    }

    public InMemoryMarketFactsService(IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService)
        : this(
            MetadataBootstrapCatalog.LoadDefault(),
            workspaceBoundMarketAccessService,
            new LocalMarketSnapshotStore(),
            new LocalMarketHistoryStore(),
            new LocalMarketStatisticsStore(),
            new LocalMarketOrderStore(),
            new LocalAdjustedPriceStore())
    {
    }

    public InMemoryMarketFactsService(
        string metadataManifestPath,
        IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService,
        string? structureDirectoryOverlayPath = null)
        : this(
            MetadataBootstrapCatalog.LoadFromPaths(metadataManifestPath, structureDirectoryOverlayPath),
            workspaceBoundMarketAccessService,
            new LocalMarketSnapshotStore(),
            new LocalMarketHistoryStore(),
            new LocalMarketStatisticsStore(),
            new LocalMarketOrderStore(),
            new LocalAdjustedPriceStore())
    {
    }

    public InMemoryMarketFactsService(
        string metadataManifestPath,
        string marketFactsDirectoryPath,
        IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService,
        string? structureDirectoryOverlayPath = null)
        : this(
            MetadataBootstrapCatalog.LoadFromPaths(metadataManifestPath, structureDirectoryOverlayPath),
            workspaceBoundMarketAccessService,
            new LocalMarketSnapshotStore(marketFactsDirectoryPath),
            new LocalMarketHistoryStore(marketFactsDirectoryPath),
            new LocalMarketStatisticsStore(marketFactsDirectoryPath),
            new LocalMarketOrderStore(marketFactsDirectoryPath),
            new LocalAdjustedPriceStore())
    {
    }

    internal InMemoryMarketFactsService(
        MetadataBootstrapCatalog metadata,
        IWorkspaceBoundMarketAccessService workspaceBoundMarketAccessService,
        IMarketSnapshotStore snapshotStore,
        IMarketHistoryStore historyStore,
        IMarketStatisticsStore statisticsStore,
        IMarketOrderStore orderStore,
        IAdjustedPriceStore adjustedPriceStore)
    {
        _metadata = metadata;
        _workspaceBoundMarketAccessService = workspaceBoundMarketAccessService;
        _snapshotStore = snapshotStore;
        _historyStore = historyStore;
        _statisticsStore = statisticsStore;
        _orderStore = orderStore;
        _adjustedPriceStore = adjustedPriceStore;
    }

    public UseCaseResult<KnownTypesCatalog> GetKnownTypes(GetKnownTypesRequest request)
    {
        var traceId = NextTraceId();
        if (request.RegionIds is null || request.RegionIds.Count == 0)
        {
            return UseCaseResult<KnownTypesCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "At least one region id is required.",
                traceId,
                ["region_ids must contain at least one value."]);
        }

        if (request.Limit is <= 0 or > 200)
        {
            return UseCaseResult<KnownTypesCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "Limit must be between 1 and 200.",
                traceId,
                ["limit must be between 1 and 200."]);
        }

        if (!request.RegionIds.Any(_metadata.HasRegion))
        {
            var coverage = CreateCoverage(
                coverageKind: "requested_region_catalog",
                isCompleteForRequest: false,
                coveredEntryCount: 0,
                coveredRegionIds: request.RegionIds.ToArray(),
                coveredLocationIds: [],
                notes:
                [
                    "Requested regions are outside the loaded metadata bundle.",
                    "Known type coverage is limited to the shared metadata bootstrap bundle."
                ]);

            return UseCaseResult<KnownTypesCatalog>.Success(
                new KnownTypesCatalog(
                    [],
                    request.RegionIds.ToArray(),
                    SharedMetadataSource,
                    CreateCanonical(
                        surfaceName: "market.get_known_types",
                        canonicalFactKind: "known_type_catalog",
                        scopeKind: "region_set",
                        scopeKey: BuildRegionScopeKey(request.RegionIds),
                        grain: "catalog"),
                    CreateFreshness(
                        freshnessScope: "metadata_bundle",
                        observedAtUtc: null,
                        referenceObservedAtUtc: null,
                        lastMarketDay: null,
                        summary: "Known type catalog reuses a shared metadata bundle and does not expose per-row market observation timestamps."),
                    coverage,
                    CreateProvenance(
                        _metadata.BundleVersion,
                        readModelKind: "shared_metadata_catalog",
                        projectionKind: "metadata_catalog_projection",
                        usesDerivedProjection: false,
                        usesStitchedSources: false,
                        rawFacts:
                        [
                            CreateRawFact(
                                partitionKey: "metadata-bundle",
                                sourceKind: SharedMetadataSource.SourceKind,
                                rawFactKind: "type_metadata",
                                scopeKey: BuildRegionScopeKey(request.RegionIds),
                                observedAtUtc: null,
                                lastMarketDay: null)
                        ]),
                    [
                        CreateAnomaly(
                            code: "requested_regions_outside_loaded_metadata",
                            severity: MarketFactAnomalySeverity.Warning,
                            summary: "No requested region is covered by the loaded metadata bundle.",
                            isBlocking: false)
                    ]),
                "No loaded metadata coverage exists for the requested regions.",
                traceId,
                ["Known type coverage currently follows the shared bootstrap metadata bundle only."]);
        }

        IEnumerable<MetadataBootstrapCatalog.MetadataTypeEntry> query = _metadata.ListPublishedTypes();

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            query = query.Where(type => type.Name.Contains(request.SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var items = query
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Take(request.Limit)
            .Select(type => new KnownTypeSummary(
                type.TypeId,
                type.Name,
                type.MarketGroupName,
                type.PackagedVolumeM3,
                type.IsPublished))
            .ToArray();

        var hasCompleteRegionCoverage = request.RegionIds.All(_metadata.HasRegion);
        var knownTypesCoverage = CreateCoverage(
            coverageKind: "requested_region_catalog",
            isCompleteForRequest: hasCompleteRegionCoverage,
            coveredEntryCount: items.Length,
            coveredRegionIds: request.RegionIds.ToArray(),
            coveredLocationIds: [],
            notes:
            [
                "Catalog rows come from the shared metadata publication set.",
                "Catalog scope is not yet filtered by live market activity coverage."
            ]);

        var knownTypesAnomalies = new List<MarketFactAnomaly>();
        if (!hasCompleteRegionCoverage)
        {
            knownTypesAnomalies.Add(
                CreateAnomaly(
                    code: "partial_region_metadata_coverage",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "Some requested regions are outside the loaded metadata bundle.",
                    isBlocking: false));
        }

        return UseCaseResult<KnownTypesCatalog>.Success(
            new KnownTypesCatalog(
                items,
                request.RegionIds.ToArray(),
                SharedMetadataSource,
                CreateCanonical(
                    surfaceName: "market.get_known_types",
                    canonicalFactKind: "known_type_catalog",
                    scopeKind: "region_set",
                    scopeKey: BuildRegionScopeKey(request.RegionIds),
                    grain: "catalog"),
                CreateFreshness(
                    freshnessScope: "metadata_bundle",
                    observedAtUtc: null,
                    referenceObservedAtUtc: null,
                    lastMarketDay: null,
                    summary: "Known type catalog reuses a shared metadata bundle and does not expose per-row market observation timestamps."),
                knownTypesCoverage,
                CreateProvenance(
                    _metadata.BundleVersion,
                    readModelKind: "shared_metadata_catalog",
                    projectionKind: "metadata_catalog_projection",
                    usesDerivedProjection: false,
                    usesStitchedSources: false,
                    rawFacts:
                    [
                        CreateRawFact(
                            partitionKey: "metadata-bundle",
                            sourceKind: SharedMetadataSource.SourceKind,
                            rawFactKind: "type_metadata",
                            scopeKey: BuildRegionScopeKey(request.RegionIds),
                            observedAtUtc: null,
                            lastMarketDay: null)
                    ]),
                knownTypesAnomalies),
            $"Returned {items.Length} known type(s) for the requested market scope.",
            traceId,
            [
                $"Known types now reuse shared metadata bundle '{_metadata.BundleVersion}'.",
                "Catalog scope currently reflects the bootstrap metadata publication set rather than live market activity coverage."
            ]);
    }

    public UseCaseResult<ItemMarketProfile> GetItemProfile(GetItemProfileRequest request)
    {
        var traceId = NextTraceId();
        if (!_metadata.TryGetType(request.TypeId, out var type))
        {
            return UseCaseResult<ItemMarketProfile>.Failure(
                UseCaseStatus.NotFound,
                $"Type '{request.TypeId}' was not found.",
                traceId,
                ["Unknown type id."]);
        }

        var coveredLocationIds = _snapshotStore.ListCoveredLocations(request.TypeId);
        var knownMarkets = coveredLocationIds
            .Select(locationId => _metadata.TryGetLocation(locationId, out var location) ? location : null)
            .Where(location => location is not null)
            .Cast<MetadataBootstrapCatalog.MetadataLocationEntry>()
            .OrderBy(location => location.Name, StringComparer.OrdinalIgnoreCase)
            .Select(location => new MarketLocationSummary(
                location.LocationId,
                location.Name,
                location.Kind,
                location.RequiresAccountGrant,
                HasCurrentSnapshot: true))
            .ToArray();

        DateTimeOffset? latestKnownSnapshotObservedAtUtc = coveredLocationIds
            .Select(_snapshotStore.GetLastObservedAtUtc)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        if (latestKnownSnapshotObservedAtUtc == default)
        {
            latestKnownSnapshotObservedAtUtc = null;
        }

        var itemProfileCoverage = CreateCoverage(
            coverageKind: "known_market_snapshot_catalog",
            isCompleteForRequest: knownMarkets.Length > 0,
            coveredEntryCount: knownMarkets.Length,
            coveredRegionIds: knownMarkets
                .Select(location => ResolveRegionId(location.LocationId))
                .Where(regionId => regionId > 0)
                .Distinct()
                .OrderBy(regionId => regionId)
                .ToArray(),
            coveredLocationIds: knownMarkets.Select(location => location.LocationId).ToArray(),
            notes:
            [
                "Known market coverage is inferred from current snapshot presence in the local store.",
                "The surface does not yet stitch multiple source scopes into a higher-level market scope."
            ]);

        var itemProfileAnomalies = new List<MarketFactAnomaly>();
        if (knownMarkets.Length == 0)
        {
            itemProfileAnomalies.Add(
                CreateAnomaly(
                    code: "no_known_market_snapshot_coverage",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "No local snapshot-backed market location is currently known for this type.",
                    isBlocking: false));
        }

        return UseCaseResult<ItemMarketProfile>.Success(
            new ItemMarketProfile(
                type.TypeId,
                type.Name,
                type.Description,
                type.MarketGroupName,
                type.VolumeM3,
                type.PackagedVolumeM3,
                type.IsPublished,
                knownMarkets,
                _snapshotStore.Source,
                CreateCanonical(
                    surfaceName: "market.get_item_profile",
                    canonicalFactKind: "item_market_profile",
                    scopeKind: "type",
                    scopeKey: BuildTypeScopeKey(type.TypeId),
                    grain: "type"),
                CreateFreshness(
                    freshnessScope: "latest_known_market_snapshot_for_type",
                    observedAtUtc: latestKnownSnapshotObservedAtUtc,
                    referenceObservedAtUtc: latestKnownSnapshotObservedAtUtc,
                    lastMarketDay: null,
                    summary: knownMarkets.Length > 0
                        ? "Item profile freshness follows the newest known current snapshot for this type."
                        : "Item profile has no snapshot-backed known market coverage yet."),
                itemProfileCoverage,
                CreateProvenance(
                    _snapshotStore.BundleVersion,
                    readModelKind: "item_market_profile_projection",
                    projectionKind: "known_market_snapshot_projection",
                    usesDerivedProjection: true,
                    usesStitchedSources: false,
                    rawFacts: coveredLocationIds
                        .Select(locationId => CreateRawFact(
                            partitionKey: "current-snapshots",
                            sourceKind: _snapshotStore.Source.SourceKind,
                            rawFactKind: "snapshot_presence",
                            scopeKey: BuildTypeLocationScopeKey(type.TypeId, locationId),
                            observedAtUtc: _snapshotStore.GetLastObservedAtUtc(locationId),
                            lastMarketDay: null))
                        .ToArray()),
                itemProfileAnomalies),
            $"Loaded item profile for '{type.Name}'.",
            traceId,
            [
                $"Type metadata now reuses shared bundle '{_metadata.BundleVersion}'.",
                DescribeStoreUsage("Known market coverage", _snapshotStore.Source, _snapshotStore.BundleVersion)
            ]);
    }

    public UseCaseResult<AdjustedPriceCatalog> GetAdjustedPrices(GetAdjustedPricesRequest request)
    {
        var traceId = NextTraceId();
        if (request.TypeIds is not null && request.TypeIds.Any(typeId => typeId <= 0))
        {
            return UseCaseResult<AdjustedPriceCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "Type ids must be positive integers.",
                traceId,
                ["type_ids must contain only positive values."]);
        }

        if (request.Limit.HasValue && request.Limit.Value <= 0)
        {
            return UseCaseResult<AdjustedPriceCatalog>.Failure(
                UseCaseStatus.InvalidInput,
                "Limit must be greater than zero when provided.",
                traceId,
                ["limit must be greater than zero when provided."]);
        }

        var catalogPrices = _adjustedPriceStore.ListPrices();
        if (catalogPrices.Count == 0)
        {
            return UseCaseResult<AdjustedPriceCatalog>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Adjusted price reference data is unavailable.",
                traceId,
                ["No adjusted-price reference bundle is currently loaded."]);
        }

        var requestedTypeIds = request.TypeIds?
            .Distinct()
            .ToArray() ?? [];
        var requestedTypeIdSet = requestedTypeIds.Length > 0
            ? requestedTypeIds.ToHashSet()
            : null;
        var requestedTypeOrder = requestedTypeIds
            .Select((typeId, index) => new KeyValuePair<long, int>(typeId, index))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var searchText = string.IsNullOrWhiteSpace(request.SearchText)
            ? null
            : request.SearchText.Trim();

        IEnumerable<AdjustedPriceRecord> query = catalogPrices;
        if (requestedTypeIdSet is not null)
        {
            query = query.Where(record => requestedTypeIdSet.Contains(record.TypeId));
        }

        var projectedEntries = query
            .Select(record => new
            {
                Record = record,
                ItemName = ResolveTypeName(record.TypeId)
            });

        if (searchText is not null)
        {
            projectedEntries = projectedEntries.Where(entry =>
                entry.ItemName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        projectedEntries = requestedTypeIds.Length > 0
            ? projectedEntries.OrderBy(entry => requestedTypeOrder[entry.Record.TypeId])
            : projectedEntries
                .OrderBy(entry => entry.ItemName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Record.TypeId);

        var filteredEntries = projectedEntries.ToArray();
        var limitedEntries = request.Limit.HasValue
            ? filteredEntries.Take(request.Limit.Value).ToArray()
            : filteredEntries;
        var missingRequestedTypeIds = requestedTypeIds.Length == 0
            ? []
            : requestedTypeIds
                .Where(typeId => !_adjustedPriceStore.TryGetAdjustedPrice(typeId, out _))
                .ToArray();
        var missingNameCount = limitedEntries.Count(entry => IsFallbackTypeName(entry.Record.TypeId, entry.ItemName));
        var wasTruncatedByLimit = request.Limit.HasValue && limitedEntries.Length < filteredEntries.Length;

        var prices = limitedEntries
            .Select(entry => new AdjustedPriceEntry(
                entry.Record.TypeId,
                entry.ItemName,
                entry.Record.AdjustedPrice,
                entry.Record.AveragePrice))
            .ToArray();

        var scopeKind = requestedTypeIds.Length > 0
            ? "type_set"
            : searchText is not null
                ? "search_query"
                : "global";
        var scopeKey = requestedTypeIds.Length > 0
            ? BuildTypeSetScopeKey(requestedTypeIds)
            : searchText is not null
                ? $"search:{searchText}"
                : "global:all";
        var observedAtUtc = _adjustedPriceStore.GeneratedAtUtc;

        var coverageNotes = new List<string>
        {
            "Adjusted prices currently load from the shared industry-reference bundle.",
            "The adjusted-price surface is globally scoped and is not tied to a region or location read."
        };
        if (wasTruncatedByLimit)
        {
            coverageNotes.Add("The returned catalog was truncated by the requested limit.");
        }

        var anomalies = new List<MarketFactAnomaly>();
        if (missingRequestedTypeIds.Length > 0)
        {
            anomalies.Add(
                CreateAnomaly(
                    code: "adjusted_price_missing_for_requested_type",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: $"Adjusted-price coverage is missing for {missingRequestedTypeIds.Length} requested type(s).",
                    isBlocking: false));
        }

        if (missingNameCount > 0)
        {
            anomalies.Add(
                CreateAnomaly(
                    code: "adjusted_price_missing_metadata_name",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: $"{missingNameCount} adjusted-price row(s) could not be resolved to shared metadata names.",
                    isBlocking: false));
        }

        if (wasTruncatedByLimit)
        {
            anomalies.Add(
                CreateAnomaly(
                    code: "adjusted_price_result_truncated",
                    severity: MarketFactAnomalySeverity.Info,
                    summary: "The adjusted-price result set was truncated by the requested limit.",
                    isBlocking: false));
        }

        return UseCaseResult<AdjustedPriceCatalog>.Success(
            new AdjustedPriceCatalog(
                prices,
                observedAtUtc,
                _adjustedPriceStore.Source,
                CreateCanonical(
                    surfaceName: "market.get_adjusted_prices",
                    canonicalFactKind: "adjusted_price_catalog",
                    scopeKind: scopeKind,
                    scopeKey: scopeKey,
                    grain: "type_catalog"),
                CreateFreshness(
                    freshnessScope: "global_adjusted_price_catalog",
                    observedAtUtc: observedAtUtc,
                    referenceObservedAtUtc: observedAtUtc,
                    lastMarketDay: observedAtUtc.HasValue
                        ? DateOnly.FromDateTime(observedAtUtc.Value.UtcDateTime)
                        : null,
                    summary: observedAtUtc.HasValue
                        ? "Adjusted-price freshness follows the upstream ESI market price snapshot generation timestamp."
                        : "Adjusted-price freshness is unavailable because the reference bundle does not include a generation timestamp."),
                CreateCoverage(
                    coverageKind: "adjusted_price_catalog",
                    isCompleteForRequest: missingRequestedTypeIds.Length == 0 && !wasTruncatedByLimit,
                    coveredEntryCount: prices.Length,
                    coveredRegionIds: [],
                    coveredLocationIds: [],
                    notes: coverageNotes),
                CreateProvenance(
                    _adjustedPriceStore.BundleVersion,
                    readModelKind: "adjusted_price_reference_catalog",
                    projectionKind: "adjusted_price_reference_projection",
                    usesDerivedProjection: false,
                    usesStitchedSources: false,
                    rawFacts:
                    [
                        CreateRawFact(
                            partitionKey: "adjusted-prices",
                            sourceKind: _adjustedPriceStore.Source.SourceKind,
                            rawFactKind: "adjusted_price_catalog",
                            scopeKey: scopeKey,
                            observedAtUtc: observedAtUtc,
                            lastMarketDay: observedAtUtc.HasValue
                                ? DateOnly.FromDateTime(observedAtUtc.Value.UtcDateTime)
                                : null)
                    ]),
                anomalies),
            $"Returned {prices.Length} adjusted price reference row(s).",
            traceId,
            [
                $"Adjusted prices now load from shared bundle '{_adjustedPriceStore.BundleVersion}'.",
                $"Type names are resolved through shared metadata bundle '{_metadata.BundleVersion}'."
            ]);
    }

    public UseCaseResult<PriceSnapshot> GetPriceSnapshot(GetPriceSnapshotRequest request)
    {
        var traceId = NextTraceId();
        if (!_metadata.TryGetType(request.TypeId, out var type))
        {
            return UseCaseResult<PriceSnapshot>.Failure(
                UseCaseStatus.NotFound,
                $"Type '{request.TypeId}' was not found.",
                traceId,
                ["Unknown type id."]);
        }

        if (!_metadata.TryGetLocation(request.LocationId, out var location))
        {
            return UseCaseResult<PriceSnapshot>.Failure(
                UseCaseStatus.NotFound,
                $"Location '{request.LocationId}' was not found.",
                traceId,
                ["Unknown location id."]);
        }

        var accessFailure = EnsureLocationAccess<PriceSnapshot>(location, request.AccountKey, traceId);
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        if (!_snapshotStore.TryGetSnapshot(request.TypeId, request.LocationId, out var snapshot))
        {
            return UseCaseResult<PriceSnapshot>.Failure(
                UseCaseStatus.NotFound,
                "No price snapshot exists for the requested item/location pair.",
                traceId,
                ["No local snapshot exists in the current store for this pair."]);
        }

        var latestLocationObservedAtUtc = _snapshotStore.GetLastObservedAtUtc(request.LocationId);
        var priceSnapshotFreshness = CreateFreshness(
            freshnessScope: "location_snapshot",
            observedAtUtc: snapshot.ObservedAtUtc,
            referenceObservedAtUtc: latestLocationObservedAtUtc,
            lastMarketDay: DateOnly.FromDateTime(snapshot.ObservedAtUtc.UtcDateTime),
            summary: "Snapshot freshness is compared against the newest local snapshot available for the same market location.");
        var priceSnapshotCoverage = CreateCoverage(
            coverageKind: "type_location_snapshot",
            isCompleteForRequest: true,
            coveredEntryCount: 1,
            coveredRegionIds: [location.RegionId],
            coveredLocationIds: [location.LocationId],
            notes:
            [
                "The canonical view exposes one current snapshot for the requested type/location pair.",
                "Raw facts remain anchored to the local current-snapshots partition."
            ]);

        var priceSnapshotAnomalies = new List<MarketFactAnomaly>();
        if (priceSnapshotFreshness.IsStale)
        {
            priceSnapshotAnomalies.Add(
                CreateAnomaly(
                    code: "snapshot_not_latest_for_location",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "The returned snapshot is older than the newest local snapshot currently available for this location.",
                    isBlocking: false));
        }

        return UseCaseResult<PriceSnapshot>.Success(
            new PriceSnapshot(
                request.TypeId,
                type.Name,
                request.LocationId,
                location.Name,
                location.Kind,
                snapshot.LowestSellPrice,
                snapshot.HighestBuyPrice,
                Math.Round((snapshot.LowestSellPrice + snapshot.HighestBuyPrice) / 2m, 2),
                snapshot.TopFiveSellDepthUnits,
                snapshot.TopFiveBuyDepthUnits,
                snapshot.SellOrderCount,
                snapshot.BuyOrderCount,
                snapshot.ObservedAtUtc,
                _snapshotStore.Source,
                CreateCanonical(
                    surfaceName: "market.get_price_snapshot",
                    canonicalFactKind: "price_snapshot",
                    scopeKind: "type_location",
                    scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                    grain: "current_pair"),
                priceSnapshotFreshness,
                priceSnapshotCoverage,
                CreateProvenance(
                    _snapshotStore.BundleVersion,
                    readModelKind: "local_price_snapshot_read_model",
                    projectionKind: "snapshot_pair_projection",
                    usesDerivedProjection: false,
                    usesStitchedSources: false,
                    rawFacts:
                    [
                        CreateRawFact(
                            partitionKey: "current-snapshots",
                            sourceKind: _snapshotStore.Source.SourceKind,
                            rawFactKind: "snapshot_pair",
                            scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                            observedAtUtc: snapshot.ObservedAtUtc,
                            lastMarketDay: DateOnly.FromDateTime(snapshot.ObservedAtUtc.UtcDateTime))
                    ]),
                priceSnapshotAnomalies),
            $"Loaded price snapshot for '{type.Name}' at '{location.Name}'.",
            traceId,
            [
                DescribeStoreUsage("Snapshot facts", _snapshotStore.Source, _snapshotStore.BundleVersion),
                $"Item and location resolution now reuse shared metadata bundle '{_metadata.BundleVersion}'."
            ]);
    }

    public UseCaseResult<MarketOrdersEnvelope> GetMarketOrders(GetMarketOrdersRequest request)
    {
        var traceId = NextTraceId();
        if (request.Depth is <= 0 or > 20)
        {
            return UseCaseResult<MarketOrdersEnvelope>.Failure(
                UseCaseStatus.InvalidInput,
                "Depth must be between 1 and 20.",
                traceId,
                ["depth must be between 1 and 20."]);
        }

        if (!_metadata.TryGetType(request.TypeId, out var type))
        {
            return UseCaseResult<MarketOrdersEnvelope>.Failure(
                UseCaseStatus.NotFound,
                $"Type '{request.TypeId}' was not found.",
                traceId,
                ["Unknown type id."]);
        }

        if (!_metadata.TryGetLocation(request.LocationId, out var location))
        {
            return UseCaseResult<MarketOrdersEnvelope>.Failure(
                UseCaseStatus.NotFound,
                $"Location '{request.LocationId}' was not found.",
                traceId,
                ["Unknown location id."]);
        }

        var accessFailure = EnsureLocationAccess<MarketOrdersEnvelope>(location, request.AccountKey, traceId);
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        if (!_orderStore.TryGetOrderSnapshot(request.TypeId, request.LocationId, out var orderSnapshot))
        {
            return UseCaseResult<MarketOrdersEnvelope>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Local orders snapshot is not available for this item/location pair.",
                traceId,
                ["No local fast-fact order snapshot exists for this pair and remote order refresh is not wired yet."]);
        }

        var latestLocationObservedAtUtc = _orderStore.GetLastObservedAtUtc(request.LocationId);
        var ordersFreshness = CreateFreshness(
            freshnessScope: "location_order_snapshot",
            observedAtUtc: orderSnapshot.ObservedAtUtc,
            referenceObservedAtUtc: latestLocationObservedAtUtc,
            lastMarketDay: DateOnly.FromDateTime(orderSnapshot.ObservedAtUtc.UtcDateTime),
            summary: "Order-book freshness is compared against the newest local fast-fact order snapshot available for the same market location.");
        var sellOrders = orderSnapshot.SellOrders
            .OrderBy(order => order.UnitPrice)
            .ThenBy(order => order.OrderId)
            .Take(request.Depth)
            .Select(MapOrderEntry)
            .ToArray();
        var buyOrders = orderSnapshot.BuyOrders
            .OrderByDescending(order => order.UnitPrice)
            .ThenBy(order => order.OrderId)
            .Take(request.Depth)
            .Select(MapOrderEntry)
            .ToArray();

        var coverageNotes = new List<string>
        {
            "Canonical orders expose the top requested depth from the locally retained fast-fact order snapshot.",
            "Older snapshots are retained on read if no fresher pair snapshot is available, so blank reads are avoided during refresh failures."
        };
        if (orderSnapshot.SellOrders.Count > request.Depth || orderSnapshot.BuyOrders.Count > request.Depth)
        {
            coverageNotes.Add("Additional raw order rows remain in the current-orders partition beyond the returned top-of-book depth.");
        }

        var ordersCoverage = CreateCoverage(
            coverageKind: "type_location_order_depth",
            isCompleteForRequest: true,
            coveredEntryCount: sellOrders.Length + buyOrders.Length,
            coveredRegionIds: [location.RegionId],
            coveredLocationIds: [location.LocationId],
            notes: coverageNotes);

        var ordersAnomalies = new List<MarketFactAnomaly>();
        if (ordersFreshness.IsStale)
        {
            ordersAnomalies.Add(
                CreateAnomaly(
                    code: "orders_snapshot_not_latest_for_location",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "The returned order snapshot is older than the newest local order snapshot available for this location and is being reused as a fallback.",
                    isBlocking: false));
        }

        return UseCaseResult<MarketOrdersEnvelope>.Success(
            new MarketOrdersEnvelope(
                request.TypeId,
                type.Name,
                request.LocationId,
                location.Name,
                location.Kind,
                request.Depth,
                orderSnapshot.ObservedAtUtc,
                orderSnapshot.SellOrders.Count,
                orderSnapshot.BuyOrders.Count,
                sellOrders,
                buyOrders,
                _orderStore.Source,
                CreateCanonical(
                    surfaceName: "market.get_orders",
                    canonicalFactKind: "order_book",
                    scopeKind: "type_location",
                    scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                    grain: "top_of_book_depth"),
                ordersFreshness,
                ordersCoverage,
                CreateProvenance(
                    _orderStore.BundleVersion,
                    readModelKind: "local_order_book_read_model",
                    projectionKind: "order_book_depth_projection",
                    usesDerivedProjection: true,
                    usesStitchedSources: false,
                    rawFacts:
                    [
                        CreateRawFact(
                            partitionKey: "current-orders",
                            sourceKind: _orderStore.Source.SourceKind,
                            rawFactKind: "sell_order_rows",
                            scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                            observedAtUtc: orderSnapshot.ObservedAtUtc,
                            lastMarketDay: DateOnly.FromDateTime(orderSnapshot.ObservedAtUtc.UtcDateTime)),
                        CreateRawFact(
                            partitionKey: "current-orders",
                            sourceKind: _orderStore.Source.SourceKind,
                            rawFactKind: "buy_order_rows",
                            scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                            observedAtUtc: orderSnapshot.ObservedAtUtc,
                            lastMarketDay: DateOnly.FromDateTime(orderSnapshot.ObservedAtUtc.UtcDateTime))
                    ]),
                ordersAnomalies),
            $"Loaded top-of-book orders for '{type.Name}' at '{location.Name}'.",
            traceId,
            [
                DescribeStoreUsage("Orders fast facts", _orderStore.Source, _orderStore.BundleVersion),
                "Older pair snapshots remain queryable so refresh failures degrade into staleness rather than empty reads."
            ]);
    }

    public UseCaseResult<HistoryWindow> GetHistoryWindow(GetHistoryWindowRequest request)
    {
        var traceId = NextTraceId();
        if (request.Days is <= 0 or > 180)
        {
            return UseCaseResult<HistoryWindow>.Failure(
                UseCaseStatus.InvalidInput,
                "Days must be between 1 and 180.",
                traceId,
                ["days must be between 1 and 180."]);
        }

        if (!_metadata.TryGetType(request.TypeId, out var type))
        {
            return UseCaseResult<HistoryWindow>.Failure(
                UseCaseStatus.NotFound,
                $"Type '{request.TypeId}' was not found.",
                traceId,
                ["Unknown type id."]);
        }

        if (!_metadata.TryGetLocation(request.LocationId, out var location))
        {
            return UseCaseResult<HistoryWindow>.Failure(
                UseCaseStatus.NotFound,
                $"Location '{request.LocationId}' was not found.",
                traceId,
                ["Unknown location id."]);
        }

        var accessFailure = EnsureLocationAccess<HistoryWindow>(location, request.AccountKey, traceId);
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        if (!_historyStore.TryGetHistory(request.TypeId, request.LocationId, out var points))
        {
            return UseCaseResult<HistoryWindow>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Local history store is not available for this item/location pair.",
                traceId,
                ["No local history series exists for this pair and archival refresh ingestion is not wired yet."]);
        }

        var window = points
            .OrderByDescending(point => point.Day)
            .Take(request.Days)
            .OrderBy(point => point.Day)
            .ToArray();

        var lastObservedAtUtc = GetLastObservedAtUtc(request.LocationId);
        var historyFreshness = CreateFreshness(
            freshnessScope: "history_window_against_location_snapshot",
            observedAtUtc: lastObservedAtUtc,
            referenceObservedAtUtc: _snapshotStore.GetLastObservedAtUtc(request.LocationId),
            lastMarketDay: window.LastOrDefault()?.Day,
            summary: "History freshness follows the latest local observation available for the same market location.");
        var historyCoverage = CreateCoverage(
            coverageKind: "requested_history_window",
            isCompleteForRequest: window.Length == request.Days,
            coveredEntryCount: window.Length,
            coveredRegionIds: [location.RegionId],
            coveredLocationIds: [location.LocationId],
            notes:
            [
                "Canonical history windows are trimmed from the locally stored history series.",
                "The returned window does not imply the store has complete archival coverage beyond the exposed points."
            ]);
        var historyAnomalies = new List<MarketFactAnomaly>();
        if (!historyCoverage.IsCompleteForRequest)
        {
            historyAnomalies.Add(
                CreateAnomaly(
                    code: "history_window_shorter_than_requested",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "Local archival coverage contains fewer history points than the requested window length.",
                    isBlocking: false));
        }
        if (historyFreshness.IsStale)
        {
            historyAnomalies.Add(
                CreateAnomaly(
                    code: "history_window_not_aligned_with_latest_location_snapshot",
                    severity: MarketFactAnomalySeverity.Info,
                    summary: "History freshness trails the newest local snapshot available for the same location.",
                    isBlocking: false));
        }

        return UseCaseResult<HistoryWindow>.Success(
            new HistoryWindow(
                request.TypeId,
                type.Name,
                request.LocationId,
                location.Name,
                request.Days,
                window,
                lastObservedAtUtc,
                _historyStore.Source,
                CreateCanonical(
                    surfaceName: "market.get_history_window",
                    canonicalFactKind: "history_window",
                    scopeKind: "type_location",
                    scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                    grain: "requested_day_window"),
                historyFreshness,
                historyCoverage,
                CreateProvenance(
                    _historyStore.BundleVersion,
                    readModelKind: "local_history_window_read_model",
                    projectionKind: "history_window_projection",
                    usesDerivedProjection: false,
                    usesStitchedSources: false,
                    rawFacts:
                    [
                        CreateRawFact(
                            partitionKey: "history-windows",
                            sourceKind: _historyStore.Source.SourceKind,
                            rawFactKind: "history_series",
                            scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                            observedAtUtc: lastObservedAtUtc,
                            lastMarketDay: points.LastOrDefault()?.Day),
                        CreateRawFact(
                            partitionKey: "current-snapshots",
                            sourceKind: _snapshotStore.Source.SourceKind,
                            rawFactKind: "location_freshness_anchor",
                            scopeKey: $"location:{request.LocationId}",
                            observedAtUtc: _snapshotStore.GetLastObservedAtUtc(request.LocationId),
                            lastMarketDay: null)
                    ]),
                historyAnomalies),
            $"Loaded {window.Length} history point(s) for '{type.Name}' at '{location.Name}'.",
            traceId,
            [
                DescribeStoreUsage("History facts", _historyStore.Source, _historyStore.BundleVersion),
                $"Item and location identity are resolved through shared metadata bundle '{_metadata.BundleVersion}'."
            ]);
    }

    public UseCaseResult<BasicStatisticsEnvelope> GetBasicStatistics(GetBasicStatisticsRequest request)
    {
        var traceId = NextTraceId();
        if (!_metadata.TryGetType(request.TypeId, out var type))
        {
            return UseCaseResult<BasicStatisticsEnvelope>.Failure(
                UseCaseStatus.NotFound,
                $"Type '{request.TypeId}' was not found.",
                traceId,
                ["Unknown type id."]);
        }

        if (!_metadata.TryGetLocation(request.LocationId, out var location))
        {
            return UseCaseResult<BasicStatisticsEnvelope>.Failure(
                UseCaseStatus.NotFound,
                $"Location '{request.LocationId}' was not found.",
                traceId,
                ["Unknown location id."]);
        }

        var accessFailure = EnsureLocationAccess<BasicStatisticsEnvelope>(location, request.AccountKey, traceId);
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        if (!_statisticsStore.TryGetProjection(request.TypeId, request.LocationId, out var projection))
        {
            return UseCaseResult<BasicStatisticsEnvelope>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Basic statistics require a local statistics projection, but none is available for this pair.",
                traceId,
                ["No local statistics projection exists for this pair and statistics refresh ingestion is not wired yet."]);
        }

        _historyStore.TryGetHistory(request.TypeId, request.LocationId, out var historyPoints);

        var statisticsFreshness = CreateFreshness(
            freshnessScope: "statistics_projection_against_location_snapshot",
            observedAtUtc: projection.DerivedFromLastObservedAtUtc,
            referenceObservedAtUtc: _snapshotStore.GetLastObservedAtUtc(request.LocationId),
            lastMarketDay: historyPoints?.LastOrDefault()?.Day,
            summary: "Statistics freshness follows the snapshot timestamp that the local projection was derived from.");
        var statisticsCoverage = CreateCoverage(
            coverageKind: "statistics_projection",
            isCompleteForRequest: true,
            coveredEntryCount: 3,
            coveredRegionIds: [location.RegionId],
            coveredLocationIds: [location.LocationId],
            notes:
            [
                "Canonical statistics are derived from local history windows plus the latest observation anchor.",
                "The current surface exposes fixed 7/21/60 day rolling windows."
            ]);
        var statisticsAnomalies = new List<MarketFactAnomaly>();
        if (statisticsFreshness.IsStale)
        {
            statisticsAnomalies.Add(
                CreateAnomaly(
                    code: "statistics_projection_not_latest_for_location",
                    severity: MarketFactAnomalySeverity.Warning,
                    summary: "The statistics projection trails the newest local snapshot available for this location.",
                    isBlocking: false));
        }

        var data = new BasicStatisticsEnvelope(
            request.TypeId,
            type.Name,
            request.LocationId,
            location.Name,
            projection.SevenDay,
            projection.TwentyOneDay,
            projection.SixtyDay,
            _statisticsStore.Source,
            CreateCanonical(
                surfaceName: "market.get_basic_statistics",
                canonicalFactKind: "basic_statistics",
                scopeKind: "type_location",
                scopeKey: BuildTypeLocationScopeKey(request.TypeId, request.LocationId),
                grain: "rolling_window_set"),
            statisticsFreshness,
            statisticsCoverage,
            CreateProvenance(
                _statisticsStore.BundleVersion,
                readModelKind: "local_statistics_read_model",
                projectionKind: "statistics_projection",
                usesDerivedProjection: true,
                usesStitchedSources: false,
                rawFacts: BuildStatisticsRawFacts(request.TypeId, request.LocationId, projection.DerivedFromLastObservedAtUtc, historyPoints)),
            statisticsAnomalies);

        return UseCaseResult<BasicStatisticsEnvelope>.Success(
            data,
            $"Loaded basic 7/21/60 day statistics for '{type.Name}' at '{location.Name}'.",
            traceId,
            [
                DescribeStoreUsage("Statistics projections", _statisticsStore.Source, _statisticsStore.BundleVersion),
                $"Item and location identity are resolved through shared metadata bundle '{_metadata.BundleVersion}'."
            ]);
    }

    public UseCaseResult<AccessibleStructureCatalog> GetAccessibleStructures(GetAccessibleStructuresRequest request)
    {
        var traceId = NextTraceId();
        var accessCatalogResult = _workspaceBoundMarketAccessService.GetMarketAccessCatalog(request.AccountKey);
        if (!accessCatalogResult.IsSuccess)
        {
            return UseCaseResult<AccessibleStructureCatalog>.Failure(
                accessCatalogResult.Status,
                accessCatalogResult.Summary,
                traceId,
                accessCatalogResult.Errors);
        }

        var missingMetadataStructures = new List<long>();
        var structures = accessCatalogResult.Data!.StructureGrants
            .Where(grant => request.IncludeStaleGrants || !grant.IsStale)
            .Select(grant =>
            {
                if (!_metadata.TryGetLocation(grant.StructureId, out var location))
                {
                    missingMetadataStructures.Add(grant.StructureId);
                    return null;
                }

                return new AccessibleStructure(
                    location.LocationId,
                    location.Name,
                    location.SolarSystemId,
                    location.SolarSystemName,
                    grant.GrantSource,
                    grant.LastVerifiedAtUtc,
                    IsMarketReadEnabled: true,
                    grant.IsStale);
            })
            .Where(structure => structure is not null)
            .Cast<AccessibleStructure>()
            .OrderBy(structure => structure.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = new List<string>
        {
            "Structure access is now bound to workspace/account context instead of anonymous local grants.",
            DescribeStoreUsage("Grant projections", accessCatalogResult.Data.Source, accessCatalogResult.Data.BundleVersion)
        };

        if (!accessCatalogResult.Data.Source.UsesPlaceholderData)
        {
            warnings.Add("Authenticated structure grant refresh is still not wired; current coverage is limited to the locally stored grant projection.");
        }

        if (missingMetadataStructures.Count > 0)
        {
            warnings.Add($"Skipped {missingMetadataStructures.Count} structure grant(s) because shared metadata was missing.");
        }

        DateTimeOffset? latestVerifiedAtUtc = structures
            .Select(structure => structure.LastVerifiedAtUtc)
            .DefaultIfEmpty()
            .Max();

        if (latestVerifiedAtUtc == default)
        {
            latestVerifiedAtUtc = null;
        }

        var structureCoverage = CreateCoverage(
            coverageKind: "workspace_market_structure_grants",
            isCompleteForRequest: true,
            coveredEntryCount: structures.Length,
            coveredRegionIds: structures
                .Select(structure => ResolveRegionId(structure.StructureId))
                .Where(regionId => regionId > 0)
                .Distinct()
                .OrderBy(regionId => regionId)
                .ToArray(),
            coveredLocationIds: structures.Select(structure => structure.StructureId).ToArray(),
            notes:
            [
                "Canonical structure access follows workspace/account-scoped grant projections.",
                "The surface does not yet expose full structure discovery coverage outside the stored grants."
            ]);
        var structureAnomalies = structures
            .Where(structure => structure.IsStale)
            .Select(structure => CreateAnomaly(
                code: "stale_structure_grant",
                severity: MarketFactAnomalySeverity.Warning,
                summary: $"Structure grant '{structure.Name}' is marked stale in the local grant projection.",
                isBlocking: false))
            .ToArray();

        return UseCaseResult<AccessibleStructureCatalog>.Success(
            new AccessibleStructureCatalog(
                request.AccountKey,
                structures,
                accessCatalogResult.Data.Source,
                CreateCanonical(
                    surfaceName: "market.get_accessible_structures",
                    canonicalFactKind: "accessible_structure_catalog",
                    scopeKind: "account_key",
                    scopeKey: $"account:{request.AccountKey}",
                    grain: "structure_catalog"),
                CreateFreshness(
                    freshnessScope: "workspace_market_grants",
                    observedAtUtc: latestVerifiedAtUtc,
                    referenceObservedAtUtc: latestVerifiedAtUtc,
                    lastMarketDay: null,
                    summary: structures.Length > 0
                        ? "Structure access freshness follows the latest verified workspace-bound grant."
                        : "No structure grant is currently present for this workspace-bound account."),
                structureCoverage,
                CreateProvenance(
                    accessCatalogResult.Data.BundleVersion,
                    readModelKind: "workspace_market_access_catalog",
                    projectionKind: "structure_grant_projection",
                    usesDerivedProjection: true,
                    usesStitchedSources: false,
                    rawFacts: accessCatalogResult.Data.StructureGrants
                        .Select(grant => CreateRawFact(
                            partitionKey: "structure-grants",
                            sourceKind: accessCatalogResult.Data.Source.SourceKind,
                            rawFactKind: "structure_grant",
                            scopeKey: $"account:{request.AccountKey}/structure:{grant.StructureId}",
                            observedAtUtc: grant.LastVerifiedAtUtc,
                            lastMarketDay: null))
                        .ToArray()),
                structureAnomalies),
            $"Returned {structures.Length} accessible market structure(s) for '{request.AccountKey}'.",
            traceId,
            warnings);
    }

    private UseCaseResult<T>? EnsureLocationAccess<T>(
        MetadataBootstrapCatalog.MetadataLocationEntry location,
        string? accountKey,
        string traceId)
    {
        if (!location.RequiresAccountGrant)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(accountKey))
        {
            return UseCaseResult<T>.Failure(
                UseCaseStatus.PermissionDenied,
                $"Location '{location.Name}' requires an account-bound structure grant.",
                traceId,
                ["A structure-backed market read requires account-scoped access."]);
        }

        var accessCatalogResult = _workspaceBoundMarketAccessService.GetMarketAccessCatalog(accountKey);
        if (!accessCatalogResult.IsSuccess)
        {
            return UseCaseResult<T>.Failure(
                accessCatalogResult.Status,
                accessCatalogResult.Summary,
                traceId,
                accessCatalogResult.Errors);
        }

        if (!accessCatalogResult.Data!.StructureGrants.Any(grant => grant.StructureId == location.LocationId))
        {
            return UseCaseResult<T>.Failure(
                UseCaseStatus.PermissionDenied,
                $"Account '{accountKey}' does not have market read access to '{location.Name}'.",
                traceId,
                ["The provided account key does not include a grant for this structure."]);
        }

        return null;
    }

    private DateTimeOffset GetLastObservedAtUtc(long locationId)
    {
        return _snapshotStore.GetLastObservedAtUtc(locationId)
            ?? DateTimeOffset.Parse("2026-04-11T00:00:00Z");
    }

    private IReadOnlyList<MarketRawFactReference> BuildStatisticsRawFacts(
        long typeId,
        long locationId,
        DateTimeOffset derivedFromLastObservedAtUtc,
        IReadOnlyList<HistoryPoint>? historyPoints)
    {
        var rawFacts = new List<MarketRawFactReference>
        {
            CreateRawFact(
                partitionKey: "current-snapshots",
                sourceKind: _snapshotStore.Source.SourceKind,
                rawFactKind: "location_freshness_anchor",
                scopeKey: $"location:{locationId}",
                observedAtUtc: _snapshotStore.GetLastObservedAtUtc(locationId),
                lastMarketDay: null),
            CreateRawFact(
                partitionKey: "statistics-projections",
                sourceKind: _statisticsStore.Source.SourceKind,
                rawFactKind: "statistics_projection",
                scopeKey: BuildTypeLocationScopeKey(typeId, locationId),
                observedAtUtc: derivedFromLastObservedAtUtc,
                lastMarketDay: historyPoints?.LastOrDefault()?.Day)
        };

        if (historyPoints is not null)
        {
            rawFacts.Add(
                CreateRawFact(
                    partitionKey: "history-windows",
                    sourceKind: _historyStore.Source.SourceKind,
                    rawFactKind: "history_series",
                    scopeKey: BuildTypeLocationScopeKey(typeId, locationId),
                    observedAtUtc: derivedFromLastObservedAtUtc,
                    lastMarketDay: historyPoints.LastOrDefault()?.Day));
        }

        return rawFacts;
    }

    private long ResolveRegionId(long locationId)
    {
        return _metadata.TryGetLocation(locationId, out var location)
            ? location.RegionId
            : 0;
    }

    private static MarketCanonicalFact CreateCanonical(
        string surfaceName,
        string canonicalFactKind,
        string scopeKind,
        string scopeKey,
        string grain)
    {
        return new MarketCanonicalFact(
            surfaceName,
            canonicalFactKind,
            scopeKind,
            scopeKey,
            grain,
            IsStitched: false,
            IsReadModel: true);
    }

    private static MarketRawFactReference CreateRawFact(
        string partitionKey,
        string sourceKind,
        string rawFactKind,
        string scopeKey,
        DateTimeOffset? observedAtUtc,
        DateOnly? lastMarketDay,
        bool matchesCanonicalScope = true)
    {
        return new MarketRawFactReference(
            partitionKey,
            sourceKind,
            rawFactKind,
            scopeKey,
            observedAtUtc,
            lastMarketDay,
            matchesCanonicalScope);
    }

    private static MarketFactProvenance CreateProvenance(
        string bundleVersion,
        string readModelKind,
        string projectionKind,
        bool usesDerivedProjection,
        bool usesStitchedSources,
        IReadOnlyList<MarketRawFactReference> rawFacts)
    {
        return new MarketFactProvenance(
            bundleVersion,
            readModelKind,
            projectionKind,
            usesDerivedProjection,
            usesStitchedSources,
            rawFacts);
    }

    private static MarketFactFreshness CreateFreshness(
        string freshnessScope,
        DateTimeOffset? observedAtUtc,
        DateTimeOffset? referenceObservedAtUtc,
        DateOnly? lastMarketDay,
        string summary)
    {
        var lagMinutes = ComputeLagMinutes(observedAtUtc, referenceObservedAtUtc);
        return new MarketFactFreshness(
            freshnessScope,
            observedAtUtc,
            referenceObservedAtUtc,
            lastMarketDay,
            lagMinutes,
            (lagMinutes ?? 0) > 0,
            summary);
    }

    private static MarketFactCoverage CreateCoverage(
        string coverageKind,
        bool isCompleteForRequest,
        int coveredEntryCount,
        IReadOnlyList<long> coveredRegionIds,
        IReadOnlyList<long> coveredLocationIds,
        IReadOnlyList<string> notes)
    {
        return new MarketFactCoverage(
            coverageKind,
            isCompleteForRequest,
            coveredEntryCount,
            coveredRegionIds,
            coveredLocationIds,
            notes);
    }

    private static MarketOrderEntry MapOrderEntry(MarketOrderRecord order)
    {
        return new MarketOrderEntry(
            order.OrderId,
            order.UnitPrice,
            order.RemainingVolume,
            order.MinVolume,
            order.Range,
            order.IssuedAtUtc);
    }

    private static MarketFactAnomaly CreateAnomaly(
        string code,
        MarketFactAnomalySeverity severity,
        string summary,
        bool isBlocking)
    {
        return new MarketFactAnomaly(code, severity, summary, isBlocking);
    }

    private static int? ComputeLagMinutes(DateTimeOffset? observedAtUtc, DateTimeOffset? referenceObservedAtUtc)
    {
        if (!observedAtUtc.HasValue || !referenceObservedAtUtc.HasValue)
        {
            return null;
        }

        var lag = referenceObservedAtUtc.Value - observedAtUtc.Value;
        if (lag <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Round(lag.TotalMinutes, MidpointRounding.AwayFromZero);
    }

    private static string BuildRegionScopeKey(IEnumerable<long> regionIds)
    {
        return string.Join(",", regionIds.OrderBy(regionId => regionId).Select(regionId => $"region:{regionId}"));
    }

    private static string BuildTypeScopeKey(long typeId)
    {
        return $"type:{typeId}";
    }

    private static string BuildTypeSetScopeKey(IEnumerable<long> typeIds)
    {
        return string.Join(",", typeIds.Select(typeId => $"type:{typeId}"));
    }

    private static string BuildTypeLocationScopeKey(long typeId, long locationId)
    {
        return $"type:{typeId}/location:{locationId}";
    }

    private string ResolveTypeName(long typeId)
    {
        return _metadata.TryGetType(typeId, out var type)
            ? type.Name
            : $"type:{typeId}";
    }

    private static bool IsFallbackTypeName(long typeId, string itemName)
    {
        return string.Equals(itemName, $"type:{typeId}", StringComparison.Ordinal);
    }

    private static string DescribeStoreUsage(string label, MarketFactSource source, string bundleVersion)
    {
        if (source.UsesPlaceholderData)
        {
            return $"{label} are running on bootstrap fallback data because the local fact store bundle is unavailable.";
        }

        return $"{label} now load from local store bundle '{bundleVersion}'.";
    }

    private static string NextTraceId() => Guid.NewGuid().ToString("N");
}
