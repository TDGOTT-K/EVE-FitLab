using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class LocalMarketRefreshRuntime
{
    private const string RunKind = "formal_refresh_run";
    private const string BatchRunKind = "formal_refresh_batch";
    private const string DefaultTriggerKind = "cli_host";
    private const string StateStoreFormat = "edenos.market_refresh_runtime.v2";
    private const string FactProtectionMode = "replace_after_success_preserve_last_known_good";
    private const string FactProtectionSummary = "Refresh writes preserve the last known good local facts whenever a refresh fails before replacement completes.";
    private const int RecentRunRetentionLimit = 20;
    private const int RecentBatchRetentionLimit = 10;
    private const int RecentDeadLetterRetentionLimit = 20;
    private const int RecentLeaseRecoveryRetentionLimit = 10;
    private const int LockAcquireAttemptCount = 40;
    private static readonly TimeSpan LockAcquireDelay = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly int[] RetryBackoffScheduleMilliseconds = [250, 750];
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _marketFactsDirectoryPath;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, MarketRefreshOperationKind, string?, MarketFactRefreshResult> _operationExecutor;
    private readonly MarketRefreshRuntimePaths _paths;
    private readonly MarketRefreshRuntimeStateStore _stateStore;

    public LocalMarketRefreshRuntime(
        string marketFactsDirectoryPath,
        TimeProvider? timeProvider = null,
        Func<string, MarketRefreshOperationKind, string?, MarketFactRefreshResult>? operationExecutor = null)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        _marketFactsDirectoryPath = Path.GetFullPath(marketFactsDirectoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _operationExecutor = operationExecutor ?? DefaultExecuteOperation;
        _paths = MarketRefreshRuntimePaths.EnsureLayout(_marketFactsDirectoryPath);
        _stateStore = new MarketRefreshRuntimeStateStore(_paths.StatePath);
    }

    public UseCaseResult<MarketRefreshRunView> RunSingle(
        MarketRefreshOperationKind operation,
        MarketRefreshRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId(operation.OperationName);
        try
        {
            ValidateSingleRequest(operation, request);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return UseCaseResult<MarketRefreshRunView>.Failure(
                UseCaseStatus.InvalidInput,
                $"market refresh {operation.OperationName} rejected the supplied request.",
                traceId,
                [ex.Message]);
        }

        var runId = CreateRunId(operation.OperationName);
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_refresh");
        var triggerKind = NormalizeTriggerKind(request.TriggerKind, DefaultTriggerKind);
        var leaseRecovery = RecoverStaleLeaseIfPossible(requestedBy);
        using var lease = TryAcquireLease(
            ownerId: runId,
            batchId: request.BatchId,
            batchName: request.BatchName,
            requestedBy);
        if (lease is null)
        {
            return BuildLeaseConflictResult<MarketRefreshRunView>(
                traceId,
                $"market refresh {operation.OperationName} could not start because another refresh is already active.");
        }

        return ExecuteRunWithinLease(
            operation,
            request,
            traceId,
            runId,
            requestedBy,
            request.BatchId,
            request.BatchName,
            lease.LeaseId,
            triggerKind,
            leaseRecovery);
    }

    public UseCaseResult<MarketRefreshBatchView> RunBatch(MarketRefreshBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("batch");
        if (request.Operations.Count == 0)
        {
            return UseCaseResult<MarketRefreshBatchView>.Failure(
                UseCaseStatus.InvalidInput,
                "market refresh batch requires at least one operation.",
                traceId,
                ["request.operations must contain at least one refresh step."]);
        }

        var batchId = CreateBatchId();
        var batchName = string.IsNullOrWhiteSpace(request.BatchName)
            ? "market-refresh-batch"
            : request.BatchName!;
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_refresh.batch");
        var triggerKind = NormalizeTriggerKind(request.TriggerKind, DefaultTriggerKind);
        var startedAtUtc = UtcNow();
        var leaseRecovery = RecoverStaleLeaseIfPossible(requestedBy);
        using var lease = TryAcquireLease(batchId, batchId, batchName, requestedBy);
        if (lease is null)
        {
            return BuildLeaseConflictResult<MarketRefreshBatchView>(
                traceId,
                "market refresh batch could not start because another refresh is already active.");
        }

        var runs = new List<MarketRefreshRunView>();
        var warnings = new List<string>();
        var errors = new List<string>();

        foreach (var operation in request.Operations)
        {
            MarketRefreshOperationKind normalizedOperation;
            try
            {
                normalizedOperation = MarketRefreshOperationKind.Parse(operation.Operation);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
                if (!request.ContinueOnError)
                {
                    break;
                }

                continue;
            }

            var runRequest = new MarketRefreshRunRequest
            {
                PayloadPath = operation.PayloadPath,
                RequestedBy = requestedBy,
                Cursor = string.IsNullOrWhiteSpace(operation.Cursor) ? request.Cursor : operation.Cursor,
                BatchId = batchId,
                BatchName = batchName,
                TriggerKind = triggerKind
            };

            var runResult = ExecuteRunWithinLease(
                normalizedOperation,
                runRequest,
                traceId,
                CreateRunId(normalizedOperation.OperationName),
                requestedBy,
                batchId,
                batchName,
                lease.LeaseId,
                triggerKind,
                leaseRecovery: null);

            if (runResult.Data is not null)
            {
                runs.Add(runResult.Data);
            }

            if (!runResult.IsSuccess)
            {
                errors.AddRange(runResult.Errors.Count > 0 ? runResult.Errors : [runResult.Summary]);
                if (!request.ContinueOnError)
                {
                    break;
                }
            }
        }

        var completedAtUtc = UtcNow();
        var batchStatus = errors.Count == 0
            ? "completed"
            : runs.Count == 0
                ? "failed"
                : "completed_with_failures";

        if (errors.Count > 0 && request.ContinueOnError)
        {
            warnings.Add("At least one refresh step failed, but the batch continued because continue_on_error=true.");
        }

        var batch = new MarketRefreshBatchView
        {
            TraceId = traceId,
            BatchId = batchId,
            BatchName = batchName,
            RunKind = BatchRunKind,
            TriggerKind = triggerKind,
            Status = batchStatus,
            RequestedBy = requestedBy,
            Cursor = request.Cursor,
            ContinueOnError = request.ContinueOnError,
            LeaseId = lease.LeaseId,
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            BatchLogPath = Path.Combine(_paths.BatchDirectoryPath, $"{batchId}.json"),
            Runs = runs
        };

        WriteJsonAtomically(batch.BatchLogPath, batch);
        _stateStore.Update(state => UpdateStateAfterBatch(state, batch));

        if (errors.Count > 0)
        {
            return new UseCaseResult<MarketRefreshBatchView>
            {
                Status = UseCaseStatus.Error,
                Summary = "market refresh batch completed with failures.",
                Data = batch,
                Warnings = warnings,
                Errors = errors,
                TraceId = traceId
            };
        }

        return UseCaseResult<MarketRefreshBatchView>.Success(
            batch,
            $"market refresh batch completed with {runs.Count} run(s).",
            traceId,
            warnings);
    }

    public UseCaseResult<MarketRefreshStatusView> GetStatus(MarketRefreshStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("status");
        try
        {
            ValidateStatusRequest(request);
        }
        catch (ArgumentException ex)
        {
            return UseCaseResult<MarketRefreshStatusView>.Failure(
                UseCaseStatus.InvalidInput,
                "market refresh status rejected the supplied request.",
                traceId,
                [ex.Message]);
        }

        var snapshot = _stateStore.Load();
        var data = new MarketRefreshStatusView
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
            Operations = snapshot.State.Operations
                .OrderBy(state => state.Operation, StringComparer.Ordinal)
                .ToArray(),
            RecentRuns = snapshot.State.RecentRuns
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentBatches = snapshot.State.RecentBatches
                .Take(request.RecentBatchLimit)
                .ToArray(),
            RecentDeadLetters = snapshot.State.RecentDeadLetters
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentLeaseRecoveries = snapshot.State.RecentLeaseRecoveries
                .Take(request.RecentBatchLimit)
                .ToArray()
        };

        return UseCaseResult<MarketRefreshStatusView>.Success(
            data,
            "market refresh runtime status queried.",
            traceId);
    }

    public UseCaseResult<MarketRefreshHealthView> GetHealth(MarketRefreshHealthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("health");
        try
        {
            ValidateHealthRequest(request);
        }
        catch (ArgumentException ex)
        {
            return UseCaseResult<MarketRefreshHealthView>.Failure(
                UseCaseStatus.InvalidInput,
                "market refresh health rejected the supplied request.",
                traceId,
                [ex.Message]);
        }

        var snapshot = _stateStore.Load();
        var now = UtcNow();
        var leaseObservation = ObserveLease(snapshot.State.ActiveLease, request.ProbeLease);
        var operations = snapshot.State.Operations
            .OrderBy(state => state.Operation, StringComparer.Ordinal)
            .Select(state => BuildOperationHealth(state, now, request.WarningLagMinutes, request.ErrorLagMinutes))
            .ToArray();

        var notes = new List<string>();
        if (leaseObservation.Status == "stale_orphaned")
        {
            notes.Add("The runtime state still reports an active lease, but the lease file is not currently locked.");
        }

        if (snapshot.State.RecentDeadLetters.Count > 0)
        {
            notes.Add("Recent dead-letter entries exist. Review failure classification and quarantine payloads before assuming coverage is complete.");
        }

        if (operations.Length == 0)
        {
            notes.Add("No persisted refresh operation state exists yet for this market-facts directory.");
        }

        var overallStatus = DetermineOverallHealthStatus(leaseObservation, operations, snapshot.State.RecentDeadLetters);
        var data = new MarketRefreshHealthView
        {
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            RuntimeStatePath = _paths.StatePath,
            RunLeaseLockPath = _paths.LeaseLockPath,
            EvaluatedAtUtc = now,
            OverallStatus = overallStatus,
            IsHealthy = overallStatus == "healthy",
            WarningLagMinutes = request.WarningLagMinutes,
            ErrorLagMinutes = request.ErrorLagMinutes,
            FactProtectionMode = FactProtectionMode,
            FactProtectionSummary = FactProtectionSummary,
            RetentionPolicy = BuildRetentionPolicy(),
            Lease = leaseObservation,
            Operations = operations,
            RecentRuns = snapshot.State.RecentRuns
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentBatches = snapshot.State.RecentBatches
                .Take(request.RecentBatchLimit)
                .ToArray(),
            RecentDeadLetters = snapshot.State.RecentDeadLetters
                .Take(request.RecentRunLimit)
                .ToArray(),
            RecentLeaseRecoveries = snapshot.State.RecentLeaseRecoveries
                .Take(request.RecentBatchLimit)
                .ToArray(),
            Notes = notes
        };

        return UseCaseResult<MarketRefreshHealthView>.Success(
            data,
            $"market refresh health is {overallStatus}.",
            traceId,
            warnings: notes);
    }

    public UseCaseResult<MarketRefreshLeaseRecoveryView> RecoverLease(MarketRefreshLeaseRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("recover-lease");
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_refresh.recover_lease");
        var snapshot = _stateStore.Load();
        var observation = ObserveLease(snapshot.State.ActiveLease, request.ProbeLease);

        if (observation.ActiveLease is null)
        {
            return UseCaseResult<MarketRefreshLeaseRecoveryView>.Success(
                new MarketRefreshLeaseRecoveryView
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
                "market refresh lease recovery found no stale lease state.",
                traceId);
        }

        if (observation.LockHeld)
        {
            return UseCaseResult<MarketRefreshLeaseRecoveryView>.Failure(
                UseCaseStatus.Conflict,
                "market refresh lease recovery refused because the lease file is currently held by another process.",
                traceId,
                ["The lease file is locked, so recovery would risk interrupting an active refresh run."]);
        }

        var recovery = PersistLeaseRecovery(observation.ActiveLease, requestedBy);
        PerformRetentionCompaction();

        return UseCaseResult<MarketRefreshLeaseRecoveryView>.Success(
            recovery,
            "market refresh stale lease state recovered.",
            traceId,
            recovery.Notes);
    }

    public UseCaseResult<MarketRefreshCleanupView> Cleanup(MarketRefreshCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceId = CreateTraceId("cleanup");
        var requestedBy = NormalizeRequestedBy(request.RequestedBy, "cli.market_refresh.cleanup");
        _stateStore.Update(TrimStateToRetentionLimits);
        var summary = PerformRetentionCompaction();

        var data = new MarketRefreshCleanupView
        {
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            RequestedBy = requestedBy,
            CleanedAtUtc = summary.CleanedAtUtc,
            RetentionPolicy = BuildRetentionPolicy(),
            DeletedRunLogs = summary.DeletedRunLogs,
            DeletedBatchLogs = summary.DeletedBatchLogs,
            DeletedDeadLetters = summary.DeletedDeadLetters,
            DeletedQuarantinePayloads = summary.DeletedQuarantinePayloads,
            RetainedRunLogs = summary.RetainedRunLogs,
            RetainedBatchLogs = summary.RetainedBatchLogs,
            RetainedDeadLetters = summary.RetainedDeadLetters,
            RetainedLeaseRecoveries = summary.RetainedLeaseRecoveries,
            Notes = summary.Notes
        };

        return UseCaseResult<MarketRefreshCleanupView>.Success(
            data,
            "market refresh retention cleanup completed.",
            traceId,
            data.Notes);
    }

    private UseCaseResult<MarketRefreshRunView> ExecuteRunWithinLease(
        MarketRefreshOperationKind operation,
        MarketRefreshRunRequest request,
        string traceId,
        string runId,
        string requestedBy,
        string? batchId,
        string? batchName,
        string leaseId,
        string triggerKind,
        MarketRefreshLeaseRecoveryView? leaseRecovery)
    {
        var payloadPath = operation.RequiresPayload ? request.PayloadPath : null;
        var startedAtUtc = UtcNow();
        var runLogPath = Path.Combine(_paths.RunDirectoryPath, $"{runId}.json");
        var attemptFailures = new List<MarketRefreshAttemptFailureView>();
        var retryDelaysMs = new List<int>();
        MarketFactRefreshResult? refreshResult = null;
        MarketRefreshFailureView? failure = null;
        var attemptCount = 0;

        for (var attemptIndex = 0; attemptIndex < RetryBackoffScheduleMilliseconds.Length + 1; attemptIndex++)
        {
            attemptCount = attemptIndex + 1;

            try
            {
                refreshResult = ExecutePipelineOperation(operation, payloadPath);
                failure = null;
                break;
            }
            catch (Exception ex)
            {
                var classification = ClassifyFailure(ex);
                failure = new MarketRefreshFailureView
                {
                    Category = classification.Category,
                    ReasonCode = classification.ReasonCode,
                    Error = ex.Message,
                    Retryable = classification.Retryable,
                    AttemptCount = attemptCount,
                    OccurredAtUtc = UtcNow()
                };

                attemptFailures.Add(new MarketRefreshAttemptFailureView
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

                var delayMilliseconds = RetryBackoffScheduleMilliseconds[attemptIndex];
                retryDelaysMs.Add(delayMilliseconds);
                Thread.Sleep(delayMilliseconds);
            }
        }

        var completedAtUtc = UtcNow();
        var anomalies = new List<string>();
        if (leaseRecovery is not null)
        {
            anomalies.Add("stale_lease_recovered_before_run");
        }

        MarketRefreshDeadLetterView? deadLetter = null;
        if (failure is not null)
        {
            anomalies.Add("existing_local_facts_preserved_after_failure");
            deadLetter = WriteDeadLetter(
                runId,
                operation,
                payloadPath,
                requestedBy,
                traceId,
                leaseId,
                failure,
                attemptFailures,
                completedAtUtc);
            anomalies.Add("dead_letter_recorded");
        }

        var run = new MarketRefreshRunView
        {
            TraceId = traceId,
            RunId = runId,
            BatchId = batchId,
            BatchName = batchName,
            RunKind = RunKind,
            TriggerKind = triggerKind,
            Operation = operation.OperationName,
            Status = failure is null ? "completed" : "failed",
            RequestedBy = requestedBy,
            Cursor = request.Cursor,
            IngressKind = operation.IngressKind,
            PayloadPath = payloadPath,
            LeaseId = leaseId,
            AttemptCount = attemptCount,
            RetryDelaysMs = retryDelaysMs,
            AttemptFailures = attemptFailures,
            MarketFactsDirectoryPath = _marketFactsDirectoryPath,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            RunLogPath = runLogPath,
            RefreshResult = refreshResult,
            Errors = failure is null ? Array.Empty<string>() : [failure.Error],
            Failure = failure,
            DeadLetter = deadLetter,
            LeaseRecovery = leaseRecovery,
            Source = refreshResult?.Source,
            BundleVersion = refreshResult?.BundleVersion,
            CoverageSummary = BuildCoverageSummary(refreshResult),
            TraceSummary = BuildTraceSummary(traceId, refreshResult),
            FreshnessSummary = BuildFreshnessSummary(refreshResult, completedAtUtc),
            DataProtectionStatus = failure is null
                ? "replaced_after_success"
                : "preserved_last_known_good_facts",
            Anomalies = anomalies,
            Notes = BuildRunNotes(refreshResult, failure, deadLetter, leaseRecovery)
        };

        WriteJsonAtomically(run.RunLogPath, run);
        _stateStore.Update(state => UpdateStateAfterRun(state, run));
        PerformRetentionCompaction();

        if (failure is not null)
        {
            return new UseCaseResult<MarketRefreshRunView>
            {
                Status = UseCaseStatus.Error,
                Summary = $"market refresh {operation.OperationName} failed with a {failure.Category} failure.",
                Data = run,
                Errors = [failure.Error],
                TraceId = traceId
            };
        }

        return UseCaseResult<MarketRefreshRunView>.Success(
            run,
            attemptCount == 1
                ? $"market refresh {operation.OperationName} completed."
                : $"market refresh {operation.OperationName} completed after {attemptCount} attempts.",
            traceId,
            run.Notes);
    }

    private MarketFactRefreshResult ExecutePipelineOperation(
        MarketRefreshOperationKind operation,
        string? payloadPath)
    {
        return _operationExecutor(_marketFactsDirectoryPath, operation, payloadPath);
    }

    private static MarketFactRefreshResult DefaultExecuteOperation(
        string marketFactsDirectoryPath,
        MarketRefreshOperationKind operation,
        string? payloadPath)
    {
        var pipeline = new LocalMarketFactRefreshPipeline(marketFactsDirectoryPath);
        return operation.OperationName switch
        {
            "snapshot" => pipeline.ImportSnapshotFile(payloadPath!),
            "history" => pipeline.AppendHistoryFile(payloadPath!),
            "grants" => pipeline.RefreshStructureGrantsFile(payloadPath!),
            "rebuild-statistics" => pipeline.RebuildStatisticsProjections(),
            _ => throw new InvalidOperationException($"Unsupported market refresh operation '{operation.OperationName}'.")
        };
    }

    private LeaseHandle? TryAcquireLease(
        string ownerId,
        string? batchId,
        string? batchName,
        string requestedBy)
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

                var acquiredAtUtc = UtcNow();
                var leaseState = new MarketRefreshLeaseState
                {
                    LeaseId = $"{ownerId}-lease",
                    OwnerId = ownerId,
                    BatchId = batchId,
                    BatchName = batchName,
                    RequestedBy = requestedBy,
                    AcquiredAtUtc = acquiredAtUtc,
                    ExpiresAtUtc = acquiredAtUtc.Add(LeaseDuration)
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

    private MarketRefreshLeaseObservationView ObserveLease(
        MarketRefreshLeaseState? activeLease,
        bool probeLease)
    {
        if (!probeLease)
        {
            return new MarketRefreshLeaseObservationView
            {
                Status = activeLease is null
                    ? "idle"
                    : UtcNow() <= activeLease.ExpiresAtUtc
                        ? "active_unprobed"
                        : "expired_unprobed",
                LockHeld = false,
                ActiveLease = activeLease
            };
        }

        var lockHeld = probeLease && IsLeaseLockHeld();
        var now = UtcNow();
        var status = activeLease is null
            ? lockHeld ? "active_without_state" : "idle"
            : lockHeld
                ? now <= activeLease.ExpiresAtUtc ? "active" : "expired_but_locked"
                : "stale_orphaned";

        return new MarketRefreshLeaseObservationView
        {
            Status = status,
            LockHeld = lockHeld,
            ActiveLease = activeLease
        };
    }

    private bool IsLeaseLockHeld()
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

    private MarketRefreshOperationHealthView BuildOperationHealth(
        MarketRefreshOperationState state,
        DateTimeOffset now,
        int warningLagMinutes,
        int errorLagMinutes)
    {
        var lagMinutes = state.LastSucceededAtUtc.HasValue
            ? (long?)Math.Max(0, Math.Floor((now - state.LastSucceededAtUtc.Value).TotalMinutes))
            : null;

        var status = "unknown";
        if (state.ConsecutiveFailures >= 3)
        {
            status = "unhealthy";
        }
        else if (!state.LastSucceededAtUtc.HasValue)
        {
            status = state.LastFailedAtUtc.HasValue ? "degraded" : "unknown";
        }
        else if (lagMinutes > errorLagMinutes)
        {
            status = "unhealthy";
        }
        else if (lagMinutes > warningLagMinutes || state.ConsecutiveFailures > 0 || !string.IsNullOrWhiteSpace(state.LastDeadLetterPath))
        {
            status = "degraded";
        }
        else
        {
            status = "healthy";
        }

        return new MarketRefreshOperationHealthView
        {
            Operation = state.Operation,
            Status = status,
            LastRunStatus = state.LastStatus,
            LastRunId = state.LastRunId,
            LastRequestedCursor = state.LastRequestedCursor,
            LastSucceededCursor = state.LastSucceededCursor,
            LastStartedAtUtc = state.LastStartedAtUtc,
            LastCompletedAtUtc = state.LastCompletedAtUtc,
            LastSucceededAtUtc = state.LastSucceededAtUtc,
            LastFailedAtUtc = state.LastFailedAtUtc,
            LagMinutes = lagMinutes,
            ConsecutiveFailures = state.ConsecutiveFailures,
            LastError = state.LastError,
            LastAttemptCount = state.LastAttemptCount,
            LastBundleVersion = state.LastBundleVersion,
            LastSource = state.LastSource,
            LastCoverageSummary = state.LastCoverageSummary,
            LastTraceSummary = state.LastTraceSummary,
            LastFreshnessSummary = state.LastFreshnessSummary,
            LastFailureCategory = state.LastFailureCategory,
            LastFailureReasonCode = state.LastFailureReasonCode,
            LastDeadLetterPath = state.LastDeadLetterPath,
            LastDataProtectionStatus = state.LastDataProtectionStatus,
            LastTraceId = state.LastTraceId,
            LastAnomalies = state.LastAnomalies
        };
    }

    private static string DetermineOverallHealthStatus(
        MarketRefreshLeaseObservationView lease,
        IReadOnlyList<MarketRefreshOperationHealthView> operations,
        IReadOnlyList<MarketRefreshDeadLetterView> recentDeadLetters)
    {
        if (lease.Status == "stale_orphaned")
        {
            return "unhealthy";
        }

        if (operations.Any(operation => operation.Status == "unhealthy"))
        {
            return "unhealthy";
        }

        if (lease.Status is "expired_but_locked" or "active_without_state")
        {
            return "degraded";
        }

        if (recentDeadLetters.Count > 0 || operations.Any(operation => operation.Status == "degraded"))
        {
            return "degraded";
        }

        if (operations.All(operation => operation.Status == "healthy") && operations.Count > 0)
        {
            return "healthy";
        }

        return "unknown";
    }

    private MarketRefreshRuntimeState UpdateStateAfterRun(
        MarketRefreshRuntimeState state,
        MarketRefreshRunView run)
    {
        var operationStates = state.Operations.ToDictionary(
            entry => entry.Operation,
            entry => entry,
            StringComparer.Ordinal);

        operationStates[run.Operation] = BuildOperationState(
            operationStates.TryGetValue(run.Operation, out var existing) ? existing : null,
            run);

        return TrimStateToRetentionLimits(state with
        {
            Operations = operationStates.Values
                .OrderBy(entry => entry.Operation, StringComparer.Ordinal)
                .ToArray(),
            RecentRuns = state.RecentRuns
                .Prepend(run)
                .Take(RecentRunRetentionLimit)
                .ToArray(),
            RecentDeadLetters = run.DeadLetter is null
                ? state.RecentDeadLetters
                : state.RecentDeadLetters
                    .Prepend(run.DeadLetter)
                    .Take(RecentDeadLetterRetentionLimit)
                    .ToArray()
        });
    }

    private static MarketRefreshRuntimeState UpdateStateAfterBatch(
        MarketRefreshRuntimeState state,
        MarketRefreshBatchView batch)
    {
        return TrimStateToRetentionLimits(state with
        {
            RecentBatches = state.RecentBatches
                .Prepend(batch)
                .Take(RecentBatchRetentionLimit)
                .ToArray()
        });
    }

    private static MarketRefreshOperationState BuildOperationState(
        MarketRefreshOperationState? existing,
        MarketRefreshRunView run)
    {
        var isSuccess = string.Equals(run.Status, "completed", StringComparison.Ordinal);
        return new MarketRefreshOperationState
        {
            Operation = run.Operation,
            LastRunId = run.RunId,
            LastStatus = run.Status,
            LastRequestedCursor = run.Cursor,
            LastSucceededCursor = isSuccess ? run.Cursor : existing?.LastSucceededCursor,
            LastPayloadPath = run.PayloadPath,
            LastStartedAtUtc = run.StartedAtUtc,
            LastCompletedAtUtc = run.CompletedAtUtc,
            LastSucceededAtUtc = isSuccess ? run.CompletedAtUtc : existing?.LastSucceededAtUtc,
            LastFailedAtUtc = isSuccess ? existing?.LastFailedAtUtc : run.CompletedAtUtc,
            ConsecutiveFailures = isSuccess ? 0 : (existing?.ConsecutiveFailures ?? 0) + 1,
            LastError = run.Errors.Count == 0 ? null : run.Errors[0],
            LastAttemptCount = run.AttemptCount,
            LastBundleVersion = run.BundleVersion,
            LastSource = run.Source,
            LastCoverageSummary = run.CoverageSummary,
            LastTraceSummary = run.TraceSummary,
            LastFreshnessSummary = run.FreshnessSummary,
            LastFailureCategory = run.Failure?.Category ?? existing?.LastFailureCategory,
            LastFailureReasonCode = run.Failure?.ReasonCode ?? existing?.LastFailureReasonCode,
            LastDeadLetterPath = run.DeadLetter?.RecordPath ?? existing?.LastDeadLetterPath,
            LastDataProtectionStatus = run.DataProtectionStatus,
            LastTraceId = run.TraceId,
            LastAnomalies = run.Anomalies
        };
    }

    private static MarketRefreshRuntimeState TrimStateToRetentionLimits(MarketRefreshRuntimeState state)
    {
        return state with
        {
            RecentRuns = state.RecentRuns.Take(RecentRunRetentionLimit).ToArray(),
            RecentBatches = state.RecentBatches.Take(RecentBatchRetentionLimit).ToArray(),
            RecentDeadLetters = state.RecentDeadLetters.Take(RecentDeadLetterRetentionLimit).ToArray(),
            RecentLeaseRecoveries = state.RecentLeaseRecoveries.Take(RecentLeaseRecoveryRetentionLimit).ToArray()
        };
    }

    private MarketRefreshLeaseRecoveryView? RecoverStaleLeaseIfPossible(string requestedBy)
    {
        var snapshot = _stateStore.Load();
        var observation = ObserveLease(snapshot.State.ActiveLease, probeLease: true);
        if (observation.ActiveLease is null || observation.LockHeld)
        {
            return null;
        }

        return PersistLeaseRecovery(observation.ActiveLease, requestedBy);
    }

    private MarketRefreshLeaseRecoveryView PersistLeaseRecovery(MarketRefreshLeaseState staleLease, string requestedBy)
    {
        var recoveredAtUtc = UtcNow();
        var recovery = new MarketRefreshLeaseRecoveryView
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
                "The stale lease state was cleared before the next refresh action started.",
                "Existing local facts were not deleted during lease recovery."
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

    private MarketRefreshDeadLetterView WriteDeadLetter(
        string runId,
        MarketRefreshOperationKind operation,
        string? payloadPath,
        string requestedBy,
        string traceId,
        string leaseId,
        MarketRefreshFailureView failure,
        IReadOnlyList<MarketRefreshAttemptFailureView> attemptFailures,
        DateTimeOffset createdAtUtc)
    {
        var deadLetterId = $"{runId}-dead-letter";
        string? quarantinePayloadPath = null;
        var notes = new List<string>
        {
            "The failed refresh payload was quarantined so the last known good facts could remain readable.",
            "Review the failure classification before retrying this payload."
        };

        if (!string.IsNullOrWhiteSpace(payloadPath) && File.Exists(payloadPath))
        {
            quarantinePayloadPath = Path.Combine(
                _paths.QuarantineDirectoryPath,
                $"{runId}-{Path.GetFileName(payloadPath)}");
            File.Copy(payloadPath, quarantinePayloadPath, overwrite: true);
        }
        else if (!string.IsNullOrWhiteSpace(payloadPath))
        {
            notes.Add("The original payload path no longer existed when quarantine was attempted.");
        }

        var deadLetter = new MarketRefreshDeadLetterView
        {
            DeadLetterId = deadLetterId,
            RunId = runId,
            Operation = operation.OperationName,
            FailureCategory = failure.Category,
            FailureReasonCode = failure.ReasonCode,
            RequestedBy = requestedBy,
            TraceId = traceId,
            LeaseId = leaseId,
            CreatedAtUtc = createdAtUtc,
            PayloadPath = payloadPath,
            QuarantinePayloadPath = quarantinePayloadPath,
            RecordPath = Path.Combine(_paths.DeadLetterDirectoryPath, $"{deadLetterId}.json"),
            PreservedExistingFacts = true,
            Errors = attemptFailures.Select(item => $"{item.ReasonCode}: {item.Error}").ToArray(),
            Notes = notes
        };

        WriteJsonAtomically(deadLetter.RecordPath, deadLetter);
        return deadLetter;
    }

    private RetentionCleanupSummary PerformRetentionCompaction()
    {
        var snapshot = _stateStore.Load();
        var retainedRunLogs = snapshot.State.RecentRuns
            .Select(run => run.RunLogPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedBatchLogs = snapshot.State.RecentBatches
            .Select(batch => batch.BatchLogPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedDeadLetters = snapshot.State.RecentDeadLetters
            .Select(deadLetter => deadLetter.RecordPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedQuarantinePayloads = snapshot.State.RecentDeadLetters
            .Select(deadLetter => deadLetter.QuarantinePayloadPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedRunLogs = DeleteOrphanedFiles(_paths.RunDirectoryPath, "*.json", retainedRunLogs);
        var deletedBatchLogs = DeleteOrphanedFiles(_paths.BatchDirectoryPath, "*.json", retainedBatchLogs);
        var deletedDeadLetters = DeleteOrphanedFiles(_paths.DeadLetterDirectoryPath, "*.json", retainedDeadLetters);
        var deletedQuarantinePayloads = DeleteOrphanedFiles(_paths.QuarantineDirectoryPath, "*", retainedQuarantinePayloads);
        var notes = deletedRunLogs + deletedBatchLogs + deletedDeadLetters + deletedQuarantinePayloads == 0
            ? new[] { "No orphaned refresh runtime artifacts were removed during this compaction pass." }
            : new[] { "Retention compaction removed runtime artifacts that were no longer referenced by the retained state window." };

        return new RetentionCleanupSummary(
            UtcNow(),
            deletedRunLogs,
            deletedBatchLogs,
            deletedDeadLetters,
            deletedQuarantinePayloads,
            snapshot.State.RecentRuns.Count,
            snapshot.State.RecentBatches.Count,
            snapshot.State.RecentDeadLetters.Count,
            snapshot.State.RecentLeaseRecoveries.Count,
            notes);
    }

    private static int DeleteOrphanedFiles(string directoryPath, string searchPattern, IReadOnlySet<string> retainedPaths)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var filePath in Directory.GetFiles(directoryPath, searchPattern))
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

    private static MarketRefreshFailureClassification ClassifyFailure(Exception ex)
    {
        return ex switch
        {
            FileNotFoundException => new MarketRefreshFailureClassification("permanent", "missing_payload", false),
            DirectoryNotFoundException => new MarketRefreshFailureClassification("permanent", "missing_directory", false),
            PathTooLongException => new MarketRefreshFailureClassification("permanent", "invalid_path", false),
            JsonException => new MarketRefreshFailureClassification("permanent", "invalid_json_payload", false),
            FormatException => new MarketRefreshFailureClassification("permanent", "invalid_payload_format", false),
            InvalidDataException => new MarketRefreshFailureClassification("permanent", "invalid_payload_data", false),
            UnauthorizedAccessException => new MarketRefreshFailureClassification("permanent", "filesystem_access_denied", false),
            ArgumentException => new MarketRefreshFailureClassification("permanent", "invalid_request", false),
            InvalidOperationException => new MarketRefreshFailureClassification("permanent", "invalid_operation", false),
            TimeoutException => new MarketRefreshFailureClassification("transient", "timeout", true),
            IOException => new MarketRefreshFailureClassification("transient", "io_transient", true),
            _ => new MarketRefreshFailureClassification("permanent", "unexpected_runtime_exception", false)
        };
    }

    private static IReadOnlyList<string> BuildRunNotes(
        MarketFactRefreshResult? refreshResult,
        MarketRefreshFailureView? failure,
        MarketRefreshDeadLetterView? deadLetter,
        MarketRefreshLeaseRecoveryView? leaseRecovery)
    {
        var notes = new List<string>();
        if (leaseRecovery is not null)
        {
            notes.Add("A stale lease state was recovered before this refresh run started.");
        }

        if (refreshResult is not null)
        {
            notes.Add($"Refresh source '{refreshResult.Source}' produced bundle '{refreshResult.BundleVersion}'.");
            notes.AddRange(refreshResult.Notes);
        }

        if (failure is not null)
        {
            notes.Add($"Refresh failed with a {failure.Category} classification ({failure.ReasonCode}).");
        }

        if (deadLetter is not null)
        {
            notes.Add($"Dead-letter record written to '{deadLetter.RecordPath}'.");
        }

        return notes;
    }

    private static IReadOnlyList<string> BuildBatchNotes(
        IReadOnlyList<MarketRefreshRunView> runs,
        MarketRefreshLeaseRecoveryView? leaseRecovery)
    {
        var notes = new List<string>();
        if (leaseRecovery is not null)
        {
            notes.Add("A stale lease state was recovered before the batch lease was acquired.");
        }

        var deadLetterCount = runs.Count(run => run.DeadLetter is not null);
        if (deadLetterCount > 0)
        {
            notes.Add($"Batch produced {deadLetterCount} dead-letter entr{(deadLetterCount == 1 ? "y" : "ies")} for failed refresh runs.");
        }

        return notes;
    }

    private static string? BuildCoverageSummary(MarketFactRefreshResult? refreshResult)
    {
        if (refreshResult is null)
        {
            return null;
        }

        return refreshResult.Operation switch
        {
            "snapshot-import" => $"snapshot_pairs={refreshResult.SnapshotUpserts}; statistics_recomputed={refreshResult.StatisticsRecomputed}",
            "history-append" => $"history_series={refreshResult.HistorySeriesTouched}; history_points={refreshResult.HistoryPointsUpserted}; statistics_recomputed={refreshResult.StatisticsRecomputed}",
            "grant-refresh" => $"characters_refreshed={refreshResult.CharactersRefreshed}; structure_grants_written={refreshResult.StructureGrantsWritten}",
            "statistics-rebuild" => $"statistics_recomputed={refreshResult.StatisticsRecomputed}",
            _ => $"operation={refreshResult.Operation}; notes={refreshResult.Notes.Count}"
        };
    }

    private static string? BuildTraceSummary(string traceId, MarketFactRefreshResult? refreshResult)
    {
        return refreshResult is null
            ? $"trace_id={traceId}"
            : $"trace_id={traceId}; source={refreshResult.Source}; bundle_version={refreshResult.BundleVersion}";
    }

    private static string? BuildFreshnessSummary(MarketFactRefreshResult? refreshResult, DateTimeOffset completedAtUtc)
    {
        return refreshResult is null
            ? $"run_completed_at_utc={completedAtUtc:O}"
            : $"{refreshResult.Operation}_completed_at_utc={completedAtUtc:O}; source={refreshResult.Source}";
    }

    private static MarketRefreshRetentionPolicyView BuildRetentionPolicy()
    {
        return new MarketRefreshRetentionPolicyView
        {
            RecentRunLimit = RecentRunRetentionLimit,
            RecentBatchLimit = RecentBatchRetentionLimit,
            RecentDeadLetterLimit = RecentDeadLetterRetentionLimit,
            RecentLeaseRecoveryLimit = RecentLeaseRecoveryRetentionLimit
        };
    }

    private static UseCaseResult<T> BuildLeaseConflictResult<T>(string traceId, string summary)
    {
        return UseCaseResult<T>.Failure(
            UseCaseStatus.Conflict,
            summary,
            traceId,
            ["Another market refresh process already holds the lease for this market-facts directory."]);
    }

    private static void ValidateSingleRequest(MarketRefreshOperationKind operation, MarketRefreshRunRequest request)
    {
        if (operation.RequiresPayload && string.IsNullOrWhiteSpace(request.PayloadPath))
        {
            throw new InvalidOperationException($"market refresh {operation.OperationName} requires payload_path.");
        }
    }

    private static void ValidateStatusRequest(MarketRefreshStatusRequest request)
    {
        if (request.RecentRunLimit <= 0)
        {
            throw new ArgumentException("recent_run_limit must be greater than zero.", nameof(request));
        }

        if (request.RecentBatchLimit <= 0)
        {
            throw new ArgumentException("recent_batch_limit must be greater than zero.", nameof(request));
        }
    }

    private static void ValidateHealthRequest(MarketRefreshHealthRequest request)
    {
        ValidateStatusRequest(request);

        if (request.WarningLagMinutes <= 0)
        {
            throw new ArgumentException("warning_lag_minutes must be greater than zero.", nameof(request));
        }

        if (request.ErrorLagMinutes < request.WarningLagMinutes)
        {
            throw new ArgumentException("error_lag_minutes must be greater than or equal to warning_lag_minutes.", nameof(request));
        }
    }

    private static string NormalizeRequestedBy(string? requestedBy, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedBy)
            ? fallback
            : requestedBy!;
    }

    private static string NormalizeTriggerKind(string? triggerKind, string fallback)
    {
        return string.IsNullOrWhiteSpace(triggerKind)
            ? fallback
            : triggerKind!;
    }

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    private static string CreateRunId(string operationName)
    {
        return $"market-refresh-{operationName}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
    }

    private static string CreateBatchId()
    {
        return $"market-refresh-batch-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
    }

    private static string CreateTraceId(string suffix)
    {
        return $"cli.market-refresh.{suffix}:{Guid.NewGuid():N}";
    }

    private static string CreateRecoveryId()
    {
        return $"market-refresh-lease-recovery-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
    }

    private static void WriteJsonAtomically<T>(string path, T value)
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
                JsonSerializer.Serialize(stream, value, JsonOptions);
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

    private sealed class LeaseHandle : IDisposable
    {
        private readonly FileStream _stream;
        private readonly MarketRefreshRuntimeStateStore _stateStore;
        private readonly string _leaseId;
        private bool _disposed;

        public LeaseHandle(
            FileStream stream,
            MarketRefreshRuntimeStateStore stateStore,
            MarketRefreshLeaseState state)
        {
            _stream = stream;
            _stateStore = stateStore;
            _leaseId = state.LeaseId;
            LeaseId = state.LeaseId;
        }

        public string LeaseId { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
            _stateStore.Update(state =>
                state.ActiveLease?.LeaseId == _leaseId
                    ? state with { ActiveLease = null }
                    : state);
        }
    }

    private sealed class MarketRefreshRuntimeStateStore
    {
        private readonly string _path;
        private readonly string _lockPath;
        private readonly object _syncRoot = new();

        public MarketRefreshRuntimeStateStore(string path)
        {
            _path = path;
            _lockPath = $"{path}.lock";
        }

        public MarketRefreshStateSnapshot Load()
        {
            lock (_syncRoot)
            {
                return ReadSnapshot();
            }
        }

        public MarketRefreshRuntimeState Update(Func<MarketRefreshRuntimeState, MarketRefreshRuntimeState> apply)
        {
            ArgumentNullException.ThrowIfNull(apply);

            lock (_syncRoot)
            {
                using var lockStream = AcquireLock();
                var snapshot = ReadSnapshot();
                var nextState = apply(snapshot.State);
                WriteSnapshot(snapshot.Revision + 1, nextState);
                return nextState;
            }
        }

        private MarketRefreshStateSnapshot ReadSnapshot()
        {
            if (!File.Exists(_path))
            {
                return new MarketRefreshStateSnapshot(0, null, new MarketRefreshRuntimeState());
            }

            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!root.TryGetProperty("state", out var stateElement))
            {
                return new MarketRefreshStateSnapshot(
                    0,
                    null,
                    root.Deserialize<MarketRefreshRuntimeState>(JsonOptions) ?? new MarketRefreshRuntimeState());
            }

            var revision = root.TryGetProperty("revision", out var revisionElement) && revisionElement.TryGetInt64(out var value)
                ? value
                : 0L;
            var updatedAtUtc = root.TryGetProperty("updated_at_utc", out var updatedAtElement) && updatedAtElement.ValueKind == JsonValueKind.String
                ? updatedAtElement.GetDateTimeOffset()
                : (DateTimeOffset?)null;

            return new MarketRefreshStateSnapshot(
                revision,
                updatedAtUtc,
                stateElement.Deserialize<MarketRefreshRuntimeState>(JsonOptions) ?? new MarketRefreshRuntimeState());
        }

        private FileStream AcquireLock()
        {
            var directoryPath = Path.GetDirectoryName(_lockPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            IOException? lastException = null;
            for (var attempt = 0; attempt < LockAcquireAttemptCount; attempt++)
            {
                try
                {
                    return new FileStream(
                        _lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    Thread.Sleep(LockAcquireDelay);
                }
            }

            throw new IOException($"Timed out waiting for market refresh runtime state lock '{_lockPath}'.", lastException);
        }

        private void WriteSnapshot(long revision, MarketRefreshRuntimeState state)
        {
            var directoryPath = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
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
                    JsonSerializer.Serialize(
                        stream,
                        new MarketRefreshRuntimeEnvelope
                        {
                            StoreFormat = StateStoreFormat,
                            Revision = revision,
                            UpdatedAtUtc = DateTimeOffset.UtcNow,
                            State = state
                        },
                        JsonOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, _path);
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
    }

    private sealed record MarketRefreshFailureClassification(string Category, string ReasonCode, bool Retryable);

    private sealed record RetentionCleanupSummary(
        DateTimeOffset CleanedAtUtc,
        int DeletedRunLogs,
        int DeletedBatchLogs,
        int DeletedDeadLetters,
        int DeletedQuarantinePayloads,
        int RetainedRunLogs,
        int RetainedBatchLogs,
        int RetainedDeadLetters,
        int RetainedLeaseRecoveries,
        IReadOnlyList<string> Notes);
}

internal sealed record MarketRefreshStateSnapshot(
        long Revision,
        DateTimeOffset? UpdatedAtUtc,
        MarketRefreshRuntimeState State);

public sealed record MarketRefreshRunRequest
{
    public string? PayloadPath { get; init; }

    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public string? BatchId { get; init; }

    public string? BatchName { get; init; }

    public string? TriggerKind { get; init; }
}

public sealed record MarketRefreshBatchRequest
{
    public string? BatchName { get; init; }

    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public bool ContinueOnError { get; init; }

    public string? TriggerKind { get; init; }

    public IReadOnlyList<MarketRefreshBatchOperationRequest> Operations { get; init; } = Array.Empty<MarketRefreshBatchOperationRequest>();
}

public sealed record MarketRefreshBatchOperationRequest
{
    public string? Operation { get; init; }

    public string? PayloadPath { get; init; }

    public string? Cursor { get; init; }
}

public record MarketRefreshStatusRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public int RecentRunLimit { get; init; } = 10;

    public int RecentBatchLimit { get; init; } = 5;

    public bool ProbeLease { get; init; } = true;
}

public sealed record MarketRefreshHealthRequest : MarketRefreshStatusRequest
{
    public int WarningLagMinutes { get; init; } = 360;

    public int ErrorLagMinutes { get; init; } = 1440;
}

public sealed record MarketRefreshLeaseRecoveryRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }

    public bool ProbeLease { get; init; } = true;
}

public sealed record MarketRefreshCleanupRequest
{
    public string? MarketFactsDirectoryPath { get; init; }

    public string? RequestedBy { get; init; }
}

public sealed record MarketRefreshRunView
{
    public required string TraceId { get; init; }

    public required string RunId { get; init; }

    public string? BatchId { get; init; }

    public string? BatchName { get; init; }

    public required string RunKind { get; init; }

    public required string TriggerKind { get; init; }

    public required string Operation { get; init; }

    public required string Status { get; init; }

    public required string RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public required string IngressKind { get; init; }

    public string? PayloadPath { get; init; }

    public string? LeaseId { get; init; }

    public int AttemptCount { get; init; }

    public IReadOnlyList<int> RetryDelaysMs { get; init; } = Array.Empty<int>();

    public IReadOnlyList<MarketRefreshAttemptFailureView> AttemptFailures { get; init; } = Array.Empty<MarketRefreshAttemptFailureView>();

    public required string MarketFactsDirectoryPath { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public required string RunLogPath { get; init; }

    public MarketFactRefreshResult? RefreshResult { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public MarketRefreshFailureView? Failure { get; init; }

    public MarketRefreshDeadLetterView? DeadLetter { get; init; }

    public MarketRefreshLeaseRecoveryView? LeaseRecovery { get; init; }

    public string? Source { get; init; }

    public string? BundleVersion { get; init; }

    public string? CoverageSummary { get; init; }

    public string? TraceSummary { get; init; }

    public string? FreshnessSummary { get; init; }

    public string? DataProtectionStatus { get; init; }

    public IReadOnlyList<string> Anomalies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshBatchView
{
    public required string TraceId { get; init; }

    public required string BatchId { get; init; }

    public required string BatchName { get; init; }

    public required string RunKind { get; init; }

    public required string TriggerKind { get; init; }

    public required string Status { get; init; }

    public required string RequestedBy { get; init; }

    public string? Cursor { get; init; }

    public bool ContinueOnError { get; init; }

    public string? LeaseId { get; init; }

    public required string MarketFactsDirectoryPath { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public required string BatchLogPath { get; init; }

    public int DeadLetterCount { get; init; }

    public MarketRefreshLeaseRecoveryView? RecoveredLease { get; init; }

    public IReadOnlyList<MarketRefreshRunView> Runs { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshStatusView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RuntimeStatePath { get; init; }

    public required string RunLeaseLockPath { get; init; }

    public DateTimeOffset QueriedAtUtc { get; init; }

    public long RuntimeRevision { get; init; }

    public DateTimeOffset? RuntimeUpdatedAtUtc { get; init; }

    public required string FactProtectionMode { get; init; }

    public required string FactProtectionSummary { get; init; }

    public required MarketRefreshRetentionPolicyView RetentionPolicy { get; init; }

    public required MarketRefreshLeaseObservationView Lease { get; init; }

    public IReadOnlyList<MarketRefreshOperationState> Operations { get; init; } = Array.Empty<MarketRefreshOperationState>();

    public IReadOnlyList<MarketRefreshRunView> RecentRuns { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<MarketRefreshBatchView> RecentBatches { get; init; } = Array.Empty<MarketRefreshBatchView>();

    public IReadOnlyList<MarketRefreshDeadLetterView> RecentDeadLetters { get; init; } = Array.Empty<MarketRefreshDeadLetterView>();

    public IReadOnlyList<MarketRefreshLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketRefreshLeaseRecoveryView>();
}

public sealed record MarketRefreshHealthView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RuntimeStatePath { get; init; }

    public required string RunLeaseLockPath { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string OverallStatus { get; init; }

    public bool IsHealthy { get; init; }

    public int WarningLagMinutes { get; init; }

    public int ErrorLagMinutes { get; init; }

    public required string FactProtectionMode { get; init; }

    public required string FactProtectionSummary { get; init; }

    public required MarketRefreshRetentionPolicyView RetentionPolicy { get; init; }

    public required MarketRefreshLeaseObservationView Lease { get; init; }

    public IReadOnlyList<MarketRefreshOperationHealthView> Operations { get; init; } = Array.Empty<MarketRefreshOperationHealthView>();

    public IReadOnlyList<MarketRefreshRunView> RecentRuns { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<MarketRefreshBatchView> RecentBatches { get; init; } = Array.Empty<MarketRefreshBatchView>();

    public IReadOnlyList<MarketRefreshDeadLetterView> RecentDeadLetters { get; init; } = Array.Empty<MarketRefreshDeadLetterView>();

    public IReadOnlyList<MarketRefreshLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketRefreshLeaseRecoveryView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshLeaseObservationView
{
    public required string Status { get; init; }

    public bool LockHeld { get; init; }

    public MarketRefreshLeaseState? ActiveLease { get; init; }
}

public sealed record MarketRefreshOperationHealthView
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

    public long? LagMinutes { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string? LastError { get; init; }

    public int LastAttemptCount { get; init; }

    public string? LastBundleVersion { get; init; }

    public string? LastSource { get; init; }

    public string? LastCoverageSummary { get; init; }

    public string? LastTraceSummary { get; init; }

    public string? LastFreshnessSummary { get; init; }

    public string? LastFailureCategory { get; init; }

    public string? LastFailureReasonCode { get; init; }

    public string? LastDeadLetterPath { get; init; }

    public string? LastDataProtectionStatus { get; init; }

    public string? LastTraceId { get; init; }

    public IReadOnlyList<string> LastAnomalies { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshAttemptFailureView
{
    public int Attempt { get; init; }

    public required string Category { get; init; }

    public required string ReasonCode { get; init; }

    public required string Error { get; init; }
}

public sealed record MarketRefreshFailureView
{
    public required string Category { get; init; }

    public required string ReasonCode { get; init; }

    public required string Error { get; init; }

    public bool Retryable { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record MarketRefreshDeadLetterView
{
    public required string DeadLetterId { get; init; }

    public required string RunId { get; init; }

    public required string Operation { get; init; }

    public required string FailureCategory { get; init; }

    public required string FailureReasonCode { get; init; }

    public required string RequestedBy { get; init; }

    public required string TraceId { get; init; }

    public string? LeaseId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string? PayloadPath { get; init; }

    public string? QuarantinePayloadPath { get; init; }

    public required string RecordPath { get; init; }

    public bool PreservedExistingFacts { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshLeaseRecoveryView
{
    public required string RecoveryId { get; init; }

    public required string Status { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public DateTimeOffset? RecoveredAtUtc { get; init; }

    public required string RecoveryReason { get; init; }

    public bool LockHeld { get; init; }

    public MarketRefreshLeaseState? Lease { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshCleanupView
{
    public required string MarketFactsDirectoryPath { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset CleanedAtUtc { get; init; }

    public required MarketRefreshRetentionPolicyView RetentionPolicy { get; init; }

    public int DeletedRunLogs { get; init; }

    public int DeletedBatchLogs { get; init; }

    public int DeletedDeadLetters { get; init; }

    public int DeletedQuarantinePayloads { get; init; }

    public int RetainedRunLogs { get; init; }

    public int RetainedBatchLogs { get; init; }

    public int RetainedDeadLetters { get; init; }

    public int RetainedLeaseRecoveries { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshRetentionPolicyView
{
    public int RecentRunLimit { get; init; }

    public int RecentBatchLimit { get; init; }

    public int RecentDeadLetterLimit { get; init; }

    public int RecentLeaseRecoveryLimit { get; init; }
}

public sealed record MarketRefreshRuntimeState
{
    public MarketRefreshLeaseState? ActiveLease { get; init; }

    public IReadOnlyList<MarketRefreshOperationState> Operations { get; init; } = Array.Empty<MarketRefreshOperationState>();

    public IReadOnlyList<MarketRefreshRunView> RecentRuns { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<MarketRefreshBatchView> RecentBatches { get; init; } = Array.Empty<MarketRefreshBatchView>();

    public IReadOnlyList<MarketRefreshDeadLetterView> RecentDeadLetters { get; init; } = Array.Empty<MarketRefreshDeadLetterView>();

    public IReadOnlyList<MarketRefreshLeaseRecoveryView> RecentLeaseRecoveries { get; init; } = Array.Empty<MarketRefreshLeaseRecoveryView>();
}

public sealed record MarketRefreshLeaseState
{
    public required string LeaseId { get; init; }

    public required string OwnerId { get; init; }

    public string? BatchId { get; init; }

    public string? BatchName { get; init; }

    public required string RequestedBy { get; init; }

    public DateTimeOffset AcquiredAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record MarketRefreshOperationState
{
    public required string Operation { get; init; }

    public string? LastRunId { get; init; }

    public string? LastStatus { get; init; }

    public string? LastRequestedCursor { get; init; }

    public string? LastSucceededCursor { get; init; }

    public string? LastPayloadPath { get; init; }

    public DateTimeOffset? LastStartedAtUtc { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string? LastError { get; init; }

    public int LastAttemptCount { get; init; }

    public string? LastBundleVersion { get; init; }

    public string? LastSource { get; init; }

    public string? LastCoverageSummary { get; init; }

    public string? LastTraceSummary { get; init; }

    public string? LastFreshnessSummary { get; init; }

    public string? LastFailureCategory { get; init; }

    public string? LastFailureReasonCode { get; init; }

    public string? LastDeadLetterPath { get; init; }

    public string? LastDataProtectionStatus { get; init; }

    public string? LastTraceId { get; init; }

    public IReadOnlyList<string> LastAnomalies { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshRuntimeEnvelope
{
    public required string StoreFormat { get; init; }

    public long Revision { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public required MarketRefreshRuntimeState State { get; init; }
}

public sealed record MarketRefreshOperationKind(string OperationName, bool RequiresPayload, string IngressKind)
{
    public static readonly MarketRefreshOperationKind Snapshot = new("snapshot", true, "local_payload_file");
    public static readonly MarketRefreshOperationKind History = new("history", true, "local_payload_file");
    public static readonly MarketRefreshOperationKind Grants = new("grants", true, "local_payload_file");
    public static readonly MarketRefreshOperationKind RebuildStatistics = new("rebuild-statistics", false, "local_store_rebuild");

    public static MarketRefreshOperationKind Parse(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "snapshot" => Snapshot,
            "history" => History,
            "grants" => Grants,
            "rebuild-statistics" => RebuildStatistics,
            _ => throw new InvalidOperationException(
                $"Unknown market refresh operation '{value}'. Supported values: snapshot, history, grants, rebuild-statistics.")
        };
    }
}

internal sealed record MarketRefreshRuntimePaths(
    string StateDirectoryPath,
    string StatePath,
    string LeaseLockPath,
    string RunDirectoryPath,
    string BatchDirectoryPath,
    string DeadLetterDirectoryPath,
    string QuarantineDirectoryPath)
{
    public static MarketRefreshRuntimePaths EnsureLayout(string marketFactsDirectoryPath)
    {
        Directory.CreateDirectory(marketFactsDirectoryPath);

        var stateDirectoryPath = Path.Combine(marketFactsDirectoryPath, "refresh-runtime");
        var statePath = Path.Combine(stateDirectoryPath, "state.json");
        var leaseLockPath = Path.Combine(stateDirectoryPath, "run.lock");
        var runDirectoryPath = Path.Combine(marketFactsDirectoryPath, "refresh-runs");
        var batchDirectoryPath = Path.Combine(marketFactsDirectoryPath, "refresh-batches");
        var deadLetterDirectoryPath = Path.Combine(marketFactsDirectoryPath, "refresh-dead-letters");
        var quarantineDirectoryPath = Path.Combine(marketFactsDirectoryPath, "refresh-quarantine");

        Directory.CreateDirectory(stateDirectoryPath);
        Directory.CreateDirectory(runDirectoryPath);
        Directory.CreateDirectory(batchDirectoryPath);
        Directory.CreateDirectory(deadLetterDirectoryPath);
        Directory.CreateDirectory(quarantineDirectoryPath);

        return new MarketRefreshRuntimePaths(
            stateDirectoryPath,
            statePath,
            leaseLockPath,
            runDirectoryPath,
            batchDirectoryPath,
            deadLetterDirectoryPath,
            quarantineDirectoryPath);
    }
}
