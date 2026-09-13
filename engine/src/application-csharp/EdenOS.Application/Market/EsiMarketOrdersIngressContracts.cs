namespace EdenOS.Application.Market;

public sealed record EsiMarketOrdersIngressOptions
{
    public string ServiceName { get; init; } = "market-esi-ingress-service";

    public required string OutputDirectoryPath { get; init; }

    public string? ServiceStateDirectoryPath { get; init; }

    public string RequestedBy { get; init; } = "service.market_esi_ingress";

    public string TriggerKind { get; init; } = "service_worker";

    public int PollIntervalSeconds { get; init; } = 30;

    public bool RunDueOnStartup { get; init; } = true;

    public int? MaxCycles { get; init; }

    public int CycleHistoryLimit { get; init; } = 40;

    public string? HeartbeatPath { get; init; }

    public string BaseUrl { get; init; } = "https://esi.evetech.net/latest";

    public string Datasource { get; init; } = "tranquility";

    public string UserAgent { get; init; } = "EdenOS Rewrite market-esi-ingress-service";

    public string? CompatibilityDate { get; init; }

    public int MaxConcurrentRequests { get; init; } = 6;

    public int MaxRetriesPerRequest { get; init; } = 3;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int NearExpiryDelayThresholdSeconds { get; init; } = 5;

    public int ErrorLimitPauseThreshold { get; init; } = 2;

    public int RateLimitPauseThreshold { get; init; } = 1;

    public int RetryBaseDelayMilliseconds { get; init; } = 750;

    public IReadOnlyList<EsiMarketOrdersIngressScopeOptions> Scopes { get; init; } = Array.Empty<EsiMarketOrdersIngressScopeOptions>();
}

public sealed record EsiMarketOrdersIngressScopeOptions
{
    public string ScopeName { get; init; } = string.Empty;

    public long RegionId { get; init; }

    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 15;

    public string OrderType { get; init; } = "all";

    public string? PayloadPath { get; init; }

    public IReadOnlyList<EsiMarketOrdersIngressOrderSourceOverrideOptions> OrderSourceOverrides { get; init; } = Array.Empty<EsiMarketOrdersIngressOrderSourceOverrideOptions>();
}

public sealed record EsiMarketOrdersIngressOrderSourceOverrideOptions
{
    public long SourceRegionId { get; init; }

    public IReadOnlyList<long> TypeIds { get; init; } = Array.Empty<long>();
}

public sealed record EsiMarketOrdersIngressArtifactPathsView
{
    public required string OutputDirectoryPath { get; init; }

    public required string ServiceStateDirectoryPath { get; init; }

    public required string HeartbeatPath { get; init; }

    public required string CycleLogsDirectoryPath { get; init; }

    public required string CycleLogPath { get; init; }

    public required string StatePath { get; init; }
}

public sealed record EsiMarketOrdersIngressPlannedScopeView
{
    public required string ScopeName { get; init; }

    public long RegionId { get; init; }

    public required string OrderType { get; init; }

    public bool Enabled { get; init; }

    public bool IsDue { get; init; }

    public int IntervalMinutes { get; init; }

    public required string PayloadPath { get; init; }

    public required string Decision { get; init; }

    public string? LastStatus { get; init; }

    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    public DateTimeOffset? LastFailedAtUtc { get; init; }

    public DateTimeOffset? NextDueAtUtc { get; init; }
}

public sealed record EsiMarketOrdersIngressRateLimitView
{
    public int? ErrorLimitRemain { get; init; }

    public int? ErrorLimitResetSeconds { get; init; }

    public int? RateLimitRemain { get; init; }

    public int? RateLimitResetSeconds { get; init; }

    public string? RateLimitGroup { get; init; }

    public DateTimeOffset? PauseUntilUtc { get; init; }
}

public sealed record EsiMarketOrdersIngressScopeRunView
{
    public required string ScopeName { get; init; }

    public long RegionId { get; init; }

    public required string OrderType { get; init; }

    public required string PayloadPath { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public bool NotModified { get; init; }

    public bool ExistingPayloadPreserved { get; init; }

    public bool WaitedForFreshCache { get; init; }

    public int PageCount { get; init; }

    public int RequestCount { get; init; }

    public int OrderCount { get; init; }

    public int SnapshotCount { get; init; }

    public int SellOrderCount { get; init; }

    public int BuyOrderCount { get; init; }

    public int RetryCount { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? LastModifiedAtUtc { get; init; }

    public EsiMarketOrdersIngressRateLimitView? RateLimit { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiMarketOrdersIngressCycleView
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

    public required EsiMarketOrdersIngressArtifactPathsView ArtifactPaths { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset PlannedAtUtc { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; }

    public int RetainedCycleCount { get; init; }

    public int PrunedCycleLogCount { get; init; }

    public IReadOnlyList<EsiMarketOrdersIngressPlannedScopeView> Scopes { get; init; } = Array.Empty<EsiMarketOrdersIngressPlannedScopeView>();

    public IReadOnlyList<EsiMarketOrdersIngressScopeRunView> ExecutedScopes { get; init; } = Array.Empty<EsiMarketOrdersIngressScopeRunView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
