using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class LocalMarketOrdersRuntime
{
    private const string OperationName = "orders-snapshot";
    private const string RunKind = "formal_orders_snapshot_import";
    private const string DefaultTriggerKind = "cli_host";
    private const string StateStoreFormat = "edenos.market_orders_runtime.v1";
    private const string FactProtectionMode = "replace_after_success_preserve_last_known_good";
    private const string FactProtectionSummary = "Orders imports only replace the local current-orders snapshot after a new payload has been parsed and written successfully, so prior fast facts remain available on import failure.";
    private const int RecentRunRetentionLimit = 20;
    private const int RecentSnapshotArtifactRetentionLimit = 10;
    private const int RecentLeaseRecoveryRetentionLimit = 10;
    private const int LockAcquireAttemptCount = 40;
    private static readonly TimeSpan LockAcquireDelay = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly int[] RetryBackoffScheduleMilliseconds = [250, 750];
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _marketFactsDirectoryPath;
    private readonly TimeProvider _timeProvider;
    private readonly MarketOrdersRuntimePaths _paths;
    private readonly MarketOrdersRuntimeStateStore _stateStore;

    public LocalMarketOrdersRuntime(
        string marketFactsDirectoryPath,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        _marketFactsDirectoryPath = Path.GetFullPath(marketFactsDirectoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paths = MarketOrdersRuntimePaths.EnsureLayout(_marketFactsDirectoryPath);
        _stateStore = new MarketOrdersRuntimeStateStore(_paths.StatePath, UtcNow);
    }

    public UseCaseResult<MarketOrdersRuntimeRunView> Import(MarketOrdersRuntimeRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("import");
        if (string.IsNullOrWhiteSpace(request.PayloadPath))
        {
            return UseCaseResult<MarketOrdersRuntimeRunView>.Failure(
                UseCaseStatus.InvalidInput,
                "market orders import requires a payload path.",
                traceId,
                ["payload_path is required."]);
        }

        var payloadPath = Path.GetFullPath(request.PayloadPath);
        if (!File.Exists(payloadPath))
        {
            return UseCaseResult<MarketOrdersRuntimeRunView>.Failure(
                UseCaseStatus.InvalidInput,
                "market orders import could not find the supplied payload file.",
                traceId,
                [$"Payload file '{payloadPath}' does not exist."]);
        }

        var runId = CreateRunId();
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_orders.import");
        var triggerKind = NormalizeTriggerKind(request.TriggerKind, DefaultTriggerKind);
        var leaseRecovery = RecoverStaleLeaseIfPossible(requestedBy);

        using var lease = TryAcquireLease(runId, requestedBy);
        if (lease is null)
        {
            return UseCaseResult<MarketOrdersRuntimeRunView>.Failure(
                UseCaseStatus.Conflict,
                "market orders import could not start because another import is already active.",
                traceId,
                ["orders runtime lease is currently held by another process."]);
        }

        return ExecuteImportWithinLease(request, payloadPath, traceId, runId, requestedBy, triggerKind, lease.LeaseId, leaseRecovery);
    }

    public UseCaseResult<MarketOrdersRuntimeStatusView> GetStatus(MarketOrdersRuntimeStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("status");
        if (request.RecentRunLimit <= 0 || request.RecentSnapshotArtifactLimit <= 0)
        {
            return UseCaseResult<MarketOrdersRuntimeStatusView>.Failure(
                UseCaseStatus.InvalidInput,
                "market orders status requires positive retention query limits.",
                traceId,
                ["recent_run_limit and recent_snapshot_artifact_limit must both be greater than zero."]);
        }

        var snapshot = _stateStore.Load();
        var currentStoreSummary = TryBuildCurrentStoreSummary(out var storeError);
        var ingressBoundaries = snapshot.State.Operations
            .OrderBy(state => state.Operation, StringComparer.Ordinal)
            .Select(state => BuildIngressBoundary(state, UtcNow(), request.WarningAgeMinutes, request.ErrorAgeMinutes))
            .ToArray();
        var notes = BuildRuntimeBoundaryNotes();
        if (!string.IsNullOrWhiteSpace(storeError))
        {
            notes = [.. notes, storeError!];
        }

        if (snapshot.State.RecentLeaseRecoveries.Count > 0)
        {
            notes = [.. notes, $"Recent lease recoveries recorded: {snapshot.State.RecentLeaseRecoveries.Count}."];
        }

        notes = [.. notes, .. BuildIngressBoundaryNotes(ingressBoundaries)];

        var data = new MarketOrdersRuntimeStatusView
        {
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            RuntimeStatePath = _paths.StatePath,
            RunLeaseLockPath = _paths.LeaseLockPath,
            QueriedAtUtc = UtcNow(),
            RuntimeRevision = snapshot.Revision,
            RuntimeUpdatedAtUtc = snapshot.UpdatedAtUtc,
            FactProtectionMode = FactProtectionMode,
            FactProtectionSummary = FactProtectionSummary,
            RetentionPolicy = BuildRetentionPolicy(),
            Lease = ObserveLease(snapshot.State.ActiveLease, request.ProbeLease),
            CurrentStoreSummary = currentStoreSummary,
            Operations = snapshot.State.Operations
                .OrderBy(state => state.Operation, StringComparer.Ordinal)
                .ToArray(),
            RecentRuns = snapshot.State.RecentRuns
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentSnapshotArtifacts = snapshot.State.RecentSnapshotArtifacts
                .Take(request.RecentSnapshotArtifactLimit)
                .ToArray(),
            RecentLeaseRecoveries = snapshot.State.RecentLeaseRecoveries
                .ToArray(),
            IngressBoundaryStatus = DetermineIngressBoundaryAggregateStatus(ingressBoundaries),
            IngressBoundaries = ingressBoundaries,
            SupportedIngressKinds = ["local_payload_file"],
            FutureWorkerBoundaryNotes = notes
        };

        return UseCaseResult<MarketOrdersRuntimeStatusView>.Success(
            data,
            "market orders runtime status queried.",
            traceId,
            warnings: notes);
    }

    public UseCaseResult<MarketOrdersRuntimeHealthView> GetHealth(MarketOrdersRuntimeHealthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("health");
        if (request.RecentRunLimit <= 0 || request.RecentSnapshotArtifactLimit <= 0)
        {
            return UseCaseResult<MarketOrdersRuntimeHealthView>.Failure(
                UseCaseStatus.InvalidInput,
                "market orders health requires positive retention query limits.",
                traceId,
                ["recent_run_limit and recent_snapshot_artifact_limit must both be greater than zero."]);
        }

        var snapshot = _stateStore.Load();
        var now = UtcNow();
        var leaseObservation = ObserveLease(snapshot.State.ActiveLease, request.ProbeLease);
        var currentStoreSummary = TryBuildCurrentStoreSummary(out var storeError);
        var operations = snapshot.State.Operations
            .OrderBy(state => state.Operation, StringComparer.Ordinal)
            .Select(state => BuildOperationHealth(state, currentStoreSummary, now, request.WarningAgeMinutes, request.ErrorAgeMinutes))
            .ToArray();

        var notes = BuildRuntimeBoundaryNotes();
        if (!string.IsNullOrWhiteSpace(storeError))
        {
            notes = [.. notes, storeError!];
        }

        if (leaseObservation.Status == "stale_orphaned")
        {
            notes = [.. notes, "The runtime state still reports an active lease, but the lease file is not currently locked. Use the formal market orders recover-lease entrypoint after confirming no active import is still running."];
        }

        if (snapshot.State.RecentLeaseRecoveries.Count > 0)
        {
            notes = [.. notes, $"Recent lease recoveries recorded: {snapshot.State.RecentLeaseRecoveries.Count}."];
        }

        if (currentStoreSummary?.StaleScopeCount > 0)
        {
            notes = [.. notes, $"The local current-orders store is retaining {currentStoreSummary.StaleScopeCount} older pair snapshot(s) as fallback while fresher snapshots exist for the same location."];
        }

        if (currentStoreSummary?.ScopeCount == 0)
        {
            notes = [.. notes, "The local current-orders store is currently empty, so read-side fast facts would have no snapshot-backed coverage."];
        }

        notes = [.. notes, .. BuildIngressBoundaryNotes(operations.Select(operation => operation.IngressBoundary).ToArray())];

        var overallStatus = DetermineOverallHealthStatus(leaseObservation, currentStoreSummary, operations, request.WarningAgeMinutes, request.ErrorAgeMinutes);
        var data = new MarketOrdersRuntimeHealthView
        {
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            RuntimeStatePath = _paths.StatePath,
            RunLeaseLockPath = _paths.LeaseLockPath,
            EvaluatedAtUtc = now,
            OverallStatus = overallStatus,
            IsHealthy = overallStatus == "healthy",
            WarningAgeMinutes = request.WarningAgeMinutes,
            ErrorAgeMinutes = request.ErrorAgeMinutes,
            FactProtectionMode = FactProtectionMode,
            FactProtectionSummary = FactProtectionSummary,
            RetentionPolicy = BuildRetentionPolicy(),
            Lease = leaseObservation,
            CurrentStoreSummary = currentStoreSummary,
            Operations = operations,
            RecentRuns = snapshot.State.RecentRuns
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentSnapshotArtifacts = snapshot.State.RecentSnapshotArtifacts
                .Take(request.RecentSnapshotArtifactLimit)
                .ToArray(),
            RecentLeaseRecoveries = snapshot.State.RecentLeaseRecoveries
                .ToArray(),
            IngressBoundaryStatus = DetermineIngressBoundaryAggregateStatus(operations.Select(operation => operation.IngressBoundary).ToArray()),
            IngressBoundaries = operations.Select(operation => operation.IngressBoundary).ToArray(),
            SupportedIngressKinds = ["local_payload_file"],
            FutureWorkerBoundaryNotes = notes
        };

        return UseCaseResult<MarketOrdersRuntimeHealthView>.Success(
            data,
            $"market orders health is {overallStatus}.",
            traceId,
            warnings: notes);
    }

    public UseCaseResult<MarketOrdersRuntimeLeaseRecoveryView> RecoverLease(MarketOrdersRuntimeLeaseRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("recover-lease");
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_orders.recover_lease");
        var snapshot = _stateStore.Load();
        var observation = ObserveLease(snapshot.State.ActiveLease, request.ProbeLease);

        if (observation.ActiveLease is null)
        {
            return UseCaseResult<MarketOrdersRuntimeLeaseRecoveryView>.Success(
                new MarketOrdersRuntimeLeaseRecoveryView
                {
                    RecoveryId = CreateRecoveryId(),
                    Status = "no_recovery_needed",
                    RequestedBy = requestedBy,
                    ObservedAtUtc = UtcNow(),
                    RecoveryReason = "No persisted active lease exists for this market-facts directory.",
                    LockHeld = observation.LockHeld,
                    Lease = null,
                    Notes = Array.Empty<string>()
                },
                "market orders lease recovery found no stale lease state.",
                traceId);
        }

        if (observation.LockHeld)
        {
            return UseCaseResult<MarketOrdersRuntimeLeaseRecoveryView>.Failure(
                UseCaseStatus.Conflict,
                "market orders lease recovery refused because the lease file is currently held by another process.",
                traceId,
                ["The lease file is locked, so recovery would risk interrupting an active orders import."]);
        }

        var recovery = PersistLeaseRecovery(observation.ActiveLease, requestedBy);
        PerformRetentionCleanup();

        return UseCaseResult<MarketOrdersRuntimeLeaseRecoveryView>.Success(
            recovery,
            "market orders stale lease state recovered.",
            traceId,
            recovery.Notes);
    }

    public UseCaseResult<MarketOrdersRuntimeCleanupView> Cleanup(MarketOrdersRuntimeCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("cleanup");
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_orders.cleanup");
        _stateStore.Update(TrimStateToRetentionLimits);
        var summary = PerformRetentionCleanup();

        var data = new MarketOrdersRuntimeCleanupView
        {
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            RequestedBy = requestedBy,
            CleanedAtUtc = summary.CleanedAtUtc,
            RetentionPolicy = BuildRetentionPolicy(),
            DeletedRunLogs = summary.DeletedRunLogs,
            DeletedSnapshotArtifacts = summary.DeletedSnapshotArtifacts,
            RetainedRunLogs = summary.RetainedRunLogs,
            RetainedSnapshotArtifacts = summary.RetainedSnapshotArtifacts,
            RetainedLeaseRecoveries = summary.RetainedLeaseRecoveries,
            Notes = summary.Notes
        };

        return UseCaseResult<MarketOrdersRuntimeCleanupView>.Success(
            data,
            "market orders retention cleanup completed.",
            traceId,
            warnings: data.Notes);
    }

    private UseCaseResult<MarketOrdersRuntimeRunView> ExecuteImportWithinLease(
        MarketOrdersRuntimeRunRequest request,
        string payloadPath,
        string traceId,
        string runId,
        string requestedBy,
        string triggerKind,
        string leaseId,
        MarketOrdersRuntimeLeaseRecoveryView? leaseRecovery)
    {
        var startedAtUtc = UtcNow();
        var runLogPath = Path.Combine(_paths.RunDirectoryPath, $"{runId}.json");
        var payloadMetadata = ObservePayloadFileMetadata(payloadPath);
        var attemptFailures = new List<MarketOrdersRuntimeAttemptFailureView>();
        var retryDelaysMs = new List<int>();
        MarketOrdersImportExecutionResult? execution = null;
        MarketOrdersRuntimeFailureView? failure = null;
        var attemptCount = 0;

        for (var attemptIndex = 0; attemptIndex < RetryBackoffScheduleMilliseconds.Length + 1; attemptIndex++)
        {
            attemptCount = attemptIndex + 1;

            try
            {
                execution = ExecuteImport(payloadPath, runId);
                failure = null;
                break;
            }
            catch (Exception ex)
            {
                var classification = ClassifyFailure(ex);
                failure = new MarketOrdersRuntimeFailureView
                {
                    Category = classification.Category,
                    ReasonCode = classification.ReasonCode,
                    Error = ex.Message,
                    Retryable = classification.Retryable,
                    AttemptCount = attemptCount,
                    OccurredAtUtc = UtcNow()
                };

                attemptFailures.Add(new MarketOrdersRuntimeAttemptFailureView
                {
                    Attempt = attemptCount,
                    Category = classification.Category,
                    ReasonCode = classification.ReasonCode,
                    Error = ex.Message
                });

                if (!classification.Retryable || attemptIndex >= RetryBackoffScheduleMilliseconds.Length)
                {
                    break;
                }

                var retryDelayMs = RetryBackoffScheduleMilliseconds[attemptIndex];
                retryDelaysMs.Add(retryDelayMs);
                Thread.Sleep(retryDelayMs);
            }
        }

        var completedAtUtc = UtcNow();
        var warnings = new List<string>();

        if (leaseRecovery is not null)
        {
            warnings.Add("A stale lease state was recovered before this orders import started.");
        }

        if (execution is not null)
        {
            if (execution.StoreSummary.StaleScopeCount > 0)
            {
                warnings.Add($"Retained {execution.StoreSummary.StaleScopeCount} older pair snapshot(s) as fast-fact fallback within the refreshed local orders store.");
            }

            var run = new MarketOrdersRuntimeRunView
            {
                TraceId = traceId,
                RunId = runId,
                RunKind = RunKind,
                TriggerKind = triggerKind,
                Operation = OperationName,
                Status = "completed",
                RequestedBy = requestedBy,
                Cursor = request.Cursor,
                IngressKind = "local_payload_file",
                PayloadPath = payloadPath,
                PayloadFileModifiedAtUtc = payloadMetadata.LastModifiedAtUtc,
                PayloadFileSizeBytes = payloadMetadata.SizeBytes,
                LeaseId = leaseId,
                AttemptCount = attemptCount,
                RetryDelaysMs = retryDelaysMs,
                AttemptFailures = attemptFailures,
                MarketFactsDirectoryPath = _marketFactsDirectoryPath,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                RunLogPath = runLogPath,
                ImportResult = execution.ImportResult,
                SnapshotArtifact = execution.SnapshotArtifact,
                Source = execution.ImportResult.Source,
                BundleVersion = execution.ImportResult.BundleVersion,
                DataProtectionStatus = "existing_local_orders_preserved_until_successful_replace",
                Errors = Array.Empty<string>(),
                Failure = null,
                LeaseRecovery = leaseRecovery,
                Notes = warnings
            };

            WriteJsonAtomically(runLogPath, run);
            _stateStore.Update(state => UpdateStateAfterRun(state, run, execution.StoreSummary, wasSuccessful: true));

            return UseCaseResult<MarketOrdersRuntimeRunView>.Success(
                run,
                $"market orders import completed with {execution.ImportResult.ScopeUpserts} scope upsert(s).",
                traceId,
                warnings);
        }

        var errorMessages = failure is null
            ? new[] { "market orders import failed unexpectedly." }
            : new[] { failure.Error };
        var failedRun = new MarketOrdersRuntimeRunView
        {
            TraceId = traceId,
            RunId = runId,
            RunKind = RunKind,
            TriggerKind = triggerKind,
            Operation = OperationName,
            Status = "failed",
            RequestedBy = requestedBy,
            Cursor = request.Cursor,
            IngressKind = "local_payload_file",
            PayloadPath = payloadPath,
            PayloadFileModifiedAtUtc = payloadMetadata.LastModifiedAtUtc,
            PayloadFileSizeBytes = payloadMetadata.SizeBytes,
            LeaseId = leaseId,
            AttemptCount = attemptCount,
            RetryDelaysMs = retryDelaysMs,
            AttemptFailures = attemptFailures,
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RunLogPath = runLogPath,
            ImportResult = null,
            SnapshotArtifact = null,
            Source = null,
            BundleVersion = null,
            DataProtectionStatus = "existing_local_orders_left_unchanged_after_import_failure",
            Errors = errorMessages,
            Failure = failure,
            LeaseRecovery = leaseRecovery,
            Notes = warnings
                .Append("The local current-orders store was not replaced because the import did not complete successfully.")
                .ToArray()
        };

        WriteJsonAtomically(runLogPath, failedRun);
        _stateStore.Update(state => UpdateStateAfterRun(state, failedRun, storeSummary: null, wasSuccessful: false));

        return new UseCaseResult<MarketOrdersRuntimeRunView>
        {
            Status = UseCaseStatus.Error,
            Summary = "market orders import failed.",
            Data = failedRun,
            Warnings = failedRun.Notes,
            Errors = errorMessages,
            TraceId = traceId
        };
    }

    private MarketOrdersImportExecutionResult ExecuteImport(string payloadPath, string runId)
    {
        var batch = MarketFactFileCodec.ReadOrderSnapshotImportBatch(payloadPath);
        var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
        var retainedSnapshots = MarketFactFileCodec.ReadOrderSnapshots(layout.CurrentOrdersPath)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        foreach (var snapshot in batch.Snapshots)
        {
            retainedSnapshots[(snapshot.TypeId, snapshot.LocationId)] = snapshot;
        }

        var bundleVersion = CreateBundleVersion("orders-import");
        var snapshotArtifactPath = Path.Combine(_paths.SnapshotDirectoryPath, $"{runId}.json");
        MarketFactFileCodec.WriteOrderSnapshots(snapshotArtifactPath, retainedSnapshots);
        MarketFactFileCodec.WriteOrderSnapshots(layout.CurrentOrdersPath, retainedSnapshots);
        MarketFactsDataPaths.WriteManifest(layout, bundleVersion);

        var storeSummary = BuildStoreSummary(retainedSnapshots.Values);
        var importResult = new MarketOrdersImportResult
        {
            Source = batch.Source,
            BundleVersion = bundleVersion,
            ScopeUpserts = batch.Snapshots.Count,
            SellOrderRowsWritten = batch.Snapshots.Sum(snapshot => snapshot.SellOrders.Count),
            BuyOrderRowsWritten = batch.Snapshots.Sum(snapshot => snapshot.BuyOrders.Count),
            RetainedScopeCount = storeSummary.ScopeCount,
            RetainedLocationCount = storeSummary.LocationCount,
            LatestObservedAtUtc = storeSummary.LatestObservedAtUtc,
            OldestObservedAtUtc = storeSummary.OldestObservedAtUtc,
            StaleScopeCount = storeSummary.StaleScopeCount,
            MaxRetainedLagMinutes = storeSummary.MaxRetainedLagMinutes
        };

        var snapshotArtifact = new MarketOrdersRuntimeSnapshotArtifactView
        {
            RunId = runId,
            SnapshotArtifactPath = snapshotArtifactPath,
            CapturedAtUtc = UtcNow(),
            ScopeCount = storeSummary.ScopeCount,
            LocationCount = storeSummary.LocationCount,
            LatestObservedAtUtc = storeSummary.LatestObservedAtUtc,
            OldestObservedAtUtc = storeSummary.OldestObservedAtUtc
        };

        return new MarketOrdersImportExecutionResult(importResult, snapshotArtifact, storeSummary);
    }

    private MarketOrdersRuntimeOperationHealthView BuildOperationHealth(
        MarketOrdersRuntimeOperationState state,
        MarketOrdersRuntimeStoreSummaryView? currentStoreSummary,
        DateTimeOffset now,
        int warningAgeMinutes,
        int errorAgeMinutes)
    {
        var latestObservedAtUtc = currentStoreSummary?.LatestObservedAtUtc ?? state.LatestObservedAtUtc;
        var latestSnapshotAgeMinutes = ComputeAgeMinutes(latestObservedAtUtc, now);
        var staleScopeCount = currentStoreSummary?.StaleScopeCount ?? state.LastStaleScopeCount;
        var maxRetainedLagMinutes = currentStoreSummary?.MaxRetainedLagMinutes ?? state.LastMaxRetainedLagMinutes;
        var ingressBoundary = BuildIngressBoundary(state, now, warningAgeMinutes, errorAgeMinutes);

        return new MarketOrdersRuntimeOperationHealthView
        {
            Operation = state.Operation,
            Status = DetermineOperationHealthStatus(
                state,
                ingressBoundary.Status,
                latestSnapshotAgeMinutes,
                staleScopeCount,
                warningAgeMinutes,
                errorAgeMinutes),
            LastRunStatus = state.LastStatus,
            LastRunId = state.LastRunId,
            LastRequestedCursor = state.LastRequestedCursor,
            LastSucceededCursor = state.LastSucceededCursor,
            LastStartedAtUtc = state.LastStartedAtUtc,
            LastCompletedAtUtc = state.LastCompletedAtUtc,
            LastSucceededAtUtc = state.LastSucceededAtUtc,
            LastFailedAtUtc = state.LastFailedAtUtc,
            LatestSnapshotAgeMinutes = latestSnapshotAgeMinutes,
            ConsecutiveFailures = state.ConsecutiveFailures,
            LastError = state.LastError,
            LastAttemptCount = state.LastAttemptCount,
            LastBundleVersion = state.LastBundleVersion,
            LastSource = state.LastSource,
            LastScopeUpserts = state.LastScopeUpserts,
            LastRetainedScopeCount = currentStoreSummary?.ScopeCount ?? state.LastRetainedScopeCount,
            LastRetainedLocationCount = currentStoreSummary?.LocationCount ?? state.LastRetainedLocationCount,
            LatestObservedAtUtc = latestObservedAtUtc,
            OldestObservedAtUtc = currentStoreSummary?.OldestObservedAtUtc ?? state.OldestObservedAtUtc,
            StaleScopeCount = staleScopeCount,
            MaxRetainedLagMinutes = maxRetainedLagMinutes,
            LastSnapshotArtifactPath = state.LastSnapshotArtifactPath,
            LastDataProtectionStatus = state.LastDataProtectionStatus,
            LastTraceId = state.LastTraceId,
            IngressBoundary = ingressBoundary
        };
    }

    private string DetermineOverallHealthStatus(
        MarketOrdersRuntimeLeaseObservationView leaseObservation,
        MarketOrdersRuntimeStoreSummaryView? currentStoreSummary,
        IReadOnlyList<MarketOrdersRuntimeOperationHealthView> operations,
        int warningAgeMinutes,
        int errorAgeMinutes)
    {
        if (leaseObservation.Status == "stale_orphaned")
        {
            return "warning";
        }

        if (currentStoreSummary is null)
        {
            return "unhealthy";
        }

        if (currentStoreSummary.LatestObservedAtUtc is { } latestObservedAtUtc)
        {
            var ageMinutes = ComputeAgeMinutes(latestObservedAtUtc, UtcNow()) ?? 0;
            if (ageMinutes >= errorAgeMinutes)
            {
                return "unhealthy";
            }

            if (ageMinutes >= warningAgeMinutes)
            {
                return "warning";
            }
        }

        if (operations.Any(operation => operation.Status == "unhealthy"))
        {
            return "unhealthy";
        }

        if (operations.Any(operation => operation.Status == "warning"))
        {
            return "warning";
        }

        return currentStoreSummary.ScopeCount == 0 ? "warning" : "healthy";
    }

    private static string DetermineOperationHealthStatus(
        MarketOrdersRuntimeOperationState state,
        string ingressBoundaryStatus,
        long? latestSnapshotAgeMinutes,
        int staleScopeCount,
        int warningAgeMinutes,
        int errorAgeMinutes)
    {
        if (string.Equals(ingressBoundaryStatus, "unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            return "unhealthy";
        }

        if (latestSnapshotAgeMinutes.HasValue && latestSnapshotAgeMinutes.Value >= errorAgeMinutes)
        {
            return "unhealthy";
        }

        if (state.ConsecutiveFailures > 0)
        {
            return "warning";
        }

        if (latestSnapshotAgeMinutes.HasValue && latestSnapshotAgeMinutes.Value >= warningAgeMinutes)
        {
            return "warning";
        }

        if (string.Equals(ingressBoundaryStatus, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        if (staleScopeCount > 0)
        {
            return "warning";
        }

        if (!state.LastSucceededAtUtc.HasValue)
        {
            return "warning";
        }

        return "healthy";
    }

    private MarketOrdersRuntimeIngressBoundaryView BuildIngressBoundary(
        MarketOrdersRuntimeOperationState state,
        DateTimeOffset now,
        int warningAgeMinutes,
        int errorAgeMinutes)
    {
        var payloadPath = string.IsNullOrWhiteSpace(state.LastPayloadPath)
            ? null
            : Path.GetFullPath(state.LastPayloadPath);

        if (string.IsNullOrWhiteSpace(payloadPath))
        {
            return new MarketOrdersRuntimeIngressBoundaryView
            {
                Operation = state.Operation,
                IngressKind = "local_payload_file",
                Status = "unknown",
                Summary = "No payload file has been recorded for this runtime operation yet.",
                PayloadPath = null,
                PayloadExists = false,
                PayloadLastModifiedAtUtc = null,
                PayloadAgeMinutes = null,
                PayloadSizeBytes = null,
                PayloadAdvancedSinceLastSuccess = null,
                LastSucceededPayloadLastModifiedAtUtc = state.LastSucceededPayloadFileModifiedAtUtc,
                LastSucceededPayloadSizeBytes = state.LastSucceededPayloadFileSizeBytes,
                Notes =
                [
                    "Run at least one successful orders import before expecting runtime-only ingress freshness diagnosis."
                ]
            };
        }

        var payloadMetadata = ObservePayloadFileMetadata(payloadPath);
        if (!payloadMetadata.Exists)
        {
            return new MarketOrdersRuntimeIngressBoundaryView
            {
                Operation = state.Operation,
                IngressKind = "local_payload_file",
                Status = "unhealthy",
                Summary = "The configured local payload file is missing at the orders ingress boundary.",
                PayloadPath = payloadPath,
                PayloadExists = false,
                PayloadLastModifiedAtUtc = null,
                PayloadAgeMinutes = null,
                PayloadSizeBytes = null,
                PayloadAdvancedSinceLastSuccess = null,
                LastSucceededPayloadLastModifiedAtUtc = state.LastSucceededPayloadFileModifiedAtUtc,
                LastSucceededPayloadSizeBytes = state.LastSucceededPayloadFileSizeBytes,
                Notes =
                [
                    "The service host can still heartbeat while the ingress boundary has no readable payload file to consume."
                ]
            };
        }

        var payloadAgeMinutes = ComputeAgeMinutes(payloadMetadata.LastModifiedAtUtc, now);
        var payloadAdvancedSinceLastSuccess = HasPayloadAdvancedSinceLastSuccess(
            payloadMetadata.LastModifiedAtUtc,
            payloadMetadata.SizeBytes,
            state.LastSucceededPayloadFileModifiedAtUtc,
            state.LastSucceededPayloadFileSizeBytes);
        var status = DetermineIngressBoundaryStatus(payloadAgeMinutes, warningAgeMinutes, errorAgeMinutes);

        return new MarketOrdersRuntimeIngressBoundaryView
        {
            Operation = state.Operation,
            IngressKind = "local_payload_file",
            Status = status,
            Summary = BuildIngressBoundarySummary(status, payloadAgeMinutes, payloadAdvancedSinceLastSuccess),
            PayloadPath = payloadMetadata.Path,
            PayloadExists = true,
            PayloadLastModifiedAtUtc = payloadMetadata.LastModifiedAtUtc,
            PayloadAgeMinutes = payloadAgeMinutes,
            PayloadSizeBytes = payloadMetadata.SizeBytes,
            PayloadAdvancedSinceLastSuccess = payloadAdvancedSinceLastSuccess,
            LastSucceededPayloadLastModifiedAtUtc = state.LastSucceededPayloadFileModifiedAtUtc,
            LastSucceededPayloadSizeBytes = state.LastSucceededPayloadFileSizeBytes,
            Notes = BuildIngressBoundaryDetailNotes(payloadAgeMinutes, payloadAdvancedSinceLastSuccess)
        };
    }

    private static string DetermineIngressBoundaryStatus(
        long? payloadAgeMinutes,
        int warningAgeMinutes,
        int errorAgeMinutes)
    {
        if (payloadAgeMinutes.HasValue && payloadAgeMinutes.Value >= errorAgeMinutes)
        {
            return "unhealthy";
        }

        if (payloadAgeMinutes.HasValue && payloadAgeMinutes.Value >= warningAgeMinutes)
        {
            return "warning";
        }

        return "healthy";
    }

    private static string BuildIngressBoundarySummary(
        string status,
        long? payloadAgeMinutes,
        bool? payloadAdvancedSinceLastSuccess)
    {
        var ageText = FormatAgeMinutes(payloadAgeMinutes);
        if (payloadAdvancedSinceLastSuccess == false)
        {
            return $"The configured payload file has not advanced since the last successful import and is {ageText} old.";
        }

        if (payloadAdvancedSinceLastSuccess == true)
        {
            return $"The configured payload file is {ageText} old and has advanced since the last successful import.";
        }

        return $"The configured payload file is {ageText} old at the ingress boundary and runtime history is still converging.";
    }

    private static IReadOnlyList<string> BuildIngressBoundaryDetailNotes(
        long? payloadAgeMinutes,
        bool? payloadAdvancedSinceLastSuccess)
    {
        var notes = new List<string>();
        if (payloadAdvancedSinceLastSuccess == false)
        {
            notes.Add("The local ingress boundary is still pointing at the same payload file version as the last successful orders import.");
        }

        if (payloadAgeMinutes.HasValue)
        {
            notes.Add($"Current payload-file age at the ingress boundary: {payloadAgeMinutes.Value} minute(s).");
        }

        return notes;
    }

    private static bool? HasPayloadAdvancedSinceLastSuccess(
        DateTimeOffset? payloadLastModifiedAtUtc,
        long? payloadSizeBytes,
        DateTimeOffset? lastSucceededPayloadLastModifiedAtUtc,
        long? lastSucceededPayloadSizeBytes)
    {
        if (!payloadLastModifiedAtUtc.HasValue
            || (!lastSucceededPayloadLastModifiedAtUtc.HasValue && !lastSucceededPayloadSizeBytes.HasValue))
        {
            return null;
        }

        var sameTimestamp = lastSucceededPayloadLastModifiedAtUtc.HasValue
            && payloadLastModifiedAtUtc.Value == lastSucceededPayloadLastModifiedAtUtc.Value;
        var sameSize = lastSucceededPayloadSizeBytes.HasValue
            && payloadSizeBytes.HasValue
            && payloadSizeBytes.Value == lastSucceededPayloadSizeBytes.Value;

        if (lastSucceededPayloadLastModifiedAtUtc.HasValue && lastSucceededPayloadSizeBytes.HasValue)
        {
            return !(sameTimestamp && sameSize);
        }

        if (lastSucceededPayloadLastModifiedAtUtc.HasValue)
        {
            return !sameTimestamp;
        }

        return !sameSize;
    }

    private MarketOrdersRuntimeStoreSummaryView? TryBuildCurrentStoreSummary(out string? error)
    {
        try
        {
            var layout = MarketFactsDataPaths.EnsureLayoutInDirectory(_marketFactsDirectoryPath);
            var snapshots = MarketFactFileCodec.ReadOrderSnapshots(layout.CurrentOrdersPath);
            error = null;
            return BuildStoreSummary(snapshots.Values);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error = $"Unable to summarize the local current-orders store: {ex.Message}";
            return null;
        }
    }

    private static MarketOrdersRuntimeStoreSummaryView BuildStoreSummary(IEnumerable<MarketOrderSnapshotRecord> snapshots)
    {
        var snapshotArray = snapshots.ToArray();
        if (snapshotArray.Length == 0)
        {
            return new MarketOrdersRuntimeStoreSummaryView
            {
                ScopeCount = 0,
                LocationCount = 0,
                LatestObservedAtUtc = null,
                OldestObservedAtUtc = null,
                StaleScopeCount = 0,
                MaxRetainedLagMinutes = 0
            };
        }

        var latestObservedByLocation = snapshotArray
            .GroupBy(snapshot => snapshot.LocationId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(snapshot => snapshot.ObservedAtUtc));

        var lagMinutes = snapshotArray
            .Select(snapshot => ComputeLagMinutes(snapshot.ObservedAtUtc, latestObservedByLocation[snapshot.LocationId]) ?? 0)
            .ToArray();

        return new MarketOrdersRuntimeStoreSummaryView
        {
            ScopeCount = snapshotArray.Length,
            LocationCount = snapshotArray.Select(snapshot => snapshot.LocationId).Distinct().Count(),
            LatestObservedAtUtc = snapshotArray.Max(snapshot => snapshot.ObservedAtUtc),
            OldestObservedAtUtc = snapshotArray.Min(snapshot => snapshot.ObservedAtUtc),
            StaleScopeCount = lagMinutes.Count(value => value > 0),
            MaxRetainedLagMinutes = lagMinutes.Length == 0 ? 0 : lagMinutes.Max()
        };
    }

    private MarketOrdersRuntimeState UpdateStateAfterRun(
        MarketOrdersRuntimeState state,
        MarketOrdersRuntimeRunView run,
        MarketOrdersRuntimeStoreSummaryView? storeSummary,
        bool wasSuccessful)
    {
        var operationState = state.Operations.FirstOrDefault(existing => string.Equals(existing.Operation, OperationName, StringComparison.Ordinal))
            ?? new MarketOrdersRuntimeOperationState
            {
                Operation = OperationName
            };

        var updatedOperation = operationState with
        {
            LastRunId = run.RunId,
            LastStatus = run.Status,
            LastRequestedCursor = run.Cursor,
            LastSucceededCursor = wasSuccessful ? run.Cursor : operationState.LastSucceededCursor,
            LastPayloadPath = run.PayloadPath,
            LastPayloadFileModifiedAtUtc = run.PayloadFileModifiedAtUtc,
            LastPayloadFileSizeBytes = run.PayloadFileSizeBytes,
            LastStartedAtUtc = run.StartedAtUtc,
            LastCompletedAtUtc = run.CompletedAtUtc,
            LastSucceededAtUtc = wasSuccessful ? run.CompletedAtUtc : operationState.LastSucceededAtUtc,
            LastSucceededPayloadFileModifiedAtUtc = wasSuccessful ? run.PayloadFileModifiedAtUtc : operationState.LastSucceededPayloadFileModifiedAtUtc,
            LastSucceededPayloadFileSizeBytes = wasSuccessful ? run.PayloadFileSizeBytes : operationState.LastSucceededPayloadFileSizeBytes,
            LastFailedAtUtc = wasSuccessful ? operationState.LastFailedAtUtc : run.CompletedAtUtc,
            ConsecutiveFailures = wasSuccessful ? 0 : operationState.ConsecutiveFailures + 1,
            LastError = wasSuccessful ? null : run.Failure?.Error ?? string.Join(" | ", run.Errors),
            LastAttemptCount = run.AttemptCount,
            LastBundleVersion = run.BundleVersion,
            LastSource = run.Source,
            LastScopeUpserts = run.ImportResult?.ScopeUpserts ?? operationState.LastScopeUpserts,
            LastRetainedScopeCount = storeSummary?.ScopeCount ?? operationState.LastRetainedScopeCount,
            LastRetainedLocationCount = storeSummary?.LocationCount ?? operationState.LastRetainedLocationCount,
            LatestObservedAtUtc = storeSummary?.LatestObservedAtUtc ?? operationState.LatestObservedAtUtc,
            OldestObservedAtUtc = storeSummary?.OldestObservedAtUtc ?? operationState.OldestObservedAtUtc,
            LastStaleScopeCount = storeSummary?.StaleScopeCount ?? operationState.LastStaleScopeCount,
            LastMaxRetainedLagMinutes = storeSummary?.MaxRetainedLagMinutes ?? operationState.LastMaxRetainedLagMinutes,
            LastSnapshotArtifactPath = run.SnapshotArtifact?.SnapshotArtifactPath ?? operationState.LastSnapshotArtifactPath,
            LastDataProtectionStatus = run.DataProtectionStatus,
            LastTraceId = run.TraceId
        };

        var recentRuns = new[] { run }
            .Concat(state.RecentRuns.Where(existing => !string.Equals(existing.RunId, run.RunId, StringComparison.Ordinal)))
            .Take(RecentRunRetentionLimit)
            .ToArray();

        var recentSnapshotArtifacts = run.SnapshotArtifact is null
            ? state.RecentSnapshotArtifacts
            : new[] { run.SnapshotArtifact }
                .Concat(state.RecentSnapshotArtifacts.Where(existing => !string.Equals(existing.RunId, run.RunId, StringComparison.Ordinal)))
                .Take(RecentSnapshotArtifactRetentionLimit)
                .ToArray();

        return state with
        {
            Operations = [updatedOperation],
            RecentRuns = recentRuns,
            RecentSnapshotArtifacts = recentSnapshotArtifacts
        };
    }

    private MarketOrdersRuntimeState TrimStateToRetentionLimits(MarketOrdersRuntimeState state)
    {
        return state with
        {
            RecentRuns = state.RecentRuns.Take(RecentRunRetentionLimit).ToArray(),
            RecentSnapshotArtifacts = state.RecentSnapshotArtifacts.Take(RecentSnapshotArtifactRetentionLimit).ToArray(),
            RecentLeaseRecoveries = state.RecentLeaseRecoveries.Take(RecentLeaseRecoveryRetentionLimit).ToArray()
        };
    }

    private MarketOrdersRuntimeRetentionCleanupSummary PerformRetentionCleanup()
    {
        var snapshot = _stateStore.Load();
        var retainedRunLogPaths = snapshot.State.RecentRuns
            .Select(run => run.RunLogPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedSnapshotArtifactPaths = snapshot.State.RecentSnapshotArtifacts
            .Select(artifact => artifact.SnapshotArtifactPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedRunLogs = DeleteOrphanedJsonFiles(_paths.RunDirectoryPath, retainedRunLogPaths);
        var deletedSnapshotArtifacts = DeleteOrphanedJsonFiles(_paths.SnapshotDirectoryPath, retainedSnapshotArtifactPaths);
        var notes = new List<string>();

        if (deletedRunLogs > 0)
        {
            notes.Add($"Deleted {deletedRunLogs} orphaned orders run log artifact(s).");
        }

        if (deletedSnapshotArtifacts > 0)
        {
            notes.Add($"Deleted {deletedSnapshotArtifacts} snapshot archive artifact(s) that were outside the retained runtime window.");
        }

        return new MarketOrdersRuntimeRetentionCleanupSummary(
            UtcNow(),
            deletedRunLogs,
            deletedSnapshotArtifacts,
            retainedRunLogPaths.Count,
            retainedSnapshotArtifactPaths.Count,
            snapshot.State.RecentLeaseRecoveries.Count,
            notes);
    }

    private static int DeleteOrphanedJsonFiles(string directoryPath, IReadOnlySet<string> retainedPaths)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var filePath in Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (retainedPaths.Contains(Path.GetFullPath(filePath)))
            {
                continue;
            }

            File.Delete(filePath);
            deleted++;
        }

        return deleted;
    }

    private MarketOrdersRuntimeLeaseRecoveryView? RecoverStaleLeaseIfPossible(string requestedBy)
    {
        var snapshot = _stateStore.Load();
        var observation = ObserveLease(snapshot.State.ActiveLease, probeLease: true);
        if (observation.ActiveLease is null || observation.LockHeld)
        {
            return null;
        }

        return PersistLeaseRecovery(observation.ActiveLease, requestedBy);
    }

    private MarketOrdersRuntimeLeaseRecoveryView PersistLeaseRecovery(MarketOrdersRuntimeLeaseState staleLease, string requestedBy)
    {
        var recoveredAtUtc = UtcNow();
        var recovery = new MarketOrdersRuntimeLeaseRecoveryView
        {
            RecoveryId = CreateRecoveryId(),
            Status = "recovered_stale_lease",
            RequestedBy = requestedBy,
            ObservedAtUtc = recoveredAtUtc,
            RecoveredAtUtc = recoveredAtUtc,
            RecoveryReason = "The runtime state reported an active lease, but the lease file was no longer locked.",
            LockHeld = false,
            Lease = staleLease,
            Notes =
            [
                "The stale lease state was cleared before the next orders action started.",
                "Existing local current-orders facts were not deleted during lease recovery."
            ]
        };

        _stateStore.Update(state =>
        {
            if (!string.Equals(state.ActiveLease?.LeaseId, staleLease.LeaseId, StringComparison.Ordinal))
            {
                return state;
            }

            return TrimStateToRetentionLimits(state with
            {
                ActiveLease = null,
                RecentLeaseRecoveries = state.RecentLeaseRecoveries
                    .Prepend(recovery)
                    .Take(RecentLeaseRecoveryRetentionLimit)
                    .ToArray()
            });
        });

        return recovery;
    }

    private LeaseHandle? TryAcquireLease(string ownerId, string requestedBy)
    {
        for (var attempt = 0; attempt < LockAcquireAttemptCount; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    _paths.LeaseLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                var leaseState = new MarketOrdersRuntimeLeaseState
                {
                    LeaseId = $"orders-lease-{Guid.NewGuid():N}",
                    OwnerId = ownerId,
                    RequestedBy = requestedBy,
                    AcquiredAtUtc = UtcNow(),
                    ExpiresAtUtc = UtcNow().Add(LeaseDuration)
                };

                _stateStore.Update(state => state with { ActiveLease = leaseState });
                return new LeaseHandle(stream, _stateStore, leaseState);
            }
            catch (IOException)
            {
                Thread.Sleep(LockAcquireDelay);
            }
        }

        return null;
    }

    private MarketOrdersRuntimeLeaseObservationView ObserveLease(MarketOrdersRuntimeLeaseState? activeLease, bool probeLease)
    {
        var lockHeld = probeLease && IsLockHeld();
        var now = UtcNow();

        var status = activeLease switch
        {
            null when lockHeld => "active_without_state",
            null => "idle",
            _ when lockHeld && activeLease.ExpiresAtUtc < now => "expired_but_locked",
            _ when lockHeld => "active",
            _ => "stale_orphaned"
        };

        return new MarketOrdersRuntimeLeaseObservationView
        {
            Status = status,
            LockHeld = lockHeld,
            ActiveLease = activeLease
        };
    }

    private bool IsLockHeld()
    {
        try
        {
            using var stream = new FileStream(
                _paths.LeaseLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private MarketOrdersRuntimeFailureClassification ClassifyFailure(Exception ex)
    {
        return ex switch
        {
            IOException => new MarketOrdersRuntimeFailureClassification("transient", "io_failure", Retryable: true),
            JsonException => new MarketOrdersRuntimeFailureClassification("permanent", "invalid_json_payload", Retryable: false),
            InvalidOperationException => new MarketOrdersRuntimeFailureClassification("permanent", "invalid_payload_shape", Retryable: false),
            FormatException => new MarketOrdersRuntimeFailureClassification("permanent", "invalid_payload_value", Retryable: false),
            _ => new MarketOrdersRuntimeFailureClassification("transient", "unexpected_runtime_error", Retryable: true)
        };
    }

    private static MarketOrdersRuntimeRetentionPolicyView BuildRetentionPolicy()
    {
        return new MarketOrdersRuntimeRetentionPolicyView
        {
            RecentRunLimit = RecentRunRetentionLimit,
            RecentSnapshotArtifactLimit = RecentSnapshotArtifactRetentionLimit,
            RecentLeaseRecoveryLimit = RecentLeaseRecoveryRetentionLimit
        };
    }

    private IReadOnlyList<string> BuildRuntimeBoundaryNotes()
    {
        return
        [
            "The current orders runtime only supports local payload-file ingress; it does not claim to be a CCP/ESI-facing worker yet.",
            "The host can keep heartbeating while the local payload file stops advancing, so ingress freshness must be checked separately from process liveness.",
            "A future orders worker can reuse the same runtime state, archive retention, and health surface without pushing long-running responsibility back into the CLI read side."
        ];
    }

    private static IReadOnlyList<string> BuildIngressBoundaryNotes(IReadOnlyList<MarketOrdersRuntimeIngressBoundaryView> ingressBoundaries)
    {
        var notes = new List<string>();
        foreach (var boundary in ingressBoundaries)
        {
            if (string.Equals(boundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase)
                && !boundary.PayloadExists
                && !string.IsNullOrWhiteSpace(boundary.PayloadPath))
            {
                notes.Add($"Ingress boundary for {boundary.Operation} cannot see the configured payload file at '{boundary.PayloadPath}'.");
                continue;
            }

            if ((string.Equals(boundary.Status, "warning", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(boundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))
                && boundary.PayloadAdvancedSinceLastSuccess == false)
            {
                notes.Add($"{boundary.Operation} is still reading the same local payload file as the last successful import, so the host can remain alive without receiving newer upstream data.");
            }
        }

        return notes.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string DetermineIngressBoundaryAggregateStatus(IReadOnlyList<MarketOrdersRuntimeIngressBoundaryView> ingressBoundaries)
    {
        if (ingressBoundaries.Any(boundary => string.Equals(boundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase)))
        {
            return "unhealthy";
        }

        if (ingressBoundaries.Any(boundary => string.Equals(boundary.Status, "warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "warning";
        }

        return ingressBoundaries.Count == 0 ? "unknown" : "healthy";
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

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

    private static long? ComputeAgeMinutes(DateTimeOffset? observedAtUtc, DateTimeOffset now)
    {
        if (!observedAtUtc.HasValue)
        {
            return null;
        }

        var age = now - observedAtUtc.Value;
        if (age <= TimeSpan.Zero)
        {
            return 0;
        }

        return (long)Math.Round(age.TotalMinutes, MidpointRounding.AwayFromZero);
    }

    private static string FormatAgeMinutes(long? ageMinutes)
    {
        return ageMinutes.HasValue ? $"{ageMinutes.Value}m" : "unknown";
    }

    private static MarketOrdersRuntimePayloadFileMetadataView ObservePayloadFileMetadata(string payloadPath)
    {
        var fullPath = Path.GetFullPath(payloadPath);
        if (!File.Exists(fullPath))
        {
            return new MarketOrdersRuntimePayloadFileMetadataView(fullPath, Exists: false, null, null);
        }

        var fileInfo = new FileInfo(fullPath);
        fileInfo.Refresh();
        return new MarketOrdersRuntimePayloadFileMetadataView(
            fullPath,
            Exists: true,
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            fileInfo.Length);
    }

    private static string CreateTraceId(string operation) => $"market-orders-runtime:{operation}:{Guid.NewGuid():N}";

    private static string CreateRunId() => $"orders-run-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    private static string CreateRecoveryId() => $"orders-lease-recovery-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    private static string CreateBundleVersion(string operation) => $"{DateTimeOffset.UtcNow:yyyy-MM-dd}-local-market-facts-{operation}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}";

    private static string NormalizeRequestedBy(string? requestedBy, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedBy)
            ? fallback
            : requestedBy.Trim();
    }

    private static string NormalizeTriggerKind(string? triggerKind, string fallback)
    {
        return string.IsNullOrWhiteSpace(triggerKind)
            ? fallback
            : triggerKind.Trim();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return global::EdenOS.Application.JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);
    }

    private static void WriteJsonAtomically<T>(string path, T data)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, data, JsonOptions);
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

    private sealed class LeaseHandle : IDisposable
    {
        private readonly FileStream _stream;
        private readonly MarketOrdersRuntimeStateStore _stateStore;
        private readonly string _leaseId;
        private bool _disposed;

        public LeaseHandle(
            FileStream stream,
            MarketOrdersRuntimeStateStore stateStore,
            MarketOrdersRuntimeLeaseState lease)
        {
            _stream = stream;
            _stateStore = stateStore;
            _leaseId = lease.LeaseId;
            LeaseId = lease.LeaseId;
        }

        public string LeaseId { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stateStore.Update(state => state.ActiveLease?.LeaseId == _leaseId
                ? state with { ActiveLease = null }
                : state);
            _stream.Dispose();
        }
    }

    private sealed class MarketOrdersRuntimeStateStore
    {
        private readonly string _path;
        private readonly Func<DateTimeOffset> _nowProvider;

        public MarketOrdersRuntimeStateStore(string path, Func<DateTimeOffset> nowProvider)
        {
            _path = path;
            _nowProvider = nowProvider;
        }

        public MarketOrdersRuntimeStateSnapshot Load()
        {
            if (!File.Exists(_path))
            {
                return new MarketOrdersRuntimeStateSnapshot(0, null, new MarketOrdersRuntimeState());
            }

            using var stream = File.OpenRead(_path);
            var envelope = JsonSerializer.Deserialize<MarketOrdersRuntimeEnvelope>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Orders runtime state file could not be deserialized.");

            return new MarketOrdersRuntimeStateSnapshot(envelope.Revision, envelope.UpdatedAtUtc, envelope.State);
        }

        public void Update(Func<MarketOrdersRuntimeState, MarketOrdersRuntimeState> apply)
        {
            var snapshot = Load();
            var envelope = new MarketOrdersRuntimeEnvelope
            {
                StoreFormat = StateStoreFormat,
                Revision = snapshot.Revision + 1,
                UpdatedAtUtc = _nowProvider(),
                State = apply(snapshot.State)
            };

            WriteJsonAtomically(_path, envelope);
        }
    }

    private sealed record MarketOrdersImportExecutionResult(
        MarketOrdersImportResult ImportResult,
        MarketOrdersRuntimeSnapshotArtifactView SnapshotArtifact,
        MarketOrdersRuntimeStoreSummaryView StoreSummary);

    private sealed record MarketOrdersRuntimeFailureClassification(string Category, string ReasonCode, bool Retryable);

    private sealed record MarketOrdersRuntimeRetentionCleanupSummary(
        DateTimeOffset CleanedAtUtc,
        int DeletedRunLogs,
        int DeletedSnapshotArtifacts,
        int RetainedRunLogs,
        int RetainedSnapshotArtifacts,
        int RetainedLeaseRecoveries,
        IReadOnlyList<string> Notes);

    private sealed record MarketOrdersRuntimePayloadFileMetadataView(
        string Path,
        bool Exists,
        DateTimeOffset? LastModifiedAtUtc,
        long? SizeBytes);
}

internal sealed record MarketOrdersRuntimeStateSnapshot(
    long Revision,
    DateTimeOffset? UpdatedAtUtc,
    MarketOrdersRuntimeState State);

public sealed record MarketOrdersRuntimeRunRequest
{
    public string? PayloadPath { get; init; }

    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public string? TriggerKind { get; init; }
}

public record MarketOrdersRuntimeStatusRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public int RecentRunLimit { get; init; } = 10;

    public int RecentSnapshotArtifactLimit { get; init; } = 5;

    public bool ProbeLease { get; init; } = true;

    public int WarningAgeMinutes { get; init; } = 240;

    public int ErrorAgeMinutes { get; init; } = 1440;
}

public sealed record MarketOrdersRuntimeHealthRequest : MarketOrdersRuntimeStatusRequest;

public sealed record MarketOrdersRuntimeLeaseRecoveryRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }

    public bool ProbeLease { get; init; } = true;
}

public sealed record MarketOrdersRuntimeCleanupRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }
}

public sealed record MarketOrdersRuntimeRunView
{
    public required string TraceId { get; init; }

    public required string RunId { get; init; }

    public required string RunKind { get; init; }

    public required string TriggerKind { get; init; }

    public required string Operation { get; init; }

    public required string Status { get; init; }

    public required string RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public required string IngressKind { get; init; }

    public string? PayloadPath { get; init; }

    public DateTimeOffset? PayloadFileModifiedAtUtc { get; init; }

    public long? PayloadFileSizeBytes { get; init; }

    public string? LeaseId { get; init; }

    public int AttemptCount { get; init; }

    public IReadOnlyList<int> RetryDelaysMs { get; init; } = Array.Empty<int>();

    public IReadOnlyList<MarketOrdersRuntimeAttemptFailureView> AttemptFailures { get; init; } = Array.Empty<MarketOrdersRuntimeAttemptFailureView>();

    public required string MarketFactsDirectoryPath { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public required string RunLogPath { get; init; }

    public MarketOrdersImportResult? ImportResult { get; init; }

    public MarketOrdersRuntimeSnapshotArtifactView? SnapshotArtifact { get; init; }

    public string? Source { get; init; }

    public string? BundleVersion { get; init; }

    public string? DataProtectionStatus { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public MarketOrdersRuntimeFailureView? Failure { get; init; }

    public MarketOrdersRuntimeLeaseRecoveryView? LeaseRecovery { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersImportResult
{
    public required string Source { get; init; }

    public required string BundleVersion { get; init; }

    public int ScopeUpserts { get; init; }

    public int SellOrderRowsWritten { get; init; }

    public int BuyOrderRowsWritten { get; init; }

    public int RetainedScopeCount { get; init; }

    public int RetainedLocationCount { get; init; }

    public DateTimeOffset? LatestObservedAtUtc { get; init; }

    public DateTimeOffset? OldestObservedAtUtc { get; init; }

    public int StaleScopeCount { get; init; }

    public int MaxRetainedLagMinutes { get; init; }
}

public sealed record MarketOrdersRuntimeStatusView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RuntimeStatePath { get; init; }

    public required string RunLeaseLockPath { get; init; }

    public DateTimeOffset QueriedAtUtc { get; init; }

    public long RuntimeRevision { get; init; }

    public DateTimeOffset? RuntimeUpdatedAtUtc { get; init; }

    public required string FactProtectionMode { get; init; }

    public required string FactProtectionSummary { get; init; }

    public required MarketOrdersRuntimeRetentionPolicyView RetentionPolicy { get; init; }

    public required MarketOrdersRuntimeLeaseObservationView Lease { get; init; }

    public MarketOrdersRuntimeStoreSummaryView? CurrentStoreSummary { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeOperationState> Operations { get; init; } = Array.Empty<MarketOrdersRuntimeOperationState>();

    public IReadOnlyList<MarketOrdersRuntimeRunView> RecentRuns { get; init; } = Array.Empty<MarketOrdersRuntimeRunView>();

    public IReadOnlyList<MarketOrdersRuntimeSnapshotArtifactView> RecentSnapshotArtifacts { get; init; } = Array.Empty<MarketOrdersRuntimeSnapshotArtifactView>();

    public IReadOnlyList<MarketOrdersRuntimeLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketOrdersRuntimeLeaseRecoveryView>();

    public required string IngressBoundaryStatus { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeIngressBoundaryView> IngressBoundaries { get; init; } = Array.Empty<MarketOrdersRuntimeIngressBoundaryView>();

    public IReadOnlyList<string> SupportedIngressKinds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FutureWorkerBoundaryNotes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersRuntimeHealthView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RuntimeStatePath { get; init; }

    public required string RunLeaseLockPath { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string OverallStatus { get; init; }

    public bool IsHealthy { get; init; }

    public int WarningAgeMinutes { get; init; }

    public int ErrorAgeMinutes { get; init; }

    public required string FactProtectionMode { get; init; }

    public required string FactProtectionSummary { get; init; }

    public required MarketOrdersRuntimeRetentionPolicyView RetentionPolicy { get; init; }

    public required MarketOrdersRuntimeLeaseObservationView Lease { get; init; }

    public MarketOrdersRuntimeStoreSummaryView? CurrentStoreSummary { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeOperationHealthView> Operations { get; init; } = Array.Empty<MarketOrdersRuntimeOperationHealthView>();

    public IReadOnlyList<MarketOrdersRuntimeRunView> RecentRuns { get; init; } = Array.Empty<MarketOrdersRuntimeRunView>();

    public IReadOnlyList<MarketOrdersRuntimeSnapshotArtifactView> RecentSnapshotArtifacts { get; init; } = Array.Empty<MarketOrdersRuntimeSnapshotArtifactView>();

    public IReadOnlyList<MarketOrdersRuntimeLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketOrdersRuntimeLeaseRecoveryView>();

    public required string IngressBoundaryStatus { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeIngressBoundaryView> IngressBoundaries { get; init; } = Array.Empty<MarketOrdersRuntimeIngressBoundaryView>();

    public IReadOnlyList<string> SupportedIngressKinds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FutureWorkerBoundaryNotes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersRuntimeLeaseRecoveryView
{
    public required string RecoveryId { get; init; }

    public required string Status { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public DateTimeOffset? RecoveredAtUtc { get; init; }

    public required string RecoveryReason { get; init; }

    public bool LockHeld { get; init; }

    public MarketOrdersRuntimeLeaseState? Lease { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersRuntimeCleanupView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset CleanedAtUtc { get; init; }

    public required MarketOrdersRuntimeRetentionPolicyView RetentionPolicy { get; init; }

    public int DeletedRunLogs { get; init; }

    public int DeletedSnapshotArtifacts { get; init; }

    public int RetainedRunLogs { get; init; }

    public int RetainedSnapshotArtifacts { get; init; }

    public int RetainedLeaseRecoveries { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersRuntimeAttemptFailureView
{
    public int Attempt { get; init; }

    public required string Category { get; init; }

    public required string ReasonCode { get; init; }

    public required string Error { get; init; }
}

public sealed record MarketOrdersRuntimeFailureView
{
    public required string Category { get; init; }

    public required string ReasonCode { get; init; }

    public required string Error { get; init; }

    public bool Retryable { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record MarketOrdersRuntimeLeaseObservationView
{
    public required string Status { get; init; }

    public bool LockHeld { get; init; }

    public MarketOrdersRuntimeLeaseState? ActiveLease { get; init; }
}

public sealed record MarketOrdersRuntimeLeaseState
{
    public required string LeaseId { get; init; }

    public required string OwnerId { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset AcquiredAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record MarketOrdersRuntimeStoreSummaryView
{
    public int ScopeCount { get; init; }

    public int LocationCount { get; init; }

    public DateTimeOffset? LatestObservedAtUtc { get; init; }

    public DateTimeOffset? OldestObservedAtUtc { get; init; }

    public int StaleScopeCount { get; init; }

    public int MaxRetainedLagMinutes { get; init; }
}

public sealed record MarketOrdersRuntimeIngressBoundaryView
{
    public required string Operation { get; init; }

    public required string IngressKind { get; init; }

    public required string Status { get; init; }

    public required string Summary { get; init; }

    public string? PayloadPath { get; init; }

    public bool PayloadExists { get; init; }

    public DateTimeOffset? PayloadLastModifiedAtUtc { get; init; }

    public long? PayloadAgeMinutes { get; init; }

    public long? PayloadSizeBytes { get; init; }

    public bool? PayloadAdvancedSinceLastSuccess { get; init; }

    public DateTimeOffset? LastSucceededPayloadLastModifiedAtUtc { get; init; }

    public long? LastSucceededPayloadSizeBytes { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersRuntimeOperationState
{
    public required string Operation { get; init; }

    public string? LastRunId { get; init; }

    public string? LastStatus { get; init; }

    public string? LastRequestedCursor { get; init; }

    public string? LastSucceededCursor { get; init; }

    public string? LastPayloadPath { get; init; }

    public DateTimeOffset? LastPayloadFileModifiedAtUtc { get; init; }

    public long? LastPayloadFileSizeBytes { get; init; }

    public DateTimeOffset? LastStartedAtUtc { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? LastSucceededPayloadFileModifiedAtUtc { get; init; }

    public long? LastSucceededPayloadFileSizeBytes { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string? LastError { get; init; }

    public int LastAttemptCount { get; init; }

    public string? LastBundleVersion { get; init; }

    public string? LastSource { get; init; }

    public int LastScopeUpserts { get; init; }

    public int LastRetainedScopeCount { get; init; }

    public int LastRetainedLocationCount { get; init; }

    public DateTimeOffset? LatestObservedAtUtc { get; init; }

    public DateTimeOffset? OldestObservedAtUtc { get; init; }

    public int LastStaleScopeCount { get; init; }

    public int LastMaxRetainedLagMinutes { get; init; }

    public string? LastSnapshotArtifactPath { get; init; }

    public string? LastDataProtectionStatus { get; init; }

    public string? LastTraceId { get; init; }
}

public sealed record MarketOrdersRuntimeOperationHealthView
{
    public required string Operation { get; init; }

    public required string Status { get; init; }

    public string? LastRunStatus { get; init; }

    public string? LastRunId { get; init; }

    public string? LastRequestedCursor { get; init; }

    public string? LastSucceededCursor { get; init; }

    public DateTimeOffset? LastStartedAtUtc { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public long? LatestSnapshotAgeMinutes { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string? LastError { get; init; }

    public int LastAttemptCount { get; init; }

    public string? LastBundleVersion { get; init; }

    public string? LastSource { get; init; }

    public int LastScopeUpserts { get; init; }

    public int LastRetainedScopeCount { get; init; }

    public int LastRetainedLocationCount { get; init; }

    public DateTimeOffset? LatestObservedAtUtc { get; init; }

    public DateTimeOffset? OldestObservedAtUtc { get; init; }

    public int StaleScopeCount { get; init; }

    public int MaxRetainedLagMinutes { get; init; }

    public string? LastSnapshotArtifactPath { get; init; }

    public string? LastDataProtectionStatus { get; init; }

    public string? LastTraceId { get; init; }

    public required MarketOrdersRuntimeIngressBoundaryView IngressBoundary { get; init; }
}

public sealed record MarketOrdersRuntimeSnapshotArtifactView
{
    public required string RunId { get; init; }

    public required string SnapshotArtifactPath { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public int ScopeCount { get; init; }

    public int LocationCount { get; init; }

    public DateTimeOffset? LatestObservedAtUtc { get; init; }

    public DateTimeOffset? OldestObservedAtUtc { get; init; }
}

public sealed record MarketOrdersRuntimeRetentionPolicyView
{
    public int RecentRunLimit { get; init; }

    public int RecentSnapshotArtifactLimit { get; init; }

    public int RecentLeaseRecoveryLimit { get; init; }
}

public sealed record MarketOrdersRuntimeState
{
    public MarketOrdersRuntimeLeaseState? ActiveLease { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeOperationState> Operations { get; init; } = Array.Empty<MarketOrdersRuntimeOperationState>();

    public IReadOnlyList<MarketOrdersRuntimeRunView> RecentRuns { get; init; } = Array.Empty<MarketOrdersRuntimeRunView>();

    public IReadOnlyList<MarketOrdersRuntimeSnapshotArtifactView> RecentSnapshotArtifacts { get; init; } = Array.Empty<MarketOrdersRuntimeSnapshotArtifactView>();

    public IReadOnlyList<MarketOrdersRuntimeLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketOrdersRuntimeLeaseRecoveryView>();
}

public sealed record MarketOrdersRuntimeEnvelope
{
    public required string StoreFormat { get; init; }

    public long Revision { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public required MarketOrdersRuntimeState State { get; init; }
}

internal sealed record MarketOrdersRuntimePaths(
    string StateDirectoryPath,
    string StatePath,
    string LeaseLockPath,
    string RunDirectoryPath,
    string SnapshotDirectoryPath)
{
    public static MarketOrdersRuntimePaths EnsureLayout(string marketFactsDirectoryPath)
    {
        Directory.CreateDirectory(marketFactsDirectoryPath);

        var stateDirectoryPath = Path.Combine(marketFactsDirectoryPath, "orders-runtime");
        var statePath = Path.Combine(stateDirectoryPath, "state.json");
        var leaseLockPath = Path.Combine(stateDirectoryPath, "run.lock");
        var runDirectoryPath = Path.Combine(marketFactsDirectoryPath, "orders-runs");
        var snapshotDirectoryPath = Path.Combine(marketFactsDirectoryPath, "orders-snapshot-artifacts");

        Directory.CreateDirectory(stateDirectoryPath);
        Directory.CreateDirectory(runDirectoryPath);
        Directory.CreateDirectory(snapshotDirectoryPath);

        return new MarketOrdersRuntimePaths(
            stateDirectoryPath,
            statePath,
            leaseLockPath,
            runDirectoryPath,
            snapshotDirectoryPath);
    }
}
