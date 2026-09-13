namespace EdenOS.Application.Market;

public sealed record EsiMarketHistoryIngressOptions
{
    public string ServiceName { get; init; } = "market-esi-history-ingress-service";

    public required string OutputDirectoryPath { get; init; }

    public string? ServiceStateDirectoryPath { get; init; }

    public string RequestedBy { get; init; } = "service.market_esi_history_ingress";

    public string TriggerKind { get; init; } = "service_worker";

    public int PollIntervalSeconds { get; init; } = 30;

    public bool RunDueOnStartup { get; init; } = true;

    public int? MaxCycles { get; init; }

    public int CycleHistoryLimit { get; init; } = 40;

    public string? HeartbeatPath { get; init; }

    public string BaseUrl { get; init; } = "https://esi.evetech.net/latest";

    public string Datasource { get; init; } = "tranquility";

    public string UserAgent { get; init; } = "EdenOS Rewrite market-esi-history-ingress-service";

    public string? CompatibilityDate { get; init; }

    public int MaxConcurrentRequests { get; init; } = 6;

    public int MaxRetriesPerRequest { get; init; } = 3;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int ErrorLimitPauseThreshold { get; init; } = 2;

    public int RateLimitPauseThreshold { get; init; } = 1;

    public int RetryBaseDelayMilliseconds { get; init; } = 750;

    public IReadOnlyList<EsiMarketHistoryIngressScopeOptions> Scopes { get; init; } = Array.Empty<EsiMarketHistoryIngressScopeOptions>();
}

public sealed record EsiMarketHistoryIngressScopeOptions
{
    public string ScopeName { get; init; } = string.Empty;

    public long RegionId { get; init; }

    public bool Enabled { get; init; } = true;

    public string? PayloadPath { get; init; }

    public int MaxTypesPerCycle { get; init; } = 100;

    public int MinimumHistoryDaysForReady { get; init; } = 30;

    public int SuccessRefreshIntervalHours { get; init; } = 24;

    public bool EnableRecentActivityPrioritization { get; init; }

    public int RecentActivityLookbackDays { get; init; } = 14;

    public int DormantRefreshIntervalHours { get; init; } = 168;

    public int PartialCoverageRecheckHours { get; init; } = 24;

    public int NotFoundRecheckHours { get; init; } = 72;

    public int FailureBackoffSeconds { get; init; } = 900;

    public IReadOnlyList<long> IncludeTypeIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> ExcludeTypeIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<EsiMarketHistoryIngressSourceOverrideOptions> HistorySourceOverrides { get; init; } = Array.Empty<EsiMarketHistoryIngressSourceOverrideOptions>();
}

public sealed record EsiMarketHistoryIngressSourceOverrideOptions
{
    public long TypeId { get; init; }

    public IReadOnlyList<EsiMarketHistoryIngressSourceSegmentOptions> Segments { get; init; } = Array.Empty<EsiMarketHistoryIngressSourceSegmentOptions>();
}

public sealed record EsiMarketHistoryIngressSourceSegmentOptions
{
    public long SourceRegionId { get; init; }

    public string MarketScope { get; init; } = "regional";

    public string? StartDateInclusive { get; init; }

    public string? EndDateExclusive { get; init; }
}

public sealed record EsiMarketHistoryIngressArtifactPathsView
{
    public required string OutputDirectoryPath { get; init; }

    public required string ServiceStateDirectoryPath { get; init; }

    public required string HeartbeatPath { get; init; }

    public required string CycleLogsDirectoryPath { get; init; }

    public required string CycleLogPath { get; init; }

    public required string StatePath { get; init; }
}

public sealed record EsiMarketHistoryIngressPlannedScopeView
{
    public required string ScopeName { get; init; }

    public long RegionId { get; init; }

    public bool Enabled { get; init; }

    public bool IsDue { get; init; }

    public int PlannedTargetCount { get; init; }

    public int DueTargetCount { get; init; }

    public int MaxTypesPerCycle { get; init; }

    public required string PayloadPath { get; init; }

    public required string Decision { get; init; }

    public string? LastStatus { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }
}

public sealed record EsiMarketHistoryIngressRateLimitView
{
    public int? ErrorLimitRemain { get; init; }

    public int? ErrorLimitResetSeconds { get; init; }

    public int? RateLimitRemain { get; init; }

    public int? RateLimitResetSeconds { get; init; }

    public string? RateLimitGroup { get; init; }

    public DateTimeOffset? PauseUntilUtc { get; init; }
}

public sealed record EsiMarketHistoryIngressScopeRunView
{
    public required string ScopeName { get; init; }

    public long RegionId { get; init; }

    public required string PayloadPath { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int PlannedTargetCount { get; init; }

    public int DueTargetCount { get; init; }

    public int ExecutedTargetCount { get; init; }

    public int SucceededTargetCount { get; init; }

    public int NotFoundTargetCount { get; init; }

    public int FailedTargetCount { get; init; }

    public int SeriesCount { get; init; }

    public int PointCount { get; init; }

    public int RequestCount { get; init; }

    public int RetryCount { get; init; }

    public EsiMarketHistoryIngressRateLimitView? RateLimit { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiMarketHistoryIngressCycleView
{
    public required string ServiceName { get; init; }

    public required string HostKind { get; init; }

    public required string WorkerKind { get; init; }

    public required string HostInstanceId { get; init; }

    public int CycleNumber { get; init; }

    public required string CycleId { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string HeartbeatPath { get; init; }

    public required EsiMarketHistoryIngressArtifactPathsView ArtifactPaths { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset PlannedAtUtc { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<EsiMarketHistoryIngressPlannedScopeView> Scopes { get; init; } = Array.Empty<EsiMarketHistoryIngressPlannedScopeView>();

    public IReadOnlyList<EsiMarketHistoryIngressScopeRunView> ExecutedScopes { get; init; } = Array.Empty<EsiMarketHistoryIngressScopeRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
