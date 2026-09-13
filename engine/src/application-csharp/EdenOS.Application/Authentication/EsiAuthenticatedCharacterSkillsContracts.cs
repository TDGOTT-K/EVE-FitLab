namespace EdenOS.Application.Authentication;

public sealed record EsiAuthenticatedCharacterSkillsPullRequest
{
    public required string WorkspaceId { get; init; }

    public IReadOnlyList<string> EsiCharacterIds { get; init; } = Array.Empty<string>();

    public string OutputDirectoryPath { get; init; } = string.Empty;

    public string RequestedBy { get; init; } = "cli.esi_auth.pull_character_skills";

    public string TriggerKind { get; init; } = "cli_host";

    public string BaseUrl { get; init; } = "https://esi.evetech.net/latest/";

    public string Datasource { get; init; } = "tranquility";

    public string UserAgent { get; init; } = "EdenOS Rewrite esi-authenticated-character-skills";

    public string? CompatibilityDate { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 30;

    public int MaxRetriesPerRequest { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 750;

    public int MinimumTokenValiditySeconds { get; init; } = 300;

    public bool UpdateCharacterPool { get; init; } = true;
}

public sealed record EsiAuthenticatedCharacterSkillsPullView
{
    public required string WorkspaceId { get; init; }

    public required string RequestedBy { get; init; }

    public required string TriggerKind { get; init; }

    public required string OutputDirectoryPath { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public int RequestedCharacterCount { get; init; }

    public int PulledCharacterCount { get; init; }

    public int FailedCharacterCount { get; init; }

    public IReadOnlyList<EsiAuthenticatedCharacterSkillsCharacterView> Characters { get; init; } = Array.Empty<EsiAuthenticatedCharacterSkillsCharacterView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiAuthenticatedCharacterSkillsCharacterView
{
    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public required string Status { get; init; }

    public string? PayloadPath { get; init; }

    public string? ArchivePath { get; init; }

    public string? SelectedScope { get; init; }

    public int SkillCount { get; init; }

    public long TotalSkillPoints { get; init; }

    public long UnallocatedSkillPoints { get; init; }

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public IReadOnlyList<EsiAuthenticatedCharacterSkillView> Skills { get; init; } = Array.Empty<EsiAuthenticatedCharacterSkillView>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record EsiAuthenticatedCharacterSkillView
{
    public long SkillTypeId { get; init; }

    public required string SkillName { get; init; }

    public required string SkillKey { get; init; }

    public int TrainedLevel { get; init; }

    public int ActiveLevel { get; init; }

    public long SkillPointsInSkill { get; init; }
}
