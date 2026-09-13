namespace EdenOS.Application.Market;

internal static class MarketFactsDataPaths
{
    private const string DefaultCurrentSnapshotsFileName = "current-snapshots.json";
    private const string DefaultCurrentOrdersFileName = "current-orders.json";
    private const string DefaultHistoryWindowsFileName = "history-windows.json";
    private const string DefaultStatisticsProjectionsFileName = "statistics-projections.json";
    private const string DefaultStructureGrantsFileName = "structure-grants.json";
    private const string DefaultStructureDirectoryOverlayFileName = "structure-directory.tsv";

    public static bool TryResolveDefaultLayout(out MarketFactsDataLayout layout)
    {
        if (!TryFindManifestPath(out var manifestPath))
        {
            layout = null!;
            return false;
        }

        layout = ResolveFromManifestPath(manifestPath);
        return true;
    }

    public static MarketFactsDataLayout ResolveFromDirectory(string marketFactsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        var manifestPath = Path.Combine(Path.GetFullPath(marketFactsDirectoryPath), "manifest.txt");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Market facts manifest could not be found.", manifestPath);
        }

        return ResolveFromManifestPath(manifestPath);
    }

    public static MarketFactsDataLayout EnsureLayoutInDirectory(string marketFactsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        var fullDirectoryPath = Path.GetFullPath(marketFactsDirectoryPath);
        Directory.CreateDirectory(fullDirectoryPath);

        var manifestPath = Path.Combine(fullDirectoryPath, "manifest.txt");
        if (!File.Exists(manifestPath))
        {
            File.WriteAllLines(
                manifestPath,
                CreateManifestLines(
                    "initialized-local-market-facts",
                    DefaultCurrentSnapshotsFileName,
                    DefaultCurrentOrdersFileName,
                    DefaultHistoryWindowsFileName,
                    DefaultStatisticsProjectionsFileName,
                    DefaultStructureGrantsFileName));
        }

        var layout = ResolveFromManifestPath(manifestPath);
        EnsurePartitionFileExists(layout.CurrentSnapshotsPath, "{\"snapshots\":[]}");
        EnsurePartitionFileExists(layout.CurrentOrdersPath, "{\"order_books\":[]}");
        EnsurePartitionFileExists(layout.HistoryWindowsPath, "{\"series\":[]}");
        EnsurePartitionFileExists(layout.StatisticsProjectionsPath, "{\"projections\":[]}");
        EnsurePartitionFileExists(layout.StructureGrantsPath, "{\"character_grants\":[]}");

        return layout;
    }

    public static void WriteManifest(MarketFactsDataLayout layout, string bundleVersion)
    {
        ArgumentNullException.ThrowIfNull(layout);

        WriteLinesAtomically(
            layout.ManifestPath,
            CreateManifestLines(
                bundleVersion,
                Path.GetFileName(layout.CurrentSnapshotsPath),
                Path.GetFileName(layout.CurrentOrdersPath),
                Path.GetFileName(layout.HistoryWindowsPath),
                Path.GetFileName(layout.StatisticsProjectionsPath),
                Path.GetFileName(layout.StructureGrantsPath)));
    }

    public static string GetStructureDirectoryOverlayPath(string marketFactsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(marketFactsDirectoryPath))
        {
            throw new ArgumentException("Market facts directory path is required.", nameof(marketFactsDirectoryPath));
        }

        return Path.Combine(Path.GetFullPath(marketFactsDirectoryPath), DefaultStructureDirectoryOverlayFileName);
    }

    private static bool TryFindManifestPath(out string manifestPath)
    {
        return RepositoryPathResolver.TryResolveExistingFile(
            out manifestPath,
            "data",
            "market-facts",
            "manifest.txt");
    }

    private static MarketFactsDataLayout ResolveFromManifestPath(string manifestPath)
    {
        var manifestEntries = ReadManifest(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new DirectoryNotFoundException("Market facts manifest directory could not be resolved.");

        return new MarketFactsDataLayout(
            manifestEntries.TryGetValue("bundle_version", out var bundleVersion) && !string.IsNullOrWhiteSpace(bundleVersion)
                ? bundleVersion
                : "unknown",
            manifestDirectory,
            manifestPath,
            Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.current_snapshots")),
            Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.current_orders")),
            Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.history_windows")),
            Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.statistics_projections")),
            Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.structure_grants")));
    }

    private static IReadOnlyDictionary<string, string> ReadManifest(string manifestPath)
    {
        return KeyValueManifestFile.Read(manifestPath);
    }

    private static string RequiredManifestEntry(IReadOnlyDictionary<string, string> manifestEntries, string key)
    {
        return KeyValueManifestFile.RequireEntry(manifestEntries, key, "Market facts");
    }

    private static void EnsurePartitionFileExists(string path, string fallbackJson)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, fallbackJson);
        }
    }

    private static void WriteLinesAtomically(string path, IReadOnlyList<string> lines)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(tempPath, lines);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string[] CreateManifestLines(
        string bundleVersion,
        string currentSnapshotsFileName,
        string currentOrdersFileName,
        string historyWindowsFileName,
        string statisticsProjectionsFileName,
        string structureGrantsFileName)
    {
        return
        [
            $"bundle_version={bundleVersion}",
            $"partition.current_snapshots={currentSnapshotsFileName}",
            $"partition.current_orders={currentOrdersFileName}",
            $"partition.history_windows={historyWindowsFileName}",
            $"partition.statistics_projections={statisticsProjectionsFileName}",
            $"partition.structure_grants={structureGrantsFileName}"
        ];
    }
}

internal sealed record MarketFactsDataLayout(
    string BundleVersion,
    string DirectoryPath,
    string ManifestPath,
    string CurrentSnapshotsPath,
    string CurrentOrdersPath,
    string HistoryWindowsPath,
    string StatisticsProjectionsPath,
    string StructureGrantsPath);
