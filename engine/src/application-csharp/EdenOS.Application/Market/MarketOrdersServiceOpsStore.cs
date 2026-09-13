using System.Text.Json;

namespace EdenOS.Application.Market;

public sealed class MarketOrdersServiceOpsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);

    private readonly TimeProvider _timeProvider;

    public MarketOrdersServiceOpsStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal MarketOrdersServiceArtifactPathsView ResolveArtifactPaths(MarketOrdersServiceOptions options, string cycleId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var serviceStateDirectoryPath = MarketOrdersServiceScheduler.ResolveServiceStateDirectoryPath(options);
        var cycleLogsDirectoryPath = Path.Combine(serviceStateDirectoryPath, "cycles");

        return new MarketOrdersServiceArtifactPathsView
        {
            ServiceStateDirectoryPath = serviceStateDirectoryPath,
            HeartbeatPath = MarketOrdersServiceScheduler.ResolveHeartbeatPath(options),
            CycleLogsDirectoryPath = cycleLogsDirectoryPath,
            CycleLogPath = Path.Combine(cycleLogsDirectoryPath, $"{cycleId}.json"),
            CycleHistoryPath = Path.Combine(serviceStateDirectoryPath, "cycle-history.json"),
            SummaryPath = Path.Combine(serviceStateDirectoryPath, "operator-summary.json"),
            HealthPath = Path.Combine(serviceStateDirectoryPath, "operator-health.json"),
            MetricsPath = Path.Combine(serviceStateDirectoryPath, "operator-metrics.json"),
            AlertsPath = Path.Combine(serviceStateDirectoryPath, "operator-alerts.json")
        };
    }

    internal MarketOrdersServiceOpsWriteResult WriteArtifacts(
        MarketOrdersServiceOptions options,
        MarketOrdersServiceCycleView cycle,
        MarketOrdersRuntimeHealthView? runtimeHealth)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cycle);

        var now = _timeProvider.GetUtcNow();
        EnsureDirectory(cycle.ArtifactPaths.ServiceStateDirectoryPath);
        EnsureDirectory(cycle.ArtifactPaths.CycleLogsDirectoryPath);

        WriteJsonAtomically(cycle.ArtifactPaths.CycleLogPath, cycle);

        var existingHistory = TryReadJson<MarketOrdersServiceCycleHistoryView>(cycle.ArtifactPaths.CycleHistoryPath);
        var retainedEntries = BuildRetainedEntries(cycle, existingHistory);
        var retainedPaths = retainedEntries
            .Select(entry => entry.CycleLogPath)
            .Append(cycle.ArtifactPaths.CycleLogPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prunedCycleLogPaths = DeleteOrphanedCycleLogs(cycle.ArtifactPaths.CycleLogsDirectoryPath, retainedPaths);

        var historyNotes = new List<string>();
        if (prunedCycleLogPaths.Count > 0)
        {
            historyNotes.Add($"Pruned {prunedCycleLogPaths.Count} cycle log file(s) outside the retained history window.");
        }

        var history = new MarketOrdersServiceCycleHistoryView
        {
            ServiceName = cycle.ServiceName,
            GeneratedAtUtc = now,
            ArtifactPaths = cycle.ArtifactPaths,
            RetentionPolicy = cycle.RetentionPolicy,
            RetainedCycleCount = retainedEntries.Count,
            PrunedCycleLogCount = prunedCycleLogPaths.Count,
            RecentCycles = retainedEntries,
            Notes = historyNotes
        };

        var alerts = BuildAlerts(now, options, cycle, history, runtimeHealth);
        var summary = BuildSummary(now, cycle, history, runtimeHealth, alerts);
        var health = BuildHealth(now, cycle, history, runtimeHealth, alerts);
        var metrics = BuildMetrics(now, options, history, runtimeHealth, cycle);

        WriteJsonAtomically(cycle.ArtifactPaths.CycleHistoryPath, history);
        WriteJsonAtomically(cycle.ArtifactPaths.SummaryPath, summary);
        WriteJsonAtomically(cycle.ArtifactPaths.HealthPath, health);
        WriteJsonAtomically(cycle.ArtifactPaths.MetricsPath, metrics);
        WriteJsonAtomically(cycle.ArtifactPaths.AlertsPath, alerts);

        return new MarketOrdersServiceOpsWriteResult(
            history.RetainedCycleCount,
            history.PrunedCycleLogCount,
            prunedCycleLogPaths);
    }

    private static List<MarketOrdersServiceCycleHistoryEntryView> BuildRetainedEntries(
        MarketOrdersServiceCycleView cycle,
        MarketOrdersServiceCycleHistoryView? existingHistory)
    {
        var latestEntry = BuildEntry(cycle);
        var retainedEntries = existingHistory?.RecentCycles
            .Where(entry => !string.Equals(entry.CycleId, cycle.CycleId, StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? new List<MarketOrdersServiceCycleHistoryEntryView>();

        retainedEntries.Insert(0, latestEntry);
        retainedEntries.Sort(static (left, right) => right.CompletedAtUtc.CompareTo(left.CompletedAtUtc));

        if (retainedEntries.Count > cycle.RetentionPolicy.CycleHistoryLimit)
        {
            retainedEntries.RemoveRange(cycle.RetentionPolicy.CycleHistoryLimit, retainedEntries.Count - cycle.RetentionPolicy.CycleHistoryLimit);
        }

        return retainedEntries;
    }

    private static MarketOrdersServiceCycleHistoryEntryView BuildEntry(MarketOrdersServiceCycleView cycle)
    {
        return new MarketOrdersServiceCycleHistoryEntryView
        {
            CycleId = cycle.CycleId,
            CycleNumber = cycle.CycleNumber,
            HostInstanceId = cycle.HostInstanceId,
            Status = cycle.Status,
            PlannedAtUtc = cycle.PlannedAtUtc,
            StartedAtUtc = cycle.StartedAtUtc,
            CompletedAtUtc = cycle.CompletedAtUtc,
            DueOperationCount = cycle.Operations.Count(operation => operation.IsDue),
            ExecutedRunCount = cycle.ExecutedRuns.Count,
            ErrorCount = cycle.Errors.Count,
            LeaseStatusAfterCycle = cycle.LeaseStatusAfterCycle,
            RuntimeHealthStatusAfterCycle = cycle.RuntimeHealthStatusAfterCycle,
            CycleLogPath = cycle.ArtifactPaths.CycleLogPath,
            Operations = cycle.Operations
                .Select(operation => new MarketOrdersServiceCycleHistoryOperationView
                {
                    Operation = operation.Operation,
                    Enabled = operation.Enabled,
                    IsDue = operation.IsDue,
                    IntervalMinutes = operation.IntervalMinutes,
                    Decision = operation.Decision,
                    NextDueAtUtc = operation.NextDueAtUtc,
                    LastRuntimeStatus = operation.LastStatus,
                    ExecutedRunCount = cycle.ExecutedRuns.Count(run => string.Equals(run.Operation, operation.Operation, StringComparison.OrdinalIgnoreCase)),
                    FailedRunCount = cycle.ExecutedRuns.Count(run =>
                        string.Equals(run.Operation, operation.Operation, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
                })
                .OrderBy(operation => operation.Operation, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static MarketOrdersServiceSummaryView BuildSummary(
        DateTimeOffset generatedAtUtc,
        MarketOrdersServiceCycleView cycle,
        MarketOrdersServiceCycleHistoryView history,
        MarketOrdersRuntimeHealthView? runtimeHealth,
        MarketOrdersServiceAlertView alerts)
    {
        var notes = new List<string>();
        if (history.PrunedCycleLogCount > 0)
        {
            notes.Add($"Cycle retention pruned {history.PrunedCycleLogCount} old cycle log file(s).");
        }

        if (runtimeHealth is not null)
        {
            notes.AddRange(runtimeHealth.FutureWorkerBoundaryNotes);

            if (!string.Equals(runtimeHealth.IngressBoundaryStatus, "healthy", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(runtimeHealth.IngressBoundaryStatus, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("Ingress boundary freshness is impaired; the host can still heartbeat while the configured payload file stays stale or missing.");
            }
        }

        return new MarketOrdersServiceSummaryView
        {
            ServiceName = cycle.ServiceName,
            GeneratedAtUtc = generatedAtUtc,
            HostInstanceId = cycle.HostInstanceId,
            LatestCycleId = cycle.CycleId,
            LatestCycleStatus = cycle.Status,
            LatestCycleCompletedAtUtc = cycle.CompletedAtUtc,
            OverallHealthStatus = DetermineServiceOverallStatus(cycle.Status, runtimeHealth?.OverallStatus, alerts.AlertStatus, history.RecentCycles),
            AlertStatus = alerts.AlertStatus,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.RuntimeHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            IngressBoundaryStatus = runtimeHealth?.IngressBoundaryStatus ?? "unknown",
            DueOperationCount = cycle.Operations.Count(operation => operation.IsDue),
            ExecutedRunCount = cycle.ExecutedRuns.Count,
            ErrorCount = cycle.Errors.Count,
            ArtifactPaths = cycle.ArtifactPaths,
            RetentionPolicy = cycle.RetentionPolicy,
            RuntimeRetentionPolicy = runtimeHealth?.RetentionPolicy ?? cycle.RuntimeRetentionPolicy,
            CurrentStoreSummary = runtimeHealth?.CurrentStoreSummary ?? cycle.CurrentStoreSummary,
            RuntimeRecentRunCount = runtimeHealth?.RecentRuns.Count ?? 0,
            RuntimeRecentSnapshotArtifactCount = runtimeHealth?.RecentSnapshotArtifacts.Count ?? 0,
            RuntimeRecentLeaseRecoveryCount = runtimeHealth?.RecentLeaseRecoveries.Count ?? 0,
            StalePayloadOperationCount = CountStalePayloadOperations(runtimeHealth),
            MissingPayloadOperationCount = CountMissingPayloadOperations(runtimeHealth),
            RetainedCycleCount = history.RetainedCycleCount,
            PrunedCycleLogCount = history.PrunedCycleLogCount,
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static MarketOrdersServiceHealthView BuildHealth(
        DateTimeOffset evaluatedAtUtc,
        MarketOrdersServiceCycleView cycle,
        MarketOrdersServiceCycleHistoryView history,
        MarketOrdersRuntimeHealthView? runtimeHealth,
        MarketOrdersServiceAlertView alerts)
    {
        var notes = new List<string>();
        if (!string.Equals(cycle.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cycle.Status, "idle", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"Latest cycle status is '{cycle.Status}'.");
        }

        if (history.PrunedCycleLogCount > 0)
        {
            notes.Add($"History retention removed {history.PrunedCycleLogCount} old cycle log file(s).");
        }

        if (runtimeHealth is not null)
        {
            notes.AddRange(runtimeHealth.FutureWorkerBoundaryNotes);

            if (runtimeHealth.Lease.Status == "stale_orphaned")
            {
                notes.Add("Runtime lease state is stale_orphaned; confirm no active import is still running, then use the formal market orders recover-lease entrypoint.");
            }

            if (runtimeHealth.RecentLeaseRecoveries.Count > 0)
            {
                notes.Add($"Runtime recorded {runtimeHealth.RecentLeaseRecoveries.Count} recent lease recovery entr{(runtimeHealth.RecentLeaseRecoveries.Count == 1 ? "y" : "ies")}.");
            }

            if (!string.Equals(runtimeHealth.IngressBoundaryStatus, "healthy", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(runtimeHealth.IngressBoundaryStatus, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("Ingress boundary freshness is impaired; inspect payload-file age and advancement before treating orders as fresh.");
            }
        }

        return new MarketOrdersServiceHealthView
        {
            ServiceName = cycle.ServiceName,
            EvaluatedAtUtc = evaluatedAtUtc,
            OverallStatus = DetermineServiceOverallStatus(cycle.Status, runtimeHealth?.OverallStatus, alerts.AlertStatus, history.RecentCycles),
            AlertStatus = alerts.AlertStatus,
            LatestCycleStatus = cycle.Status,
            LatestCycleCompletedAtUtc = cycle.CompletedAtUtc,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.RuntimeHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            IngressBoundaryStatus = runtimeHealth?.IngressBoundaryStatus ?? "unknown",
            DueOperationCount = cycle.Operations.Count(operation => operation.IsDue),
            ExecutedRunCount = cycle.ExecutedRuns.Count,
            ErrorCount = cycle.Errors.Count,
            ArtifactPaths = cycle.ArtifactPaths,
            RetentionPolicy = cycle.RetentionPolicy,
            RetainedCycleCount = history.RetainedCycleCount,
            RuntimeHealth = runtimeHealth,
            RecentCycles = history.RecentCycles,
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static MarketOrdersServiceAlertView BuildAlerts(
        DateTimeOffset evaluatedAtUtc,
        MarketOrdersServiceOptions options,
        MarketOrdersServiceCycleView cycle,
        MarketOrdersServiceCycleHistoryView history,
        MarketOrdersRuntimeHealthView? runtimeHealth)
    {
        var alerts = new List<MarketOrdersServiceAlertItemView>();
        var thresholds = new MarketOrdersServiceAlertThresholdsView
        {
            ConsecutiveImpairedCycleAlertThreshold = options.ConsecutiveImpairedCycleAlertThreshold,
            ConsecutiveBlockedLeaseAlertThreshold = options.ConsecutiveBlockedLeaseAlertThreshold
        };

        var consecutiveImpairedCycleCount = CountConsecutiveCycles(
            history.RecentCycles,
            static entry => IsImpairedCycleStatus(entry.Status));
        var consecutiveBlockedLeaseCycleCount = CountConsecutiveCycles(
            history.RecentCycles,
            static entry => string.Equals(entry.Status, "blocked_by_active_lease", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(cycle.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            alerts.Add(new MarketOrdersServiceAlertItemView
            {
                Severity = "critical",
                Code = "latest_cycle_failed",
                Summary = "Latest market orders service cycle failed before any due import completed successfully.",
                Source = "service_cycle",
                RecommendedAction = "Inspect the cycle log and runtime failures before restarting the host.",
                RelatedPath = cycle.ArtifactPaths.CycleLogPath
            });
        }
        else if (string.Equals(cycle.Status, "completed_with_failures", StringComparison.OrdinalIgnoreCase))
        {
            alerts.Add(new MarketOrdersServiceAlertItemView
            {
                Severity = "warning",
                Code = "latest_cycle_completed_with_failures",
                Summary = "Latest market orders service cycle completed with one or more failed import attempts.",
                Source = "service_cycle",
                RecommendedAction = "Inspect the latest cycle log and runtime health before the next unattended cycle.",
                RelatedPath = cycle.ArtifactPaths.CycleLogPath
            });
        }

        if (consecutiveImpairedCycleCount >= thresholds.ConsecutiveImpairedCycleAlertThreshold)
        {
            alerts.Add(new MarketOrdersServiceAlertItemView
            {
                Severity = "warning",
                Code = "consecutive_impaired_cycles",
                Summary = $"Service recorded {consecutiveImpairedCycleCount} consecutive impaired cycle(s).",
                Source = "service_history",
                RecommendedAction = "Inspect operator health and cycle history before leaving the host unattended.",
                RelatedPath = cycle.ArtifactPaths.CycleHistoryPath
            });
        }

        if (consecutiveBlockedLeaseCycleCount >= thresholds.ConsecutiveBlockedLeaseAlertThreshold)
        {
            alerts.Add(new MarketOrdersServiceAlertItemView
            {
                Severity = "critical",
                Code = "blocked_by_active_lease_window",
                Summary = $"Service has been blocked by an active lease for {consecutiveBlockedLeaseCycleCount} consecutive cycle(s).",
                Source = "lease",
                RecommendedAction = "Confirm no valid import is still running, then use the formal market orders recover-lease entrypoint.",
                RelatedPath = cycle.ArtifactPaths.HealthPath
            });
        }

        if (runtimeHealth is not null)
        {
            if (string.Equals(runtimeHealth.Lease.Status, "stale_orphaned", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new MarketOrdersServiceAlertItemView
                {
                    Severity = "warning",
                    Code = "runtime_lease_stale_orphaned",
                    Summary = "Orders runtime state reports a stale_orphaned lease.",
                    Source = "lease",
                    RecommendedAction = "Confirm no valid import is still running, then use the formal market orders recover-lease entrypoint.",
                    RelatedPath = cycle.ArtifactPaths.HealthPath
                });
            }

            if (string.Equals(runtimeHealth.OverallStatus, "unhealthy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtimeHealth.OverallStatus, "error", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new MarketOrdersServiceAlertItemView
                {
                    Severity = "critical",
                    Code = "runtime_health_unhealthy",
                    Summary = "Orders runtime health is reporting an unhealthy state.",
                    Source = "runtime_health",
                    RecommendedAction = "Inspect runtime health, recent failures, and snapshot freshness before allowing another unattended cycle.",
                    RelatedPath = cycle.ArtifactPaths.HealthPath
                });
            }
            else if (string.Equals(runtimeHealth.OverallStatus, "warning", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new MarketOrdersServiceAlertItemView
                {
                    Severity = "warning",
                    Code = "runtime_health_warning",
                    Summary = "Orders runtime health is reporting a warning state.",
                    Source = "runtime_health",
                    RecommendedAction = "Inspect stale scope retention and snapshot age before treating the service as healthy.",
                    RelatedPath = cycle.ArtifactPaths.HealthPath
                });
            }

            foreach (var operation in runtimeHealth.Operations.OrderBy(entry => entry.Operation, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(operation.IngressBoundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketOrdersServiceAlertItemView
                    {
                        Severity = "critical",
                        Code = operation.IngressBoundary.PayloadExists ? "stale_payload_ingress_boundary" : "ingress_payload_missing",
                        Summary = $"{operation.Operation} ingress boundary is {operation.IngressBoundary.Status}: {operation.IngressBoundary.Summary}",
                        Source = "ingress_boundary",
                        Operation = operation.Operation,
                        RecommendedAction = operation.IngressBoundary.PayloadExists
                            ? "Refresh or replace the upstream payload file feeding this operation; the host can keep heartbeating without ingesting newer payloads."
                            : "Restore the configured payload file or point the service back at a readable ingress payload before leaving the host unattended.",
                        RelatedPath = operation.IngressBoundary.PayloadPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }
                else if (string.Equals(operation.IngressBoundary.Status, "warning", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketOrdersServiceAlertItemView
                    {
                        Severity = "warning",
                        Code = "stale_payload_ingress_boundary",
                        Summary = $"{operation.Operation} ingress boundary is {operation.IngressBoundary.Status}: {operation.IngressBoundary.Summary}",
                        Source = "ingress_boundary",
                        Operation = operation.Operation,
                        RecommendedAction = "Check whether the upstream payload file is still being refreshed; host liveness alone does not mean newer orders are arriving.",
                        RelatedPath = operation.IngressBoundary.PayloadPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }

                if (string.Equals(operation.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketOrdersServiceAlertItemView
                    {
                        Severity = "critical",
                        Code = "operation_unhealthy",
                        Summary = BuildOperationAlertSummary(operation),
                        Source = "runtime_operation",
                        Operation = operation.Operation,
                        RecommendedAction = "Inspect the failing import payload path, run log, and runtime health before the next unattended cycle.",
                        RelatedPath = operation.LastSnapshotArtifactPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }
                else if (string.Equals(operation.Status, "warning", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketOrdersServiceAlertItemView
                    {
                        Severity = "warning",
                        Code = "operation_warning",
                        Summary = BuildOperationAlertSummary(operation),
                        Source = "runtime_operation",
                        Operation = operation.Operation,
                        RecommendedAction = "Inspect lag, retained stale scopes, and recent failures for the orders runtime.",
                        RelatedPath = operation.LastSnapshotArtifactPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }
            }
        }

        var alertStatus = alerts.Any(alert => string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase))
            ? "critical"
            : alerts.Count > 0
                ? "warning"
                : "clear";

        return new MarketOrdersServiceAlertView
        {
            ServiceName = options.ServiceName,
            EvaluatedAtUtc = evaluatedAtUtc,
            AlertStatus = alertStatus,
            LatestCycleStatus = cycle.Status,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.RuntimeHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            IngressBoundaryStatus = runtimeHealth?.IngressBoundaryStatus ?? "unknown",
            ConsecutiveImpairedCycleCount = consecutiveImpairedCycleCount,
            ConsecutiveBlockedLeaseCycleCount = consecutiveBlockedLeaseCycleCount,
            Thresholds = thresholds,
            ArtifactPaths = cycle.ArtifactPaths,
            Alerts = alerts,
            RecommendedActions = alerts
                .Select(alert => alert.RecommendedAction)
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static MarketOrdersServiceMetricsView BuildMetrics(
        DateTimeOffset generatedAtUtc,
        MarketOrdersServiceOptions options,
        MarketOrdersServiceCycleHistoryView history,
        MarketOrdersRuntimeHealthView? runtimeHealth,
        MarketOrdersServiceCycleView cycle)
    {
        var operations = options.Operations
            .Select(option =>
            {
                var operationName = ParseOperation(option.Operation);
                var runtimeOperation = runtimeHealth?.Operations
                    .FirstOrDefault(operation => string.Equals(operation.Operation, operationName, StringComparison.OrdinalIgnoreCase));
                var historyOperations = history.RecentCycles
                    .SelectMany(entry => entry.Operations.Select(operation => (Cycle: entry, Operation: operation)))
                    .Where(entry => string.Equals(entry.Operation.Operation, operationName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var latestHistoryOperation = historyOperations
                    .OrderByDescending(entry => entry.Cycle.CompletedAtUtc)
                    .Select(entry => entry.Operation)
                    .FirstOrDefault();

                return new MarketOrdersServiceOperationMetricsView
                {
                    Operation = operationName,
                    Enabled = option.Enabled,
                    IntervalMinutes = option.IntervalMinutes,
                    DueCyclesRetained = historyOperations.Count(entry => entry.Operation.IsDue),
                    ExecutedRunsRetained = historyOperations.Sum(entry => entry.Operation.ExecutedRunCount),
                    FailedRunsRetained = historyOperations.Sum(entry => entry.Operation.FailedRunCount),
                    LastDecision = latestHistoryOperation?.Decision,
                    LastCycleStatus = historyOperations
                        .OrderByDescending(entry => entry.Cycle.CompletedAtUtc)
                        .Select(entry => entry.Cycle.Status)
                        .FirstOrDefault(),
                    CurrentRuntimeStatus = runtimeOperation?.Status,
                    IngressBoundaryStatus = runtimeOperation?.IngressBoundary.Status,
                    LatestSnapshotAgeMinutes = runtimeOperation?.LatestSnapshotAgeMinutes,
                    PayloadExists = runtimeOperation?.IngressBoundary.PayloadExists ?? false,
                    PayloadAgeMinutes = runtimeOperation?.IngressBoundary.PayloadAgeMinutes,
                    PayloadAdvancedSinceLastSuccess = runtimeOperation?.IngressBoundary.PayloadAdvancedSinceLastSuccess,
                    ConsecutiveFailures = runtimeOperation?.ConsecutiveFailures ?? 0,
                    CurrentStaleScopeCount = runtimeOperation?.StaleScopeCount ?? 0,
                    CurrentMaxRetainedLagMinutes = runtimeOperation?.MaxRetainedLagMinutes ?? 0,
                    LastSucceededAtUtc = runtimeOperation?.LastSucceededAtUtc,
                    NextDueAtUtc = latestHistoryOperation?.NextDueAtUtc
                };
            })
            .OrderBy(operation => operation.Operation, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MarketOrdersServiceMetricsView
        {
            ServiceName = options.ServiceName,
            GeneratedAtUtc = generatedAtUtc,
            RetentionPolicy = new MarketOrdersServiceRetentionPolicyView
            {
                CycleHistoryLimit = options.CycleHistoryLimit
            },
            RuntimeRetentionPolicy = runtimeHealth?.RetentionPolicy ?? cycle.RuntimeRetentionPolicy,
            CurrentStoreSummary = runtimeHealth?.CurrentStoreSummary ?? cycle.CurrentStoreSummary,
            RetainedCycleCount = history.RetainedCycleCount,
            TotalCyclesRetained = history.RecentCycles.Count,
            CompletedCyclesRetained = history.RecentCycles.Count(entry => string.Equals(entry.Status, "completed", StringComparison.OrdinalIgnoreCase)),
            CompletedWithFailuresCyclesRetained = history.RecentCycles.Count(entry => string.Equals(entry.Status, "completed_with_failures", StringComparison.OrdinalIgnoreCase)),
            IdleCyclesRetained = history.RecentCycles.Count(entry => string.Equals(entry.Status, "idle", StringComparison.OrdinalIgnoreCase)),
            BlockedCyclesRetained = history.RecentCycles.Count(entry => string.Equals(entry.Status, "blocked_by_active_lease", StringComparison.OrdinalIgnoreCase)),
            FailedCyclesRetained = history.RecentCycles.Count(entry => string.Equals(entry.Status, "failed", StringComparison.OrdinalIgnoreCase)),
            TotalDueOperationsRetained = history.RecentCycles.Sum(entry => entry.DueOperationCount),
            TotalExecutedRunsRetained = history.RecentCycles.Sum(entry => entry.ExecutedRunCount),
            CurrentRuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.RuntimeHealthStatusAfterCycle,
            CurrentLeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            CurrentIngressBoundaryStatus = runtimeHealth?.IngressBoundaryStatus ?? "unknown",
            RuntimeRecentRunCount = runtimeHealth?.RecentRuns.Count ?? 0,
            RuntimeRecentSnapshotArtifactCount = runtimeHealth?.RecentSnapshotArtifacts.Count ?? 0,
            RuntimeRecentLeaseRecoveryCount = runtimeHealth?.RecentLeaseRecoveries.Count ?? 0,
            IngressBoundaryWarningOperationCount = runtimeHealth?.IngressBoundaries.Count(boundary => string.Equals(boundary.Status, "warning", StringComparison.OrdinalIgnoreCase)) ?? 0,
            IngressBoundaryUnhealthyOperationCount = runtimeHealth?.IngressBoundaries.Count(boundary => string.Equals(boundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase)) ?? 0,
            StalePayloadOperationCount = CountStalePayloadOperations(runtimeHealth),
            Operations = operations
        };
    }

    private static string DetermineServiceOverallStatus(
        string latestCycleStatus,
        string? runtimeHealthStatus,
        string alertStatus,
        IReadOnlyList<MarketOrdersServiceCycleHistoryEntryView> recentCycles)
    {
        if (string.Equals(latestCycleStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtimeHealthStatus, "unhealthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtimeHealthStatus, "error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(alertStatus, "critical", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        if (string.Equals(latestCycleStatus, "completed_with_failures", StringComparison.OrdinalIgnoreCase)
            || string.Equals(latestCycleStatus, "blocked_by_active_lease", StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtimeHealthStatus, "warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(alertStatus, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        if (recentCycles.Take(3).Any(cycle => string.Equals(cycle.Status, "failed", StringComparison.OrdinalIgnoreCase)))
        {
            return "warning";
        }

        return "healthy";
    }

    private static int CountConsecutiveCycles(
        IReadOnlyList<MarketOrdersServiceCycleHistoryEntryView> recentCycles,
        Func<MarketOrdersServiceCycleHistoryEntryView, bool> predicate)
    {
        var count = 0;
        foreach (var cycle in recentCycles.OrderByDescending(entry => entry.CompletedAtUtc))
        {
            if (!predicate(cycle))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool IsImpairedCycleStatus(string status)
    {
        return status switch
        {
            "completed" => false,
            "idle" => false,
            _ => true
        };
    }

    private static string BuildOperationAlertSummary(MarketOrdersRuntimeOperationHealthView operation)
    {
        if (operation.ConsecutiveFailures > 0)
        {
            return $"{operation.Operation} is {operation.Status} with {operation.ConsecutiveFailures} consecutive failure(s) and snapshot_age={FormatAgeMinutes(operation.LatestSnapshotAgeMinutes)}.";
        }

        return $"{operation.Operation} is {operation.Status} with snapshot_age={FormatAgeMinutes(operation.LatestSnapshotAgeMinutes)}.";
    }

    private static int CountStalePayloadOperations(MarketOrdersRuntimeHealthView? runtimeHealth)
    {
        return runtimeHealth?.IngressBoundaries.Count(boundary =>
            boundary.PayloadExists
            && boundary.PayloadAdvancedSinceLastSuccess == false
            && (string.Equals(boundary.Status, "warning", StringComparison.OrdinalIgnoreCase)
                || string.Equals(boundary.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))) ?? 0;
    }

    private static int CountMissingPayloadOperations(MarketOrdersRuntimeHealthView? runtimeHealth)
    {
        return runtimeHealth?.IngressBoundaries.Count(boundary => !boundary.PayloadExists && !string.IsNullOrWhiteSpace(boundary.PayloadPath)) ?? 0;
    }

    private static string FormatAgeMinutes(long? ageMinutes)
    {
        return ageMinutes.HasValue ? $"{ageMinutes.Value}m" : "unknown";
    }

    private static string ParseOperation(string? operation)
    {
        if (string.Equals(operation, MarketOrdersServiceConventions.OrdersSnapshotOperation, StringComparison.OrdinalIgnoreCase))
        {
            return MarketOrdersServiceConventions.OrdersSnapshotOperation;
        }

        throw new InvalidOperationException(
            $"Unsupported market orders service operation '{operation}'. Supported operations: {MarketOrdersServiceConventions.OrdersSnapshotOperation}.");
    }

    private static List<string> DeleteOrphanedCycleLogs(string cycleLogsDirectoryPath, IReadOnlySet<string> retainedPaths)
    {
        if (!Directory.Exists(cycleLogsDirectoryPath))
        {
            return new List<string>();
        }

        var prunedPaths = new List<string>();
        foreach (var filePath in Directory.GetFiles(cycleLogsDirectoryPath, "*.json"))
        {
            if (retainedPaths.Contains(filePath))
            {
                continue;
            }

            File.Delete(filePath);
            prunedPaths.Add(filePath);
        }

        return prunedPaths;
    }

    private static T? TryReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
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
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
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

    private static void EnsureDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}

public sealed record MarketOrdersServiceOpsWriteResult(
    int RetainedCycleCount,
    int PrunedCycleLogCount,
    IReadOnlyList<string> PrunedCycleLogPaths);
