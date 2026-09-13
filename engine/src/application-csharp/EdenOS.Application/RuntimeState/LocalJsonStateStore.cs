using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Application.Accounts;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Runtime;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.RuntimeState;

internal sealed class LocalJsonStateStore<TState>(string filePath, Func<TState> createEmptyState)
    where TState : class
{
    private const string StoreFormat = "edenos.runtime_state.v2";
    private const int MaxLockAcquireAttempts = 200;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _filePath = filePath;
    private readonly string _lockFilePath = $"{filePath}.lock";
    private readonly Func<TState> _createEmptyState = createEmptyState;
    private readonly object _syncRoot = new();
    private long _lastKnownRevision;
    private string _lastKnownContentHash = ComputeHash(Array.Empty<byte>());

    internal RuntimeStateFileFingerprint CurrentFingerprint
    {
        get
        {
            lock (_syncRoot)
            {
                return new RuntimeStateFileFingerprint
                {
                    Revision = _lastKnownRevision,
                    ContentHash = _lastKnownContentHash
                };
            }
        }
    }

    public TState Load()
    {
        lock (_syncRoot)
        {
            var snapshot = ReadSnapshot();
            _lastKnownRevision = snapshot.Revision;
            _lastKnownContentHash = snapshot.ContentHash;
            return snapshot.State;
        }
    }

    public void Save(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_syncRoot)
        {
            using var lockStream = AcquireExclusiveLock();
            var snapshot = ReadSnapshot();
            if (snapshot.Revision != _lastKnownRevision
                || !string.Equals(snapshot.ContentHash, _lastKnownContentHash, StringComparison.Ordinal))
            {
                throw new RuntimeStateConcurrencyException(
                    _filePath,
                    _lastKnownRevision,
                    snapshot.Revision,
                    _lastKnownContentHash,
                    snapshot.ContentHash);
            }

            var nextRevision = snapshot.Revision + 1;
            WriteSnapshot(nextRevision, state);
            _lastKnownRevision = nextRevision;
            _lastKnownContentHash = ComputeFileHash(_filePath);
        }
    }

    private StateSnapshot ReadSnapshot()
    {
        if (!File.Exists(_filePath))
        {
            return new StateSnapshot(0L, _createEmptyState(), ComputeHash(Array.Empty<byte>()));
        }

        using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var buffer = memory.ToArray();
        using var document = JsonDocument.Parse(buffer);
        var root = document.RootElement;
        var contentHash = ComputeHash(buffer);

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new StateSnapshot(0L, _createEmptyState(), contentHash);
        }

        if (root.TryGetProperty("state", out var stateElement))
        {
            var revision = root.TryGetProperty("revision", out var revisionElement) && revisionElement.TryGetInt64(out var value)
                ? value
                : 0L;
            return new StateSnapshot(
                revision,
                stateElement.Deserialize<TState>(JsonOptions) ?? _createEmptyState(),
                contentHash);
        }

        return new StateSnapshot(
            0L,
            root.Deserialize<TState>(JsonOptions) ?? _createEmptyState(),
            contentHash);
    }

    private FileStream AcquireExclusiveLock()
    {
        var directoryPath = Path.GetDirectoryName(_lockFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        IOException? lastException = null;
        for (var attempt = 0; attempt < MaxLockAcquireAttempts; attempt++)
        {
            try
            {
                return new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException exception)
            {
                lastException = exception;
                Thread.Sleep(25);
            }
        }

        throw new IOException($"Timed out waiting for runtime state lock '{_lockFilePath}'.", lastException);
    }

    private void WriteSnapshot(long revision, TState state)
    {
        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempFilePath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    new RuntimeStateEnvelope<TState>
                    {
                        StoreFormat = StoreFormat,
                        Revision = revision,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        State = state
                    },
                    JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(tempFilePath, _filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFilePath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return global::EdenOS.Application.JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ComputeHash(memory.ToArray());
    }

    private static string ComputeHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record StateSnapshot(long Revision, TState State, string ContentHash);
}

internal sealed class RuntimeStateConcurrencyException(
    string filePath,
    long expectedRevision,
    long actualRevision,
    string? expectedContentHash,
    string? actualContentHash)
    : InvalidOperationException(
        $"Runtime state file '{filePath}' changed from revision {expectedRevision} / hash '{expectedContentHash}' to revision {actualRevision} / hash '{actualContentHash}'.")
{
    public string FilePath { get; } = filePath;

    public long ExpectedRevision { get; } = expectedRevision;

    public long ActualRevision { get; } = actualRevision;

    public string? ExpectedContentHash { get; } = expectedContentHash;

    public string? ActualContentHash { get; } = actualContentHash;
}

internal sealed record RuntimeStateEnvelope<TState>
    where TState : class
{
    public required string StoreFormat { get; init; }

    public long Revision { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public required TState State { get; init; }
}

internal static class RuntimeStatePaths
{
    public static string GetWorkspaceStatePath(string rootPath) => Path.Combine(rootPath, "workspaces.json");

    public static string GetPlanStatePath(string rootPath) => Path.Combine(rootPath, "plans.json");

    public static string GetExecutionStatePath(string rootPath) => Path.Combine(rootPath, "execution.json");

    public static string GetTaskStatePath(string rootPath) => Path.Combine(rootPath, "tasks.json");

    public static string GetEsiCredentialStatePath(string rootPath) => Path.Combine(rootPath, "esi-credentials.json");

    public static string GetFittingStatePath(string rootPath) => Path.Combine(rootPath, "fitting.json");

    public static string GetWorkspacePlanRepairAuditStatePath(string rootPath) => Path.Combine(rootPath, "workspace-plan-repair-audit.json");

    public static string GetIdentityDirectoryStatePath(string rootPath) => Path.Combine(rootPath, "identity-directory.json");

    public static string? ResolveRootPath(string? configuredRootPath)
    {
        return string.IsNullOrWhiteSpace(configuredRootPath)
            ? null
            : Path.GetFullPath(configuredRootPath);
    }
}

internal sealed record WorkspaceRuntimeState
{
    public IReadOnlyList<PersistedWorkspaceRecord> Workspaces { get; init; } = Array.Empty<PersistedWorkspaceRecord>();
}

internal sealed record PersistedWorkspaceRecord
{
    public required WorkspaceContext Workspace { get; init; }

    public IReadOnlyDictionary<string, CharacterPoolEntry> Characters { get; init; } = new Dictionary<string, CharacterPoolEntry>();

    public IReadOnlyDictionary<string, OperatorPoolEntry> Operators { get; init; } = new Dictionary<string, OperatorPoolEntry>();

    public IReadOnlyDictionary<string, string> EsiCharacterIds { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<WorkspaceStructureGrant> StructureGrants { get; init; } = Array.Empty<WorkspaceStructureGrant>();
}

internal sealed record IndustryPlanRuntimeState
{
    public IReadOnlyList<IndustryPlan> Plans { get; init; } = Array.Empty<IndustryPlan>();
}

internal sealed record ExecutionRuntimeState
{
    public IReadOnlyList<PersistedExecutionStateRecord> States { get; init; } = Array.Empty<PersistedExecutionStateRecord>();
}

internal sealed record PersistedExecutionStateRecord
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public IReadOnlyList<string> CompletedNodeIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ExecutionEventRecord> Events { get; init; } = Array.Empty<ExecutionEventRecord>();

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

internal sealed record PendingTaskRuntimeState
{
    public IReadOnlyList<PendingTask> Tasks { get; init; } = Array.Empty<PendingTask>();
}

internal sealed record EsiCredentialRuntimeState
{
    public IReadOnlyList<PersistedEsiCredentialBindingRecord> Bindings { get; init; } = Array.Empty<PersistedEsiCredentialBindingRecord>();
}

internal sealed record FittingRuntimeState
{
    public IReadOnlyList<EdenOS.Contracts.Fitting.FitEnvironmentProfile> EnvironmentProfiles { get; init; } = Array.Empty<EdenOS.Contracts.Fitting.FitEnvironmentProfile>();
}

internal sealed record PersistedEsiCredentialBindingRecord
{
    public required string WorkspaceId { get; init; }

    public string? UserId { get; init; }

    public string? SubjectId { get; init; }

    public required string EsiCharacterId { get; init; }

    public required string CharacterName { get; init; }

    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public string? OwnerHash { get; init; }

    public string? OauthClientId { get; init; }

    public string? TokenEndpoint { get; init; }

    public string? TokenType { get; init; }

    public EsiCredentialSourceKind CredentialSource { get; init; }

    public DateTimeOffset BoundAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? LastRefreshAtUtc { get; init; }

    public DateTimeOffset? LastRefreshAttemptAtUtc { get; init; }

    public string? LastRefreshError { get; init; }

    public string? Notes { get; init; }
}

internal sealed record WorkspacePlanRepairAuditState
{
    public long LatestSequenceNumber { get; init; }

    public string? HeadRunHash { get; init; }

    public long CompactedThroughSequenceNumber { get; init; }

    public string? CompactedHeadRunHash { get; init; }

    public WorkspacePlanRepairAuditCheckpoint? LatestAppliedCheckpoint { get; init; }

    public IReadOnlyList<WorkspacePlanRepairAuditCompactionSummary> Compactions { get; init; } = Array.Empty<WorkspacePlanRepairAuditCompactionSummary>();

    public IReadOnlyList<WorkspacePlanRepairAuditRun> Runs { get; init; } = Array.Empty<WorkspacePlanRepairAuditRun>();
}
