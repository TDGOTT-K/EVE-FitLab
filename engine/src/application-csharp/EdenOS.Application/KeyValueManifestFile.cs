namespace EdenOS.Application;

internal static class KeyValueManifestFile
{
    public static IReadOnlyDictionary<string, string> Read(string manifestPath)
    {
        return File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);
    }

    public static string RequireEntry(
        IReadOnlyDictionary<string, string> manifestEntries,
        string key,
        string manifestLabel)
    {
        if (manifestEntries.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"{manifestLabel} manifest is missing '{key}'.");
    }
}
