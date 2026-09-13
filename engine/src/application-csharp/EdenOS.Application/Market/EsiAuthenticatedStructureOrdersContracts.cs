namespace EdenOS.Application.Market;

public sealed record EsiAuthenticatedStructureOrdersPullRequest
{
    public required string WorkspaceId { get; init; }

    public string? MarketFactsDirectoryPath { get; init; }

    public required string OutputDirectoryPath { get; init; }

    public IReadOnlyList<long> StructureIds { get; init; } = Array.Empty<long>();

    public string RequestedBy { get; init; } = "cli.esi_auth.pull_structure_orders";

    public string TriggerKind { get; init; } = "cli_host";

    public string BaseUrl { get; init; } = "https://esi.evetech.net/latest/";

    public string Datasource { get; init; } = "tranquility";

    public string UserAgent { get; init; } = "EdenOS Rewrite esi-authenticated-structure-orders";

    public string? CompatibilityDate { get; init; }

    public int MaxConcurrentRequests { get; init; } = 6;

    public int MaxRetriesPerRequest { get; init; } = 2;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int RetryBaseDelayMilliseconds { get; init; } = 750;

    public int MinimumTokenValiditySeconds { get; init; } = 300;

    public bool ImportIntoMarketOrdersRuntime { get; init; } = true;
}

public sealed record EsiAuthenticatedStructureOrdersPullView
{
    public required string WorkspaceId { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string Status { get; init; }

    public required string PayloadPath { get; init; }

    public required string ArchivePath { get; init; }

    public required string MarketFactsDirectoryPath { get; init; }

    public required string StructureDirectoryPath { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int RequestedStructureCount { get; init; }

    public int PulledStructureCount { get; init; }

    public int FailedStructureCount { get; init; }

    public int OrderCount { get; init; }

    public int SnapshotCount { get; init; }

    public int StructureDirectoryUpsertedCount { get; init; }

    public int StructureDirectoryTotalCount { get; init; }

    public MarketOrdersRuntimeRunView? ImportRun { get; init; }

    public IReadOnlyList<EsiAuthenticatedStructureOrdersStructureView> Structures { get; init; } = Array.Empty<EsiAuthenticatedStructureOrdersStructureView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiAuthenticatedStructureOrdersStructureView
{
    public long StructureId { get; init; }

    public string? StructureName { get; init; }

    public long? SolarSystemId { get; init; }

    public required string Status { get; init; }

    public string? SelectedEsiCharacterId { get; init; }

    public int PageCount { get; init; }

    public int RequestCount { get; init; }

    public int RetryCount { get; init; }

    public int OrderCount { get; init; }

    public int SnapshotCount { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? LastModifiedAtUtc { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
