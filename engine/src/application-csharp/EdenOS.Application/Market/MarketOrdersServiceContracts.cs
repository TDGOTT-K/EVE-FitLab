namespace EdenOS.Application.Market;

public sealed record MarketOrdersServiceOptions
{
    public string ServiceName { get; init; } = "market-orders-service";

    public required string MarketFactsDirectoryPath { get; init; }

    public string? ServiceStateDirectoryPath { get; init; }

    public string RequestedBy { get; init; } = "service.market_orders";

    public string TriggerKind { get; init; } = "service_worker";

    public int PollIntervalSeconds { get; init; } = 30;

    public bool RunDueOnStartup { get; init; } = true;

    public bool ProbeLease { get; init; } = true;

    public int WarningAgeMinutes { get; init; } = 240;

    public int ErrorAgeMinutes { get; init; } = 1440;

    public int? MaxCycles { get; init; }

    public int CycleHistoryLimit { get; init; } = 40;

    public int ConsecutiveImpairedCycleAlertThreshold { get; init; } = 2;

    public int ConsecutiveBlockedLeaseAlertThreshold { get; init; } = 3;

    public string? HeartbeatPath { get; init; }

    public IReadOnlyList<MarketOrdersServiceOperationOptions> Operations { get; init; } = Array.Empty<MarketOrdersServiceOperationOptions>();
}

public sealed record MarketOrdersServiceOperationOptions
{
    public string? Operation { get; init; }

    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 15;

    public string? PayloadPath { get; init; }

    public string? CursorPrefix { get; init; }
}

public sealed record MarketOrdersServiceCyclePlan
{
    public required string ServiceName { get; init; }

    public required string MarketFactsDirectoryPath { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string HeartbeatPath { get; init; }

    public string LeaseStatusBeforeDispatch { get; init; } = "unknown";

    public DateTimeOffset PlannedAtUtc { get; init; }

    public required string CycleId { get; init; }

    public IReadOnlyList<MarketOrdersServicePlannedOperationView> Operations { get; init; } = Array.Empty<MarketOrdersServicePlannedOperationView>();
}

public sealed record MarketOrdersServicePlannedOperationView
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

public sealed record MarketOrdersServiceDispatchView
{
    public required string Status { get; init; }

    public IReadOnlyList<MarketOrdersRuntimeRunView> ExecutedRuns { get; init; } = Array.Empty<MarketOrdersRuntimeRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersServiceCycleView
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

    public required MarketOrdersServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketOrdersServiceRetentionPolicyView RetentionPolicy { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset PlannedAtUtc { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; }

    public string LeaseStatusBeforeDispatch { get; init; } = "unknown";

    public string LeaseStatusAfterCycle { get; init; } = "unknown";

    public string RuntimeHealthStatusAfterCycle { get; init; } = "unknown";

    public MarketOrdersRuntimeRetentionPolicyView? RuntimeRetentionPolicy { get; init; }

    public MarketOrdersRuntimeStoreSummaryView? CurrentStoreSummary { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<MarketOrdersServicePlannedOperationView> Operations { get; init; } = Array.Empty<MarketOrdersServicePlannedOperationView>();

    public IReadOnlyList<MarketOrdersRuntimeRunView> ExecutedRuns { get; init; } = Array.Empty<MarketOrdersRuntimeRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersServiceArtifactPathsView
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

public sealed record MarketOrdersServiceRetentionPolicyView
{
    public int CycleHistoryLimit { get; init; }
}

public sealed record MarketOrdersServiceCycleHistoryOperationView
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

public sealed record MarketOrdersServiceCycleHistoryEntryView
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

    public required string RuntimeHealthStatusAfterCycle { get; init; }

    public required string CycleLogPath { get; init; }

    public IReadOnlyList<MarketOrdersServiceCycleHistoryOperationView> Operations { get; init; } = Array.Empty<MarketOrdersServiceCycleHistoryOperationView>();
}

public sealed record MarketOrdersServiceCycleHistoryView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required MarketOrdersServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketOrdersServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<MarketOrdersServiceCycleHistoryEntryView> RecentCycles { get; init; } = Array.Empty<MarketOrdersServiceCycleHistoryEntryView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersServiceSummaryView
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

    public required string IngressBoundaryStatus { get; init; }

    public int DueOperationCount { get; init; }

    public int ExecutedRunCount { get; init; }

    public int ErrorCount { get; init; }

    public required MarketOrdersServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketOrdersServiceRetentionPolicyView RetentionPolicy { get; init; }

    public MarketOrdersRuntimeRetentionPolicyView? RuntimeRetentionPolicy { get; init; }

    public MarketOrdersRuntimeStoreSummaryView? CurrentStoreSummary { get; init; }

    public int RuntimeRecentRunCount { get; init; }

    public int RuntimeRecentSnapshotArtifactCount { get; init; }

    public int RuntimeRecentLeaseRecoveryCount { get; init; }

    public int StalePayloadOperationCount { get; init; }

    public int MissingPayloadOperationCount { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersServiceMetricsView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public required MarketOrdersServiceRetentionPolicyView RetentionPolicy { get; init; }

    public MarketOrdersRuntimeRetentionPolicyView? RuntimeRetentionPolicy { get; init; }

    public MarketOrdersRuntimeStoreSummaryView? CurrentStoreSummary { get; init; }

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

    public required string CurrentIngressBoundaryStatus { get; init; }

    public int RuntimeRecentRunCount { get; init; }

    public int RuntimeRecentSnapshotArtifactCount { get; init; }

    public int RuntimeRecentLeaseRecoveryCount { get; init; }

    public int IngressBoundaryWarningOperationCount { get; init; }

    public int IngressBoundaryUnhealthyOperationCount { get; init; }

    public int StalePayloadOperationCount { get; init; }

    public IReadOnlyList<MarketOrdersServiceOperationMetricsView> Operations { get; init; } = Array.Empty<MarketOrdersServiceOperationMetricsView>();
}

public sealed record MarketOrdersServiceOperationMetricsView
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

    public string? IngressBoundaryStatus { get; init; }

    public long? LatestSnapshotAgeMinutes { get; init; }

    public bool PayloadExists { get; init; }

    public long? PayloadAgeMinutes { get; init; }

    public bool? PayloadAdvancedSinceLastSuccess { get; init; }

    public int ConsecutiveFailures { get; init; }

    public int CurrentStaleScopeCount { get; init; }

    public int CurrentMaxRetainedLagMinutes { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }
}

public sealed record MarketOrdersServiceHealthView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string OverallStatus { get; init; }

