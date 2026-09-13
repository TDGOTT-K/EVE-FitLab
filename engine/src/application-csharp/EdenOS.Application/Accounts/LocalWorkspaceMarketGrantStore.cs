using EdenOS.Application.Market;
using EdenOS.Contracts.Market;

namespace EdenOS.Application.Accounts;

internal interface IWorkspaceMarketGrantStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    IReadOnlyList<WorkspaceStructureGrant> GetGrantsForEsiCharacter(string esiCharacterId);
}

internal sealed class LocalWorkspaceMarketGrantStore : IWorkspaceMarketGrantStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<WorkspaceStructureGrant>> _grantsByCharacterId;

    private LocalWorkspaceMarketGrantStore(
        string bundleVersion,
        MarketFactSource source,
        IReadOnlyDictionary<string, IReadOnlyList<WorkspaceStructureGrant>> grantsByCharacterId)
    {
        BundleVersion = bundleVersion;
        Source = source;
        _grantsByCharacterId = grantsByCharacterId;
    }

    public string BundleVersion { get; }

    public MarketFactSource Source { get; }

    public static IWorkspaceMarketGrantStore LoadDefaultOrBootstrapFallback()
    {
        if (!MarketFactsDataPaths.TryResolveDefaultLayout(out var layout))
        {
            return CreateBootstrapFallback();
        }

        return LoadFromLayout(layout);
    }

    internal static IWorkspaceMarketGrantStore LoadFromDirectory(string marketFactsDirectoryPath)
    {
        return LoadFromLayout(MarketFactsDataPaths.ResolveFromDirectory(marketFactsDirectoryPath));
    }

    public IReadOnlyList<WorkspaceStructureGrant> GetGrantsForEsiCharacter(string esiCharacterId)
    {
        return _grantsByCharacterId.TryGetValue(esiCharacterId, out var grants)
            ? grants
            : [];
    }

    private static IWorkspaceMarketGrantStore LoadFromLayout(MarketFactsDataLayout layout)
    {
        return new LocalWorkspaceMarketGrantStore(
            layout.BundleVersion,
            new MarketFactSource("local-market-structure-grant-store", IsCanonicalSurface: true, UsesPlaceholderData: false),
            MarketFactFileCodec.ReadStructureGrants(layout.StructureGrantsPath));
    }

    private static IWorkspaceMarketGrantStore CreateBootstrapFallback()
    {
        return new LocalWorkspaceMarketGrantStore(
            "bootstrap-fallback",
            new MarketFactSource("bootstrap-market-structure-grant-fallback", IsCanonicalSurface: true, UsesPlaceholderData: true),
            new Dictionary<string, IReadOnlyList<WorkspaceStructureGrant>>(StringComparer.Ordinal)
            {
                ["95204315"] =
                [
                    new WorkspaceStructureGrant(
                        1038457641676,
                        "workspace-bound-esi-bootstrap",
                        DateTimeOffset.Parse("2026-04-11T04:31:00Z"),
                        IsStale: false)
                ],
                ["91000002"] =
                [
                    new WorkspaceStructureGrant(
                        1038457641676,
                        "workspace-bound-esi-bootstrap",
                        DateTimeOffset.Parse("2026-04-11T04:31:00Z"),
                        IsStale: false)
                ]
            });
    }
}
