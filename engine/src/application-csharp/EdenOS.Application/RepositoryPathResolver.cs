namespace EdenOS.Application;

public static class RepositoryPathResolver
{
    public static string Find(string startPath, params string[] requiredRelativePaths)
    {
        var resolved = TryFind(startPath, requiredRelativePaths);
        if (resolved is not null)
        {
            return resolved;
        }

        var requirements = requiredRelativePaths.Length == 0
            ? "EdenOsRewrite.sln"
            : string.Join(", ", requiredRelativePaths);
        throw new DirectoryNotFoundException($"Could not locate repository root. Expected: {requirements}.");
    }

    public static string? TryResolveRepositoryRoot()
    {
        return TryFind(AppContext.BaseDirectory, "EdenOsRewrite.sln");
    }

    public static bool TryResolveExistingFile(out string path, params string[] relativeSegments)
    {
        var repositoryRoot = TryResolveRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            path = string.Empty;
            return false;
        }

        path = Path.Combine([repositoryRoot, .. relativeSegments]);
        return File.Exists(path);
    }

    private static string? TryFind(string startPath, params string[] requiredRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);

        var normalizedRequirements = requiredRelativePaths.Length == 0
            ? ["EdenOsRewrite.sln"]
            : requiredRelativePaths;
        var current = new DirectoryInfo(Path.GetFullPath(startPath));

        while (current is not null)
        {
            if (normalizedRequirements.All(relativePath => File.Exists(Path.Combine(current.FullName, relativePath))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
