namespace EdenOS.Contracts.StarMap;

public enum StarMapFactStatus
{
    Success,
    Stale,
    Partial,
    Failed,
    NotAuthorized,
    NotSupported
}

public sealed record StarMapSystemJumpEntry(
    long SolarSystemId,
    long ShipJumps,
    DateTimeOffset ObservedAtUtc);

public sealed record StarMapSystemJumpCatalog(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ObservedAtUtc,
    string Source,
    string Datasource,
    StarMapFactStatus Status,
    IReadOnlyList<StarMapSystemJumpEntry> Items);

public sealed record StarMapSystemKillEntry(
    long SolarSystemId,
    long ShipKills,
    long NpcKills,
    long PodKills,
    DateTimeOffset ObservedAtUtc);

public sealed record StarMapSystemKillCatalog(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ObservedAtUtc,
    string Source,
    string Datasource,
    StarMapFactStatus Status,
    IReadOnlyList<StarMapSystemKillEntry> Items);

public sealed record StarMapSovereigntyMapEntry(
    long SolarSystemId,
    long? AllianceId,
    long? CorporationId,
    long? FactionId,
    DateTimeOffset ObservedAtUtc);

public sealed record StarMapSovereigntyMapCatalog(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? ObservedAtUtc,
    string Source,
    string Datasource,
    StarMapFactStatus Status,
    IReadOnlyList<StarMapSovereigntyMapEntry> Items);
