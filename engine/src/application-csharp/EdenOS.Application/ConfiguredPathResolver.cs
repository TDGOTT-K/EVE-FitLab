namespace EdenOS.Application;

internal static class ConfiguredPathResolver
{
    public static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path.Trim());
    }

    public static string? ResolveOptionalPath(string? configuredPath, string environmentVariableName)
    {
        return NormalizeOptionalPath(configuredPath)
            ?? NormalizeOptionalPath(Environment.GetEnvironmentVariable(environmentVariableName));
    }

    public static string? ResolveExistingDirectory(string? configuredPath, string environmentVariableName)
    {
        return ResolveExistingDirectory(configuredPath)
            ?? ResolveExistingDirectory(Environment.GetEnvironmentVariable(environmentVariableName));
    }

    public static string? ResolveExistingFile(string? configuredPath, string environmentVariableName)
    {
        return ResolveExistingFile(configuredPath)
            ?? ResolveExistingFile(Environment.GetEnvironmentVariable(environmentVariableName));
    }

    public static string? ResolveExistingDirectory(string? path)
    {
        var normalizedPath = NormalizeOptionalPath(path);
        return normalizedPath is not null && Directory.Exists(normalizedPath)
            ? normalizedPath
            : null;
    }

    public static string? ResolveExistingFile(string? path)
    {
        var normalizedPath = NormalizeOptionalPath(path);
        return normalizedPath is not null && File.Exists(normalizedPath)
            ? normalizedPath
            : null;
    }
}
