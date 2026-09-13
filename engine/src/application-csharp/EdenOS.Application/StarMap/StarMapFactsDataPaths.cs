namespace EdenOS.Application.StarMap;

internal static class StarMapFactsDataPaths
{
    public static string ResolveRootPath(string? configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return Path.GetFullPath(configuredRootPath);
        }

        if (TryResolveDefaultRootPath(out var rootPath))
        {
            return rootPath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "star-map-facts"));
    }

    public static bool TryResolveDefaultRootPath(out string rootPath)
    {
        var repositoryRoot = RepositoryPathResolver.TryResolveRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            rootPath = string.Empty;
            return false;
        }

        rootPath = Path.Combine(repositoryRoot, "data", "star-map-facts");
        return true;
    }
}
