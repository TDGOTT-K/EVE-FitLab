using EdenOS.Contracts.Accounts;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Workspaces;

public sealed record WorkspaceIsolationBoundary
{
    public bool CredentialBoundAccountContext { get; init; } = true;

    public bool RejectsBareAccountIdAtClientBoundary { get; init; } = true;

    public bool MarketReadsAreAccountScoped { get; init; } = true;

    public bool IndustryPlanningIsAccountScoped { get; init; } = true;

    public bool TrustedServicesMayPropagateExplicitAccountContext { get; init; } = true;
}

public sealed record WorkspaceContext
{
    public required string WorkspaceId { get; init; }

    public required string WorkspaceName { get; init; }

    public required AccountContext Account { get; init; }

    public required string CharacterPoolId { get; init; }

    public WorkspaceIsolationBoundary IsolationBoundary { get; init; } = new();
}

public sealed record WorkspaceSummary
{
    public required string WorkspaceId { get; init; }

    public required string WorkspaceName { get; init; }

    public required AccountContext Account { get; init; }

    public required string CharacterPoolId { get; init; }

    public int TotalCharacters { get; init; }

    public int EsiCharacters { get; init; }

    public int VirtualCharacters { get; init; }

    public int TotalOperators { get; init; }

    public IReadOnlyList<CharacterPoolEntry> Characters { get; init; } = Array.Empty<CharacterPoolEntry>();

    public IReadOnlyList<OperatorPoolEntry> Operators { get; init; } = Array.Empty<OperatorPoolEntry>();
}

public sealed record OpenWorkspaceRequest
{
    public required CredentialBoundPrincipal Principal { get; init; }

    public string? PreferredWorkspaceName { get; init; }
}

public sealed record GetWorkspaceSummaryRequest
{
    public required string WorkspaceId { get; init; }
}

public interface IWorkspaceService
{
    UseCaseResult<WorkspaceContext> Open(OpenWorkspaceRequest request);

    UseCaseResult<WorkspaceSummary> GetSummary(GetWorkspaceSummaryRequest request);
}
