using System.Text.Json;

namespace EdenOS.Application.Planning;

internal sealed class LocalIndustryReferenceCatalog
{
    private readonly IReadOnlyDictionary<long, decimal> _adjustedPricesByTypeId;
    private readonly IReadOnlyDictionary<long, IReadOnlyDictionary<string, decimal>> _costIndicesBySystemId;

    private LocalIndustryReferenceCatalog(
        string bundleVersion,
        IReadOnlyDictionary<long, decimal> adjustedPricesByTypeId,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, decimal>> costIndicesBySystemId)
    {
        BundleVersion = bundleVersion;
        _adjustedPricesByTypeId = adjustedPricesByTypeId;
        _costIndicesBySystemId = costIndicesBySystemId;
    }

    public string BundleVersion { get; }

    public static LocalIndustryReferenceCatalog LoadDefaultOrEmpty()
    {
        try
        {
            if (!TryFindManifestPath(out var manifestPath))
            {
                return Empty;
            }

            return LoadFromManifest(manifestPath);
        }
        catch
        {
            return Empty;
        }
    }

    public bool TryGetAdjustedPrice(long typeId, out decimal adjustedPrice)
    {
        return _adjustedPricesByTypeId.TryGetValue(typeId, out adjustedPrice);
    }

    public bool TryGetSystemCostIndex(long solarSystemId, string activityKind, out decimal costIndex)
    {
        costIndex = default;
        if (!_costIndicesBySystemId.TryGetValue(solarSystemId, out var activities))
        {
            return false;
        }

        return activities.TryGetValue(activityKind, out costIndex);
    }

    private static LocalIndustryReferenceCatalog LoadFromManifest(string manifestPath)
    {
        var manifestEntries = KeyValueManifestFile.Read(manifestPath);

        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new DirectoryNotFoundException("Industry reference manifest directory could not be resolved.");

        var adjustedPricesPath = Path.Combine(
            manifestDirectory,
            KeyValueManifestFile.RequireEntry(manifestEntries, "partition.adjusted_prices", "Industry reference"));
        var systemCostIndicesPath = Path.Combine(
            manifestDirectory,
            KeyValueManifestFile.RequireEntry(manifestEntries, "partition.system_cost_indices", "Industry reference"));

        var adjustedPricesByTypeId = LoadAdjustedPrices(adjustedPricesPath);
        var costIndicesBySystemId = LoadSystemCostIndices(systemCostIndicesPath);

        return new LocalIndustryReferenceCatalog(
            manifestEntries.TryGetValue("bundle_version", out var bundleVersion) && !string.IsNullOrWhiteSpace(bundleVersion)
                ? bundleVersion
                : "unknown",
            adjustedPricesByTypeId,
            costIndicesBySystemId);
    }

    private static IReadOnlyDictionary<long, decimal> LoadAdjustedPrices(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("prices", out var pricesElement) || pricesElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<long, decimal>();
        }

        var prices = new Dictionary<long, decimal>();
        foreach (var priceElement in pricesElement.EnumerateArray())
        {
            if (!priceElement.TryGetProperty("type_id", out var typeIdElement) ||
                !priceElement.TryGetProperty("adjusted_price", out var adjustedPriceElement) ||
                !typeIdElement.TryGetInt64(out var typeId))
            {
                continue;
            }

            if (!JsonElementValueReader.TryReadDecimal(adjustedPriceElement, out var adjustedPrice))
            {
                continue;
            }

            prices[typeId] = adjustedPrice;
        }

        return prices;
    }

    private static IReadOnlyDictionary<long, IReadOnlyDictionary<string, decimal>> LoadSystemCostIndices(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("systems", out var systemsElement) || systemsElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<long, IReadOnlyDictionary<string, decimal>>();
        }

        var systems = new Dictionary<long, IReadOnlyDictionary<string, decimal>>();
        foreach (var systemElement in systemsElement.EnumerateArray())
        {
            if (!systemElement.TryGetProperty("solar_system_id", out var systemIdElement) ||
                !systemIdElement.TryGetInt64(out var solarSystemId))
            {
                continue;
            }

            if (!systemElement.TryGetProperty("activities", out var activitiesElement) ||
                activitiesElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var activities = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var property in activitiesElement.EnumerateObject())
            {
                if (JsonElementValueReader.TryReadDecimal(property.Value, out var value))
                {
                    activities[property.Name] = value;
                }
            }

            if (activities.Count > 0)
            {
                systems[solarSystemId] = activities;
            }
        }

        return systems;
    }

    private static bool TryFindManifestPath(out string manifestPath)
    {
        return RepositoryPathResolver.TryResolveExistingFile(
            out manifestPath,
            "data",
            "industry-reference",
            "manifest.txt");
    }

    private static LocalIndustryReferenceCatalog Empty { get; } = new(
        "empty",
        new Dictionary<long, decimal>(),
        new Dictionary<long, IReadOnlyDictionary<string, decimal>>());
}
