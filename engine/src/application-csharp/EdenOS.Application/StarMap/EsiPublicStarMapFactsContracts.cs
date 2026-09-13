namespace EdenOS.Application.StarMap;

public sealed record EsiPublicStarMapFactsRefreshRequest
{
    public string OutputDirectoryPath { get; init; } = string.Empty;

    public string RequestedBy { get; init; } = "cli.star_map.refresh_public_facts";

    public string TriggerKind { get; init; } = "cli_host";

    public string BaseUrl { get; init; } = "https://esi.evetech.net/latest/";

    public string Datasource { get; init; } = "tranquility";

    public string UserAgent { get; init; } = "EdenOS Rewrite star-map-public-facts";

    public string? CompatibilityDate { get; init; }

    public int MaxRetriesPerRequest { get; init; } = 2;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int RetryBaseDelayMilliseconds { get; init; } = 750;
}

public sealed record EsiPublicStarMapFactsRefreshView
{
    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string OutputDirectoryPath { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public required string Status { get; init; }

    public IReadOnlyList<EsiPublicStarMapFactPartitionView> Partitions { get; init; } = Array.Empty<EsiPublicStarMapFactPartitionView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiPublicStarMapFactPartitionView
{
    public required string PartitionKey { get; init; }

    public required string OutputPath { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int RequestCount { get; init; }

    public int RetryCount { get; init; }

    public int ItemCount { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
