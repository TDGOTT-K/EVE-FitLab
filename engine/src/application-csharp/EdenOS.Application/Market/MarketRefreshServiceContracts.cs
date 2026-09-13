namespace EdenOS.Application.Market;

public sealed record MarketRefreshServiceOptions
{
    public string ServiceName { get; init; } = "market-refresh-service";

    public required string MarketFactsDirectoryPath { get; init; }

    public string? ServiceStateDirectoryPath { get; init; }

    public string RequestedBy { get; init; } = "service.market_refresh";

    public string TriggerKind { get; init; } = "service_worker";

    public int PollIntervalSeconds { get; init; } = 30;

    public bool ContinueOnError { get; init; }

    public bool RunDueOnStartup { get; init; } = true;

    public bool ProbeLease { get; init; } = true;

    public int WarningLagMinutes { get; init; } = 360;

    public int ErrorLagMinutes { get; init; } = 1440;

    public int? MaxCycles { get; init; }

    public int CycleHistoryLimit { get; init; } = 40;

    public int ConsecutiveImpairedCycleAlertThreshold { get; init; } = 2;

    public int ConsecutiveBlockedLeaseAlertThreshold { get; init; } = 3;

    public string? HeartbeatPath { get; init; }

    public IReadOnlyList<MarketRefreshServiceOperationOptions> Operations { get; init; } = Array.Empty<MarketRefreshServiceOperationOptions>();
}

public sealed record MarketRefreshServiceOperationOptions
{
    public string? Operation { get; init; }

    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 60;

    public string? PayloadPath { get; init; }

    public string? CursorPrefix { get; init; }
}

public sealed record MarketRefreshServiceCyclePlan
{
    public required string ServiceName { get; init; }

    public required string MarketFactsDirectoryPath { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string HeartbeatPath { get; init; }

    public string LeaseStatusBeforeDispatch { get; init; } = "unknown";

    public DateTimeOffset PlannedAtUtc { get; init; }

    public required string CycleId { get; init; }

    public IReadOnlyList<MarketRefreshServicePlannedOperationView> Operations { get; init; } = Array.Empty<MarketRefreshServicePlannedOperationView>();
}

public sealed record MarketRefreshServicePlannedOperationView
{
    public required string Operation { get; init; }

    public bool Enabled { get; init; }

    public bool IsDue { get; init; }

    public int IntervalMinutes { get; init; }

    public string? PayloadPath { get; init; }

    public string? GeneratedCursor { get; init; }

    public required string Decision { get; init; }

    public string? LastStatus { get; init; }

    public DateTimeOffset? LastStartedAtUtc { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }
}

public sealed record MarketRefreshServiceDispatchView
{
    public required string Status { get; init; }

    public MarketRefreshBatchView? Batch { get; init; }

