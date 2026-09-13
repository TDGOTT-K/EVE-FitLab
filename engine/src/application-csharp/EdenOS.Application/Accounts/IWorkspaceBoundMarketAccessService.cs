using EdenOS.Contracts.Market;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Accounts;

public interface IWorkspaceBoundMarketAccessService
{
    UseCaseResult<WorkspaceMarketAccessCatalog> GetMarketAccessCatalog(string accountKey);

    UseCaseResult<WorkspaceStructureAccessCatalog> GetStructureAccessCatalog(string workspaceId);
}

public sealed record WorkspaceStructureGrant(
    long StructureId,
    string GrantSource,
    DateTimeOffset LastVerifiedAtUtc,
    bool IsStale);

public sealed record WorkspaceMarketAccessCatalog(
    string AccountKey,
    IReadOnlyList<WorkspaceStructureGrant> StructureGrants,
    MarketFactSource Source,
    string BundleVersion);

public sealed record WorkspaceStructureAccessBinding(
    long StructureId,
    string GrantSource,
    DateTimeOffset LastVerifiedAtUtc,
    bool IsStale,
    IReadOnlyList<string> AuthorizedEsiCharacterIds);

public sealed record WorkspaceStructureAccessCatalog(
    string WorkspaceId,
    string AccountKey,
    IReadOnlyList<WorkspaceStructureAccessBinding> Structures,
    MarketFactSource Source,
    string BundleVersion);
