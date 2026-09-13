using System.Text.Json;

namespace EdenOS.Application.Market;

public sealed class MarketRefreshServiceOpsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);

    private readonly TimeProvider _timeProvider;

    public MarketRefreshServiceOpsStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal MarketRefreshServiceArtifactPathsView ResolveArtifactPaths(MarketRefreshServiceOptions options, string cycleId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var serviceStateDirectoryPath = MarketRefreshServiceScheduler.ResolveServiceStateDirectoryPath(options);
        var cycleLogsDirectoryPath = Path.Combine(serviceStateDirectoryPath, "cycles");

        return new MarketRefreshServiceArtifactPathsView
        {
            ServiceStateDirectoryPath = serviceStateDirectoryPath,
            HeartbeatPath = MarketRefreshServiceScheduler.ResolveHeartbeatPath(options),
            CycleLogsDirectoryPath = cycleLogsDirectoryPath,
            CycleLogPath = Path.Combine(cycleLogsDirectoryPath, $"{cycleId}.json"),
            CycleHistoryPath = Path.Combine(serviceStateDirectoryPath, "cycle-history.json"),
            SummaryPath = Path.Combine(serviceStateDirectoryPath, "operator-summary.json"),
            HealthPath = Path.Combine(serviceStateDirectoryPath, "operator-health.json"),
            MetricsPath = Path.Combine(serviceStateDirectoryPath, "operator-metrics.json"),
            AlertsPath = Path.Combine(serviceStateDirectoryPath, "operator-alerts.json")
        };
    }

    internal MarketRefreshServiceOpsWriteResult WriteArtifacts(
        MarketRefreshServiceOptions options,
        MarketRefreshServiceCycleView cycle,
        MarketRefreshHealthView? runtimeHealth)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cycle);

        var now = _timeProvider.GetUtcNow();
        EnsureDirectory(cycle.ArtifactPaths.ServiceStateDirectoryPath);
        EnsureDirectory(cycle.ArtifactPaths.CycleLogsDirectoryPath);

        WriteJsonAtomically(cycle.ArtifactPaths.CycleLogPath, cycle);

        var existingHistory = TryReadJson<MarketRefreshServiceCycleHistoryView>(cycle.ArtifactPaths.CycleHistoryPath);
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

        var history = new MarketRefreshServiceCycleHistoryView
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
        var metrics = BuildMetrics(now, options, history, runtimeHealth);

        WriteJsonAtomically(cycle.ArtifactPaths.CycleHistoryPath, history);
        WriteJsonAtomically(cycle.ArtifactPaths.SummaryPath, summary);
        WriteJsonAtomically(cycle.ArtifactPaths.HealthPath, health);
        WriteJsonAtomically(cycle.ArtifactPaths.MetricsPath, metrics);
        WriteJsonAtomically(cycle.ArtifactPaths.AlertsPath, alerts);

        return new MarketRefreshServiceOpsWriteResult(
            history.RetainedCycleCount,
            history.PrunedCycleLogCount,
            prunedCycleLogPaths);
    }

    private static List<MarketRefreshServiceCycleHistoryEntryView> BuildRetainedEntries(
        MarketRefreshServiceCycleView cycle,
        MarketRefreshServiceCycleHistoryView? existingHistory)
    {
        var latestEntry = BuildEntry(cycle);
        var retainedEntries = existingHistory?.RecentCycles
            .Where(entry => !string.Equals(entry.CycleId, cycle.CycleId, StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? new List<MarketRefreshServiceCycleHistoryEntryView>();

        retainedEntries.Insert(0, latestEntry);
        retainedEntries.Sort(static (left, right) => right.CompletedAtUtc.CompareTo(left.CompletedAtUtc));

        if (retainedEntries.Count > cycle.RetentionPolicy.CycleHistoryLimit)
        {
            retainedEntries.RemoveRange(cycle.RetentionPolicy.CycleHistoryLimit, retainedEntries.Count - cycle.RetentionPolicy.CycleHistoryLimit);
        }

        return retainedEntries;
    }

    private static MarketRefreshServiceCycleHistoryEntryView BuildEntry(MarketRefreshServiceCycleView cycle)
    {
        return new MarketRefreshServiceCycleHistoryEntryView
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
            OverallHealthStatusAfterCycle = cycle.OverallHealthStatusAfterCycle,
            CycleLogPath = cycle.ArtifactPaths.CycleLogPath,
            Operations = cycle.Operations
                .Select(operation => new MarketRefreshServiceCycleHistoryOperationView
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

    private static MarketRefreshServiceSummaryView BuildSummary(
        DateTimeOffset generatedAtUtc,
        MarketRefreshServiceCycleView cycle,
        MarketRefreshServiceCycleHistoryView history,
        MarketRefreshHealthView? runtimeHealth,
        MarketRefreshServiceAlertView alerts)
    {
        var notes = new List<string>();
        if (history.PrunedCycleLogCount > 0)
        {
            notes.Add($"Cycle retention pruned {history.PrunedCycleLogCount} old cycle log file(s).");
        }

        if (runtimeHealth is not null && runtimeHealth.Notes.Count > 0)
        {
            notes.AddRange(runtimeHealth.Notes);
        }

        return new MarketRefreshServiceSummaryView
        {
            ServiceName = cycle.ServiceName,
            GeneratedAtUtc = generatedAtUtc,
            HostInstanceId = cycle.HostInstanceId,
            LatestCycleId = cycle.CycleId,
            LatestCycleStatus = cycle.Status,
            LatestCycleCompletedAtUtc = cycle.CompletedAtUtc,
            OverallHealthStatus = DetermineServiceOverallStatus(cycle.Status, runtimeHealth?.OverallStatus, alerts.AlertStatus, history.RecentCycles),
            AlertStatus = alerts.AlertStatus,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.OverallHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            DueOperationCount = cycle.Operations.Count(operation => operation.IsDue),
            ExecutedRunCount = cycle.ExecutedRuns.Count,
            ErrorCount = cycle.Errors.Count,
            ArtifactPaths = cycle.ArtifactPaths,
            RetentionPolicy = cycle.RetentionPolicy,
            RetainedCycleCount = history.RetainedCycleCount,
            PrunedCycleLogCount = history.PrunedCycleLogCount,
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static MarketRefreshServiceHealthView BuildHealth(
        DateTimeOffset evaluatedAtUtc,
        MarketRefreshServiceCycleView cycle,
        MarketRefreshServiceCycleHistoryView history,
        MarketRefreshHealthView? runtimeHealth,
        MarketRefreshServiceAlertView alerts)
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
            notes.AddRange(runtimeHealth.Notes);
        }

        return new MarketRefreshServiceHealthView
        {
            ServiceName = cycle.ServiceName,
            EvaluatedAtUtc = evaluatedAtUtc,
            OverallStatus = DetermineServiceOverallStatus(cycle.Status, runtimeHealth?.OverallStatus, alerts.AlertStatus, history.RecentCycles),
            AlertStatus = alerts.AlertStatus,
            LatestCycleStatus = cycle.Status,
            LatestCycleCompletedAtUtc = cycle.CompletedAtUtc,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.OverallHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
            DueOperationCount = cycle.Operations.Count(operation => operation.IsDue),
            ExecutedRunCount = cycle.ExecutedRuns.Count,
            ErrorCount = cycle.Errors.Count,
            ArtifactPaths = cycle.ArtifactPaths,
            RetentionPolicy = cycle.RetentionPolicy,
            RetainedCycleCount = history.RetainedCycleCount,
            RecentCycles = history.RecentCycles,
            RuntimeOperations = runtimeHealth?.Operations ?? Array.Empty<MarketRefreshOperationHealthView>(),
            Notes = notes.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static MarketRefreshServiceAlertView BuildAlerts(
        DateTimeOffset evaluatedAtUtc,
        MarketRefreshServiceOptions options,
        MarketRefreshServiceCycleView cycle,
        MarketRefreshServiceCycleHistoryView history,
        MarketRefreshHealthView? runtimeHealth)
    {
        var alerts = new List<MarketRefreshServiceAlertItemView>();
        var thresholds = new MarketRefreshServiceAlertThresholdsView
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
            alerts.Add(new MarketRefreshServiceAlertItemView
            {
                Severity = "critical",
                Code = "latest_cycle_failed",
                Summary = "Latest market refresh service cycle failed before any due operation completed successfully.",
                Source = "service_cycle",
                RecommendedAction = "Inspect the cycle log and recent runtime failures before restarting the host.",
                RelatedPath = cycle.ArtifactPaths.CycleLogPath
            });
        }
        else if (string.Equals(cycle.Status, "completed_with_failures", StringComparison.OrdinalIgnoreCase))
        {
            alerts.Add(new MarketRefreshServiceAlertItemView
            {
                Severity = "warning",
                Code = "latest_cycle_completed_with_failures",
                Summary = "Latest market refresh service cycle completed with one or more failed refresh runs.",
                Source = "service_cycle",
                RecommendedAction = "Inspect recent dead letters and the latest cycle log before the next dispatch window.",
                RelatedPath = cycle.ArtifactPaths.CycleLogPath
            });
        }

        if (consecutiveImpairedCycleCount >= thresholds.ConsecutiveImpairedCycleAlertThreshold)
        {
            alerts.Add(new MarketRefreshServiceAlertItemView
            {
                Severity = "warning",
                Code = "consecutive_impaired_cycles",
                Summary = $"Service recorded {consecutiveImpairedCycleCount} consecutive impaired cycle(s).",
                Source = "service_history",
                RecommendedAction = "Treat the host as degraded and inspect operator health plus recent cycle history.",
                RelatedPath = cycle.ArtifactPaths.CycleHistoryPath
            });
        }

        if (consecutiveBlockedLeaseCycleCount >= thresholds.ConsecutiveBlockedLeaseAlertThreshold)
        {
            alerts.Add(new MarketRefreshServiceAlertItemView
            {
                Severity = "critical",
                Code = "blocked_by_active_lease_window",
                Summary = $"Service has been blocked by an active lease for {consecutiveBlockedLeaseCycleCount} consecutive cycle(s).",
                Source = "lease",
                RecommendedAction = "Confirm no valid worker is still running, then use the formal CLI recover-lease entrypoint.",
                RelatedPath = cycle.ArtifactPaths.HealthPath
            });
        }

        if (runtimeHealth is not null)
        {
            if (string.Equals(runtimeHealth.OverallStatus, "unhealthy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtimeHealth.OverallStatus, "error", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new MarketRefreshServiceAlertItemView
                {
                    Severity = "critical",
                    Code = "runtime_health_unhealthy",
                    Summary = "Runtime health is reporting an unhealthy state.",
                    Source = "runtime_health",
                    RecommendedAction = "Inspect runtime health, dead letters, and recent failures before allowing another unattended cycle.",
                    RelatedPath = cycle.ArtifactPaths.HealthPath
                });
            }
            else if (string.Equals(runtimeHealth.OverallStatus, "degraded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtimeHealth.OverallStatus, "warning", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new MarketRefreshServiceAlertItemView
                {
                    Severity = "warning",
                    Code = "runtime_health_degraded",
                    Summary = "Runtime health is reporting a degraded state.",
                    Source = "runtime_health",
                    RecommendedAction = "Inspect degraded operations and verify lag is still within the accepted operator window.",
                    RelatedPath = cycle.ArtifactPaths.HealthPath
                });
            }

            foreach (var operation in runtimeHealth.Operations.OrderBy(entry => entry.Operation, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(operation.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketRefreshServiceAlertItemView
                    {
                        Severity = "critical",
                        Code = "operation_unhealthy",
                        Summary = BuildOperationAlertSummary(operation),
                        Source = "runtime_operation",
                        Operation = operation.Operation,
                        RecommendedAction = "Inspect the failing operation, its dead letter, and the source payload before the next unattended cycle.",
                        RelatedPath = operation.LastDeadLetterPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }
                else if (string.Equals(operation.Status, "degraded", StringComparison.OrdinalIgnoreCase))
                {
                    alerts.Add(new MarketRefreshServiceAlertItemView
                    {
                        Severity = "warning",
                        Code = "operation_degraded",
                        Summary = BuildOperationAlertSummary(operation),
                        Source = "runtime_operation",
                        Operation = operation.Operation,
                        RecommendedAction = "Inspect lag, retry state, and dead-letter history for the degraded operation.",
                        RelatedPath = operation.LastDeadLetterPath ?? cycle.ArtifactPaths.HealthPath
                    });
                }
            }
        }

        var alertStatus = alerts.Any(alert => string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase))
            ? "critical"
            : alerts.Count > 0
                ? "warning"
                : "clear";

        return new MarketRefreshServiceAlertView
        {
            ServiceName = options.ServiceName,
            EvaluatedAtUtc = evaluatedAtUtc,
            AlertStatus = alertStatus,
            LatestCycleStatus = cycle.Status,
            RuntimeHealthStatus = runtimeHealth?.OverallStatus ?? cycle.OverallHealthStatusAfterCycle,
            LeaseStatus = runtimeHealth?.Lease.Status ?? cycle.LeaseStatusAfterCycle,
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

    private static MarketRefreshServiceMetricsView BuildMetrics(
        DateTimeOffset generatedAtUtc,
        MarketRefreshServiceOptions options,
        MarketRefreshServiceCycleHistoryView history,
        MarketRefreshHealthView? runtimeHealth)
    {
        var operations = options.Operations
            .Select(option =>
            {
                var operationName = MarketRefreshOperationKind.Parse(option.Operation).OperationName;
                var runtimeOperation = runtimeHealth?.Operations
                    .FirstOrDefault(operation => string.Equals(operation.Operation, operationName, StringComparison.OrdinalIgnoreCase));
                var historyOperations = history.RecentCycles
                    .SelectMany(cycle => cycle.Operations.Select(operation => (Cycle: cycle, Operation: operation)))
                    .Where(entry => string.Equals(entry.Operation.Operation, operationName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var latestHistoryOperation = historyOperations
                    .OrderByDescending(entry => entry.Cycle.CompletedAtUtc)
                    .Select(entry => entry.Operation)
                    .FirstOrDefault();

                return new MarketRefreshServiceOperationMetricsView
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
                    CurrentLagMinutes = runtimeOperation?.LagMinutes,
                    LastSucceededAtUtc = runtimeOperation?.LastSucceededAtUtc,
                    NextDueAtUtc = latestHistoryOperation?.NextDueAtUtc
                };
            })
            .OrderBy(operation => operation.Operation, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MarketRefreshServiceMetricsView
        {
            ServiceName = options.ServiceName,
            GeneratedAtUtc = generatedAtUtc,
            RetentionPolicy = new MarketRefreshServiceRetentionPolicyView
            {
                CycleHistoryLimit = options.CycleHistoryLimit
            },
            RetainedCycleCount = history.RetainedCycleCount,
            TotalCyclesRetained = history.RecentCycles.Count,
            CompletedCyclesRetained = history.RecentCycles.Count(cycle => string.Equals(cycle.Status, "completed", StringComparison.OrdinalIgnoreCase)),
            CompletedWithFailuresCyclesRetained = history.RecentCycles.Count(cycle => string.Equals(cycle.Status, "completed_with_failures", StringComparison.OrdinalIgnoreCase)),
            IdleCyclesRetained = history.RecentCycles.Count(cycle => string.Equals(cycle.Status, "idle", StringComparison.OrdinalIgnoreCase)),
            BlockedCyclesRetained = history.RecentCycles.Count(cycle => string.Equals(cycle.Status, "blocked_by_active_lease", StringComparison.OrdinalIgnoreCase)),
            FailedCyclesRetained = history.RecentCycles.Count(cycle => string.Equals(cycle.Status, "failed", StringComparison.OrdinalIgnoreCase)),
            TotalDueOperationsRetained = history.RecentCycles.Sum(cycle => cycle.DueOperationCount),
            TotalExecutedRunsRetained = history.RecentCycles.Sum(cycle => cycle.ExecutedRunCount),
            CurrentRuntimeHealthStatus = runtimeHealth?.OverallStatus ?? "unknown",
            CurrentLeaseStatus = runtimeHealth?.Lease.Status ?? "unknown",
            Operations = operations
        };
    }

    private static string DetermineServiceOverallStatus(
        string latestCycleStatus,
        string? runtimeHealthStatus,
        string alertStatus,
        IReadOnlyList<MarketRefreshServiceCycleHistoryEntryView> recentCycles)
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
            || string.Equals(runtimeHealthStatus, "degraded", StringComparison.OrdinalIgnoreCase)
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
        IReadOnlyList<MarketRefreshServiceCycleHistoryEntryView> recentCycles,
        Func<MarketRefreshServiceCycleHistoryEntryView, bool> predicate)
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

    private static string BuildOperationAlertSummary(MarketRefreshOperationHealthView operation)
    {
        if (operation.ConsecutiveFailures > 0)
        {
            return $"{operation.Operation} is {operation.Status} with {operation.ConsecutiveFailures} consecutive failure(s) and lag={FormatLagMinutes(operation.LagMinutes)}.";
        }

        return $"{operation.Operation} is {operation.Status} with lag={FormatLagMinutes(operation.LagMinutes)}.";
    }

    private static string FormatLagMinutes(long? lagMinutes)
    {
        return lagMinutes.HasValue ? $"{lagMinutes.Value}m" : "unknown";
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

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
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

public sealed record MarketRefreshServiceOpsWriteResult(
    int RetainedCycleCount,
    int PrunedCycleLogCount,
    IReadOnlyList<string> PrunedCycleLogPaths);