    public required string AlertStatus { get; init; }

    public required string LatestCycleStatus { get; init; }

    public DateTimeOffset LatestCycleCompletedAtUtc { get; init; }

    public required string RuntimeHealthStatus { get; init; }

    public required string LeaseStatus { get; init; }

    public required string IngressBoundaryStatus { get; init; }

    public int DueOperationCount { get; init; }

    public int ExecutedRunCount { get; init; }

    public int ErrorCount { get; init; }

    public required MarketOrdersServiceArtifactPathsView ArtifactPaths { get; init; }

    public required MarketOrdersServiceRetentionPolicyView RetentionPolicy { get; init; }

    public int RetainedCycleCount { get; init; }

    public MarketOrdersRuntimeHealthView? RuntimeHealth { get; init; }

    public IReadOnlyList<MarketOrdersServiceCycleHistoryEntryView> RecentCycles { get; init; } = Array.Empty<MarketOrdersServiceCycleHistoryEntryView>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record MarketOrdersServiceAlertThresholdsView
{
    public int ConsecutiveImpairedCycleAlertThreshold { get; init; }

    public int ConsecutiveBlockedLeaseAlertThreshold { get; init; }
}

public sealed record MarketOrdersServiceAlertItemView
{
    public required string Severity { get; init; }

    public required string Code { get; init; }

    public required string Summary { get; init; }

    public required string Source { get; init; }

    public string? Operation { get; init; }

    public string? RecommendedAction { get; init; }

    public string? RelatedPath { get; init; }
}

public sealed record MarketOrdersServiceAlertView
{
    public required string ServiceName { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public required string AlertStatus { get; init; }

    public required string LatestCycleStatus { get; init; }

    public required string RuntimeHealthStatus { get; init; }

    public required string LeaseStatus { get; init; }

    public required string IngressBoundaryStatus { get; init; }

    public int ConsecutiveImpairedCycleCount { get; init; }

    public int ConsecutiveBlockedLeaseCycleCount { get; init; }

    public required MarketOrdersServiceAlertThresholdsView Thresholds { get; init; }

    public required MarketOrdersServiceArtifactPathsView ArtifactPaths { get; init; }

    public IReadOnlyList<MarketOrdersServiceAlertItemView> Alerts { get; init; } = Array.Empty<MarketOrdersServiceAlertItemView>();

    public IReadOnlyList<string> RecommendedActions { get; init; } = Array.Empty<string>();
}

internal static class MarketOrdersServiceConventions
{
    public const string HostKind = "market_orders_service_host";
    public const string SchedulerKind = "interval_scheduler";
    public const string WorkerKind = "market_orders_runtime_worker";
    public const string OrdersSnapshotOperation = "orders-snapshot";
}
