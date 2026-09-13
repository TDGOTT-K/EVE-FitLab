using System.Globalization;
using System.Text.Json;
using EdenOS.Contracts.Market;

namespace EdenOS.Application.Market;

internal interface IAdjustedPriceStore
{
    string BundleVersion { get; }

    MarketFactSource Source { get; }

    DateTimeOffset? GeneratedAtUtc { get; }

    IReadOnlyList<AdjustedPriceRecord> ListPrices();

    bool TryGetAdjustedPrice(long typeId, out AdjustedPriceRecord record);
}

internal sealed record AdjustedPriceRecord(
    long TypeId,
    decimal AdjustedPrice,
    decimal? AveragePrice);

internal sealed class LocalAdjustedPriceStore : IAdjustedPriceStore
{
    private readonly AdjustedPriceCatalogData _catalog;

    public LocalAdjustedPriceStore()
        : this(AdjustedPriceCatalogData.LoadDefaultOrEmpty())
    {
    }

    internal LocalAdjustedPriceStore(AdjustedPriceCatalogData catalog)
    {
        _catalog = catalog;
    }

    public string BundleVersion => _catalog.BundleVersion;

    public MarketFactSource Source => _catalog.Source;

    public DateTimeOffset? GeneratedAtUtc => _catalog.GeneratedAtUtc;

    public IReadOnlyList<AdjustedPriceRecord> ListPrices() => _catalog.Prices;

    public bool TryGetAdjustedPrice(long typeId, out AdjustedPriceRecord record)
    {
        return _catalog.PricesByTypeId.TryGetValue(typeId, out record!);
    }
}

internal sealed class AdjustedPriceCatalogData
{
    private static readonly MarketFactSource EmptySource = new(
        "industry-reference.adjusted-prices",
        IsCanonicalSurface: true,
        UsesPlaceholderData: true);

    private AdjustedPriceCatalogData(
        string bundleVersion,
        MarketFactSource source,
        DateTimeOffset? generatedAtUtc,
        IReadOnlyDictionary<long, AdjustedPriceRecord> pricesByTypeId)
    {
        BundleVersion = bundleVersion;
        Source = source;
        GeneratedAtUtc = generatedAtUtc;
        PricesByTypeId = pricesByTypeId;
        Prices = pricesByTypeId.Values
            .OrderBy(record => record.TypeId)
            .ToArray();
    }

    public string BundleVersion { get; }

    public MarketFactSource Source { get; }

    public DateTimeOffset? GeneratedAtUtc { get; }

    public IReadOnlyDictionary<long, AdjustedPriceRecord> PricesByTypeId { get; }

    public IReadOnlyList<AdjustedPriceRecord> Prices { get; }

    public static AdjustedPriceCatalogData LoadDefaultOrEmpty()
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

    private static AdjustedPriceCatalogData LoadFromManifest(string manifestPath)
    {
        var manifestEntries = KeyValueManifestFile.Read(manifestPath);

        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new DirectoryNotFoundException("Adjusted-price manifest directory could not be resolved.");
        var adjustedPricesPath = Path.Combine(
            manifestDirectory,
            KeyValueManifestFile.RequireEntry(manifestEntries, "partition.adjusted_prices", "Adjusted-price"));

        using var document = JsonDocument.Parse(File.ReadAllText(adjustedPricesPath));
        var root = document.RootElement;

        var bundleVersion = manifestEntries.TryGetValue("bundle_version", out var manifestBundleVersion) &&
                            !string.IsNullOrWhiteSpace(manifestBundleVersion)
            ? manifestBundleVersion
            : "unknown";

        var sourceKind = root.TryGetProperty("source", out var sourceElement) &&
                         sourceElement.ValueKind == JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(sourceElement.GetString())
            ? sourceElement.GetString()!
            : "industry-reference.adjusted-prices";

        DateTimeOffset? generatedAtUtc = null;
        if (root.TryGetProperty("generated_at_utc", out var generatedAtUtcElement) &&
            generatedAtUtcElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                generatedAtUtcElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedGeneratedAtUtc))
        {
            generatedAtUtc = parsedGeneratedAtUtc;
        }

        var pricesByTypeId = new Dictionary<long, AdjustedPriceRecord>();
        if (root.TryGetProperty("prices", out var pricesElement) && pricesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var priceElement in pricesElement.EnumerateArray())
            {
                if (!priceElement.TryGetProperty("type_id", out var typeIdElement) ||
                    !typeIdElement.TryGetInt64(out var typeId) ||
                    !priceElement.TryGetProperty("adjusted_price", out var adjustedPriceElement) ||
                    !JsonElementValueReader.TryReadDecimal(adjustedPriceElement, out var adjustedPrice))
                {
                    continue;
                }

                decimal? averagePrice = null;
                if (priceElement.TryGetProperty("average_price", out var averagePriceElement) &&
                    JsonElementValueReader.TryReadDecimal(averagePriceElement, out var parsedAveragePrice))
                {
                    averagePrice = parsedAveragePrice;
                }

                pricesByTypeId[typeId] = new AdjustedPriceRecord(typeId, adjustedPrice, averagePrice);
            }
        }

        return new AdjustedPriceCatalogData(
            bundleVersion,
            new MarketFactSource(
                sourceKind,
                IsCanonicalSurface: true,
                UsesPlaceholderData: false),
            generatedAtUtc,
            pricesByTypeId);
    }

    private static bool TryFindManifestPath(out string manifestPath)
    {
        return RepositoryPathResolver.TryResolveExistingFile(
            out manifestPath,
            "data",
            "industry-reference",
            "manifest.txt");
    }

    private static AdjustedPriceCatalogData Empty { get; } = new(
        "empty",
        EmptySource,
        generatedAtUtc: null,
        new Dictionary<long, AdjustedPriceRecord>());
}