    public IReadOnlyList<MarketRefreshRunView> ExecutedRuns { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshServiceCycleView
{
    public required string ServiceName { get; init; }

    public required string HostKind { get; init; }

    public required string SchedulerKind { get; init; }

    public required string WorkerKind { get; init; }

    public required string HostInstanceId { get; init; }

    public int CycleNumber { get; init; }

    public required string CycleId { get; init; }

    public required string MarketFactsDirectoryPath { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string HeartbeatPath { get; init; }

    public required MarketRefreshServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketRefreshServiceRetentionPolicyView RetentionPolicy { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset PlannedAtUtc { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; }

    public string LeaseStatusBeforeDispatch { get; init; } = "unknown";

    public string LeaseStatusAfterCycle { get; init; } = "unknown";

    public string OverallHealthStatusAfterCycle { get; init; } = "unknown";

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<MarketRefreshServicePlannedOperationView> Operations { get; init; } = Array.Empty<MarketRefreshServicePlannedOperationView>();

    public MarketRefreshBatchView? Batch { get; init; }

    public IReadOnlyList<MarketRefreshRunView> ExecutedRuns { get; init; } = Array.Empty<MarketRefreshRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshServiceArtifactPathsView
{
    public required string ServiceStateDirectoryPath { get; init; }

    public required string HeartbeatPath { get; init; }

    public required string CycleLogsDirectoryPath { get; init; }

    public required string CycleLogPath { get; init; }

    public required string CycleHistoryPath { get; init; }

    public required string SummaryPath { get; init; }

    public required string HealthPath { get; init; }

    public required string MetricsPath { get; init; }

    public required string AlertsPath { get; init; }
}

public sealed record MarketRefreshServiceRetentionPolicyView
{
    public int CycleHistoryLimit { get; init; }
}

public sealed record MarketRefreshServiceCycleHistoryOperationView
{
    public required string Operation { get; init; }

    public bool Enabled { get; init; }

    public bool IsDue { get; init; }

    public int IntervalMinutes { get; init; }

    public required string Decision { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }

    public string? LastRuntimeStatus { get; init; }

    public int ExecutedRunCount { get; init; }

    public int FailedRunCount { get; init; }
}

public sealed record MarketRefreshServiceCycleHistoryEntryView
{
    public required string CycleId { get; init; }

    public int CycleNumber { get; init; }

    public required string HostInstanceId { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset PlannedAtUtc { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int DueOperationCount { get; init; }

    public int ExecutedRunCount { get; init; }

    public int ErrorCount { get; init; }

    public required string LeaseStatusAfterCycle { get; init; }

    public required string OverallHealthStatusAfterCycle { get; init; }

    public required string CycleLogPath { get; init; }

    public IReadOnlyList<MarketRefreshServiceCycleHistoryOperationView> Operations { get; init; } = Array.Empty<MarketRefreshServiceCycleHistoryOperationView>();
}

public sealed record MarketRefreshServiceCycleHistoryView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required MarketRefreshServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketRefreshServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<MarketRefreshServiceCycleHistoryEntryView> RecentCycles { get; init; } = Array.Empty<MarketRefreshServiceCycleHistoryEntryView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshServiceSummaryView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required string HostInstanceId { get; init; }

    public required string LatestCycleId { get; init; }

    public required string LatestCycleStatus { get; init; }

    public DateTimeOffset LatestCycleCompletedAtUtc { get; init; }

    public required string OverallHealthStatus { get; init; }

    public required string AlertStatus { get; init; }

    public required string RuntimeHealthStatus { get; init; }

    public required string LeaseStatus { get; init; }

    public int DueOperationCount { get; init; }

    public int ExecutedRunCount { get; init; }

    public int ErrorCount { get; init; }

    public required MarketRefreshServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketRefreshServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshServiceMetricsView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required MarketRefreshServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public int TotalCyclesRetained { get; init; }

    public int CompletedCyclesRetained { get; init; }

    public int CompletedWithFailuresCyclesRetained { get; init; }

    public int IdleCyclesRetained { get; init; }

    public int BlockedCyclesRetained { get; init; }

    public int FailedCyclesRetained { get; init; }

    public int TotalDueOperationsRetained { get; init; }

    public int TotalExecutedRunsRetained { get; init; }

    public required string CurrentRuntimeHealthStatus { get; init; }

    public required string CurrentLeaseStatus { get; init; }

    public IReadOnlyList<MarketRefreshServiceOperationMetricsView> Operations { get; init; } = Array.Empty<MarketRefreshServiceOperationMetricsView>();
}

public sealed record MarketRefreshServiceOperationMetricsView
{
    public required string Operation { get; init; }

    public bool Enabled { get; init; }

    public int IntervalMinutes { get; init; }

    public int DueCyclesRetained { get; init; }

    public int ExecutedRunsRetained { get; init; }

    public int FailedRunsRetained { get; init; }

    public string? LastDecision { get; init; }

    public string? LastCycleStatus { get; init; }

    public string? CurrentRuntimeStatus { get; init; }

    public long? CurrentLagMinutes { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }
}

public sealed record MarketRefreshServiceHealthView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string OverallStatus { get; init; }

    public required string AlertStatus { get; init; }

    public required string LatestCycleStatus { get; init; }

    public DateTimeOffset LatestCycleCompletedAtUtc { get; init; }

    public required string RuntimeHealthStatus { get; init; }

    public required string LeaseStatus { get; init; }

    public int DueOperationCount { get; init; }

    public int ExecutedRunCount { get; init; }

    public int ErrorCount { get; init; }

    public required MarketRefreshServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketRefreshServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public IReadOnlyList<MarketRefreshServiceCycleHistoryEntryView> RecentCycles { get; init; } = Array.Empty<MarketRefreshServiceCycleHistoryEntryView>();

    public IReadOnlyList<MarketRefreshOperationHealthView> RuntimeOperations { get; init; } = Array.Empty<MarketRefreshOperationHealthView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketRefreshServiceAlertThresholdsView
{
    public int ConsecutiveImpairedCycleAlertThreshold { get; init; }

    public int ConsecutiveBlockedLeaseAlertThreshold { get; init; }
}

public sealed record MarketRefreshServiceAlertItemView
{
    public required string Severity { get; init; }

    public required string Code { get; init; }

    public required string Summary { get; init; }

    public required string Source { get; init; }

    public string? Operation { get; init; }

    public string? RecommendedAction { get; init; }

    public string? RelatedPath { get; init; }
}

public sealed record MarketRefreshServiceAlertView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string AlertStatus { get; init; }

    public required string LatestCycleStatus { get; init; }

    public required string RuntimeHealthStatus { get; init; }

    public required string LeaseStatus { get; init; }

    public int ConsecutiveImpairedCycleCount { get; init; }

    public int ConsecutiveBlockedLeaseCycleCount { get; init; }

    public required MarketRefreshServiceAlertThresholdsView Thresholds { get; init; }

    public required MarketRefreshServiceArtifactPathsView ArtifactPaths { get; init; }

    public IReadOnlyList<MarketRefreshServiceAlertItemView> Alerts { get; init; } = Array.Empty<MarketRefreshServiceAlertItemView>();

    public IReadOnlyList<string> RecommendedActions { get; init; } = Array.Empty<string>();
}

internal static class MarketRefreshServiceConventions
{
    public const string HostKind = "market_refresh_service_host";
    public const string SchedulerKind = "interval_scheduler";
    public const string WorkerKind = "market_refresh_runtime_batch_worker";
}
