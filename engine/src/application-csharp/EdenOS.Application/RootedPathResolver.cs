namespace EdenOS.Application;

public static class RootedPathResolver
{
    public static string ResolvePath(string baseDirectoryPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetFullPath(path, Path.GetFullPath(baseDirectoryPath));
    }

    public static string? ResolveOptionalPath(string baseDirectoryPath, string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : ResolvePath(baseDirectoryPath, path);
    }
}
