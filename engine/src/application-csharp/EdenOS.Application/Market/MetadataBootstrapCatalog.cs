using System.Globalization;

namespace EdenOS.Application.Market;

internal sealed class MetadataBootstrapCatalog
{
    private readonly IReadOnlyDictionary<long, MetadataTypeEntry> _typesById;
    private readonly IReadOnlyDictionary<long, MetadataLocationEntry> _locationsById;
    private readonly IReadOnlyDictionary<long, MetadataRegionEntry> _regionsById;
    private readonly IReadOnlyDictionary<long, MetadataSolarSystemEntry> _solarSystemsById;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<MetadataSolarSystemEntry>> _solarSystemsByRegionId;
    private readonly IReadOnlyDictionary<long, MetadataMarketGroupEntry> _marketGroupsById;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintActivityEntry>> _blueprintActivitiesByBlueprintId;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintActivityEntry>> _blueprintActivitiesByProductTypeId;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintMaterialEntry>> _blueprintMaterialsByBlueprintId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<MetadataBlueprintSkillRequirementEntry>> _blueprintSkillRequirementsByActivityKey;

    private MetadataBootstrapCatalog(
        string bundleVersion,
        IReadOnlyList<string> nonActiveSourceKeys,
        IReadOnlyDictionary<long, MetadataTypeEntry> typesById,
        IReadOnlyDictionary<long, MetadataLocationEntry> locationsById,
        IReadOnlyDictionary<long, MetadataRegionEntry> regionsById,
        IReadOnlyDictionary<long, MetadataSolarSystemEntry> solarSystemsById,
        IReadOnlyDictionary<long, IReadOnlyList<MetadataSolarSystemEntry>> solarSystemsByRegionId,
        IReadOnlyDictionary<long, MetadataMarketGroupEntry> marketGroupsById,
        IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintActivityEntry>> blueprintActivitiesByBlueprintId,
        IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintActivityEntry>> blueprintActivitiesByProductTypeId,
        IReadOnlyDictionary<long, IReadOnlyList<MetadataBlueprintMaterialEntry>> blueprintMaterialsByBlueprintId,
        IReadOnlyDictionary<string, IReadOnlyList<MetadataBlueprintSkillRequirementEntry>> blueprintSkillRequirementsByActivityKey)
    {
        BundleVersion = bundleVersion;
        NonActiveSourceKeys = nonActiveSourceKeys;
        _typesById = typesById;
        _locationsById = locationsById;
        _regionsById = regionsById;
        _solarSystemsById = solarSystemsById;
        _solarSystemsByRegionId = solarSystemsByRegionId;
        _marketGroupsById = marketGroupsById;
        _blueprintActivitiesByBlueprintId = blueprintActivitiesByBlueprintId;
        _blueprintActivitiesByProductTypeId = blueprintActivitiesByProductTypeId;
        _blueprintMaterialsByBlueprintId = blueprintMaterialsByBlueprintId;
        _blueprintSkillRequirementsByActivityKey = blueprintSkillRequirementsByActivityKey;
    }

    public string BundleVersion { get; }

    public IReadOnlyList<string> NonActiveSourceKeys { get; }

    public static MetadataBootstrapCatalog LoadDefault()
    {
        var manifestPath = FindManifestPath();
        return LoadFromManifest(
            manifestPath,
            ResolveDefaultStructureDirectoryOverlayPath(manifestPath));
    }

    public static MetadataBootstrapCatalog LoadFromPaths(
        string manifestPath,
        string? structureDirectoryOverlayPath = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Metadata manifest path is required.", nameof(manifestPath));
        }

        return LoadFromManifest(
            Path.GetFullPath(manifestPath),
            string.IsNullOrWhiteSpace(structureDirectoryOverlayPath)
                ? null
                : Path.GetFullPath(structureDirectoryOverlayPath));
    }

    public bool HasRegion(long regionId) => _regionsById.ContainsKey(regionId);

    public IEnumerable<MetadataRegionEntry> ListRegions()
    {
        return _regionsById.Values;
    }

    public bool TryGetRegion(long regionId, out MetadataRegionEntry region)
    {
        return _regionsById.TryGetValue(regionId, out region!);
    }

    public IEnumerable<MetadataSolarSystemEntry> ListSolarSystems(long? regionId = null)
    {
        if (regionId.HasValue)
        {
            return _solarSystemsByRegionId.GetValueOrDefault(regionId.Value, Array.Empty<MetadataSolarSystemEntry>());
        }

        return _solarSystemsById.Values;
    }

    public bool TryGetSolarSystem(long solarSystemId, out MetadataSolarSystemEntry solarSystem)
    {
        return _solarSystemsById.TryGetValue(solarSystemId, out solarSystem!);
    }

    public IEnumerable<MetadataTypeEntry> ListPublishedTypes()
    {
        return _typesById.Values.Where(type => type.IsPublished);
    }

    public bool TryGetType(long typeId, out MetadataTypeEntry type)
    {
        return _typesById.TryGetValue(typeId, out type!);
    }

    public bool TryGetLocation(long locationId, out MetadataLocationEntry location)
    {
        return _locationsById.TryGetValue(locationId, out location!);
    }

    public bool TryGetMarketGroup(long marketGroupId, out MetadataMarketGroupEntry marketGroup)
    {
        return _marketGroupsById.TryGetValue(marketGroupId, out marketGroup!);
    }

    public IReadOnlyList<MetadataMarketGroupEntry> GetMarketGroupPath(long? marketGroupId)
    {
        if (!marketGroupId.HasValue || !_marketGroupsById.ContainsKey(marketGroupId.Value))
        {
            return Array.Empty<MetadataMarketGroupEntry>();
        }

        var path = new List<MetadataMarketGroupEntry>();
        var visited = new HashSet<long>();
        var currentId = marketGroupId;

        while (currentId.HasValue &&
               visited.Add(currentId.Value) &&
               _marketGroupsById.TryGetValue(currentId.Value, out var current))
        {
            path.Add(current);
            currentId = current.ParentMarketGroupId;
        }

        path.Reverse();
        return path;
    }

    public IReadOnlyList<MetadataBlueprintActivityEntry> GetBlueprintActivities(long blueprintTypeId)
    {
        return _blueprintActivitiesByBlueprintId.GetValueOrDefault(blueprintTypeId, Array.Empty<MetadataBlueprintActivityEntry>());
    }

    public IReadOnlyList<MetadataBlueprintActivityEntry> GetBlueprintActivitiesForProduct(long productTypeId)
    {
        return _blueprintActivitiesByProductTypeId.GetValueOrDefault(productTypeId, Array.Empty<MetadataBlueprintActivityEntry>());
    }

    public IReadOnlyList<MetadataBlueprintMaterialEntry> GetBlueprintMaterials(long blueprintTypeId, string? activityKind = null)
    {
        var materials = _blueprintMaterialsByBlueprintId.GetValueOrDefault(blueprintTypeId, Array.Empty<MetadataBlueprintMaterialEntry>());
        return string.IsNullOrWhiteSpace(activityKind)
            ? materials
            : materials.Where(entry => string.Equals(entry.ActivityKind, activityKind, StringComparison.Ordinal)).ToArray();
    }

    public IReadOnlyList<MetadataBlueprintSkillRequirementEntry> GetBlueprintSkillRequirements(long blueprintTypeId, string? activityKind = null)
    {
        if (string.IsNullOrWhiteSpace(activityKind))
        {
            return _blueprintSkillRequirementsByActivityKey
                .Where(pair => pair.Key.StartsWith($"{blueprintTypeId}:", StringComparison.Ordinal))
                .SelectMany(pair => pair.Value)
                .ToArray();
        }

        return _blueprintSkillRequirementsByActivityKey.GetValueOrDefault(
            CreateBlueprintActivityKey(blueprintTypeId, activityKind),
            Array.Empty<MetadataBlueprintSkillRequirementEntry>());
    }

    public bool HasBlueprintSkillRequirements => _blueprintSkillRequirementsByActivityKey.Count > 0;

    private static MetadataBootstrapCatalog LoadFromManifest(
        string manifestPath,
        string? structureDirectoryOverlayPath)
    {
        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new DirectoryNotFoundException("Manifest directory could not be resolved.");
        var manifestEntries = ReadManifest(manifestPath);

        var bundleVersion = manifestEntries.TryGetValue("bundle_version", out var manifestBundleVersion)
            ? manifestBundleVersion
            : "unknown";

        var nonActiveSources = manifestEntries
            .Where(entry => entry.Key.StartsWith("source.", StringComparison.Ordinal))
            .Select(entry => new
            {
                SourceKey = entry.Key["source.".Length..],
                Parts = entry.Value.Split('|', StringSplitOptions.None)
            })
            .Where(entry => entry.Parts.Length >= 3 && !string.Equals(entry.Parts[2], "active", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.SourceKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        var marketGroupsById = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.market_groups")))
            .Select(row => new MetadataMarketGroupEntry(
                ParseInt64(row, "market_group_id"),
                OptionalInt64(row, "parent_group_id"),
                RequiredValue(row, "name"),
                row.TryGetValue("description", out var description) ? description : string.Empty))
            .ToDictionary(group => group.MarketGroupId);

        var typesById = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.types")))
            .Select(row =>
            {
                var marketGroupId = OptionalInt64(row, "market_group_id");
                var volumeM3 = ParseDecimal(row, "volume_m3");
                var packagedVolumeM3 = OptionalDecimal(row, "packaged_volume_m3") ?? volumeM3;

                return new MetadataTypeEntry(
                    ParseInt64(row, "type_id"),
                    RequiredValue(row, "name"),
                    row.TryGetValue("description", out var description) ? description : string.Empty,
                    RequiredValue(row, "category_name"),
                    RequiredValue(row, "group_name"),
                    marketGroupId,
                    marketGroupId is null
                        ? RequiredValue(row, "group_name")
                        : marketGroupsById.GetValueOrDefault(marketGroupId.Value)?.Name ?? RequiredValue(row, "group_name"),
                    volumeM3,
                    packagedVolumeM3,
                    ParseBoolean(row, "published"));
            })
            .ToDictionary(type => type.TypeId);

        var regionsById = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.regions")))
            .Select(row => new MetadataRegionEntry(
                ParseInt64(row, "region_id"),
                RequiredValue(row, "name")))
            .ToDictionary(region => region.RegionId);

        var solarSystemsById = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.solar_systems")))
            .Select(row => new MetadataSolarSystemEntry(
                ParseInt64(row, "solar_system_id"),
                ParseInt64(row, "region_id"),
                RequiredValue(row, "name"),
                OptionalDecimal(row, "security_status")))
            .ToDictionary(system => system.SolarSystemId);

        var solarSystemsByRegionId = solarSystemsById.Values
            .GroupBy(system => system.RegionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetadataSolarSystemEntry>)group
                    .OrderBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var stationLocations = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.stations")))
            .Select(row =>
            {
                var solarSystemId = ParseInt64(row, "solar_system_id");
                var solarSystem = solarSystemsById[solarSystemId];

                return new MetadataLocationEntry(
                    ParseInt64(row, "station_id"),
                    RequiredValue(row, "name"),
                    Contracts.Market.MarketLocationKind.Station,
                    solarSystem.RegionId,
                    solarSystem.SolarSystemId,
                    solarSystem.Name,
                    RequiresAccountGrant: false);
            });

        var structureLocations = ReadRows(Path.Combine(manifestDirectory, RequiredManifestEntry(manifestEntries, "partition.structures")))
            .Select(row =>
            {
                var solarSystemId = ParseInt64(row, "solar_system_id");
                var solarSystem = solarSystemsById[solarSystemId];
                var dockingAccess = RequiredValue(row, "docking_access");

                return new MetadataLocationEntry(
                    ParseInt64(row, "structure_id"),
                    RequiredValue(row, "name"),
                    Contracts.Market.MarketLocationKind.Structure,
                    solarSystem.RegionId,
                    solarSystem.SolarSystemId,
                    solarSystem.Name,
                    RequiresAccountGrant: !string.Equals(dockingAccess, "public", StringComparison.OrdinalIgnoreCase));
            });

        var locationsById = stationLocations
            .Concat(structureLocations)
            .ToDictionary(location => location.LocationId);

        foreach (var overlayLocation in ReadStructureOverlayLocations(structureDirectoryOverlayPath, solarSystemsById))
        {
            locationsById[overlayLocation.LocationId] = overlayLocation;
        }

        var blueprintActivityRows = ReadOptionalRows(
            manifestDirectory,
            OptionalManifestEntry(manifestEntries, "partition.blueprints"));
        var blueprintActivities = blueprintActivityRows
            .Select(row => new MetadataBlueprintActivityEntry(
                ParseInt64(row, "blueprint_type_id"),
                RequiredValue(row, "activity_kind"),
                row.TryGetValue("planner_semantic_class", out var plannerSemanticClass) && !string.IsNullOrWhiteSpace(plannerSemanticClass)
                    ? plannerSemanticClass
                    : "non_plannable_misc",
                OptionalInt64(row, "product_type_id"),
                OptionalInt64(row, "product_quantity"),
                OptionalDecimal(row, "product_probability"),
                OptionalInt64(row, "time_seconds"),
                OptionalInt64(row, "max_production_limit")))
            .ToArray();

        var blueprintMaterialRows = ReadOptionalRows(
            manifestDirectory,
            OptionalManifestEntry(manifestEntries, "partition.blueprint_materials"));
        var blueprintMaterials = blueprintMaterialRows
            .Select(row => new MetadataBlueprintMaterialEntry(
                ParseInt64(row, "blueprint_type_id"),
                RequiredValue(row, "activity_kind"),
                ParseInt64(row, "material_type_id"),
                ParseInt64(row, "quantity")))
            .ToArray();

        var blueprintActivitiesByBlueprintId = blueprintActivities
            .GroupBy(entry => entry.BlueprintTypeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetadataBlueprintActivityEntry>)group.ToArray());

        var blueprintActivitiesByProductTypeId = blueprintActivities
            .Where(entry => entry.ProductTypeId.HasValue)
            .GroupBy(entry => entry.ProductTypeId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetadataBlueprintActivityEntry>)group.ToArray());

        var blueprintMaterialsByBlueprintId = blueprintMaterials
            .GroupBy(entry => entry.BlueprintTypeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetadataBlueprintMaterialEntry>)group.ToArray());

        var blueprintSkillRequirementRows = ReadOptionalRows(
            manifestDirectory,
            OptionalManifestEntry(manifestEntries, "partition.blueprint_skill_requirements"));
        var blueprintSkillRequirements = blueprintSkillRequirementRows
            .Select(row =>
            {
                var skillTypeId = ParseInt64(row, "skill_type_id");
                return new MetadataBlueprintSkillRequirementEntry(
                    ParseInt64(row, "blueprint_type_id"),
                    RequiredValue(row, "activity_kind"),
                    skillTypeId,
                    typesById.TryGetValue(skillTypeId, out var skillType)
                        ? skillType.Name
                        : row.TryGetValue("skill_name", out var explicitSkillName) ? explicitSkillName : skillTypeId.ToString(CultureInfo.InvariantCulture),
                    (int)ParseInt64(row, "required_level"));
            })
            .ToArray();
        var blueprintSkillRequirementsByActivityKey = blueprintSkillRequirements
            .GroupBy(entry => CreateBlueprintActivityKey(entry.BlueprintTypeId, entry.ActivityKind), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetadataBlueprintSkillRequirementEntry>)group.ToArray(),
                StringComparer.Ordinal);

        return new MetadataBootstrapCatalog(
            bundleVersion,
            nonActiveSources,
            typesById,
            locationsById,
            regionsById,
            solarSystemsById,
            solarSystemsByRegionId,
            marketGroupsById,
            blueprintActivitiesByBlueprintId,
            blueprintActivitiesByProductTypeId,
            blueprintMaterialsByBlueprintId,
            blueprintSkillRequirementsByActivityKey);
    }

    private static string FindManifestPath()
    {
        if (RepositoryPathResolver.TryResolveExistingFile(out var manifestPath, "data", "static-data", "metadata", "manifest.txt"))
        {
            return manifestPath;
        }

        throw new DirectoryNotFoundException(
            "Could not locate metadata manifest. Expected EdenOsRewrite.sln and data/static-data/metadata/manifest.txt.");
    }

    private static string? ResolveDefaultStructureDirectoryOverlayPath(string manifestPath)
    {
        var metadataDirectory = System.IO.Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(metadataDirectory))
        {
            return null;
        }

        var repositoryRoot = Directory.GetParent(metadataDirectory)?.Parent?.Parent?.FullName;
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        return MarketFactsDataPaths.GetStructureDirectoryOverlayPath(
            System.IO.Path.Combine(repositoryRoot, "data", "market-facts"));
    }

    private static IReadOnlyDictionary<string, string> ReadManifest(string manifestPath)
    {
        return KeyValueManifestFile.Read(manifestPath);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(string path)
    {
        var lines = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToArray();

        if (lines.Length == 0)
        {
            return [];
        }

        var headers = lines[0].Split('\t');
        var rows = new List<IReadOnlyDictionary<string, string>>(Math.Max(0, lines.Length - 1));

        foreach (var line in lines.Skip(1))
        {
            var values = line.Split('\t');
            var row = new Dictionary<string, string>(headers.Length, StringComparer.Ordinal);

            for (var index = 0; index < headers.Length; index++)
            {
                row[headers[index]] = index < values.Length ? values[index] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadOptionalRows(string manifestDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return [];
        }

        var fullPath = Path.Combine(manifestDirectory, relativePath);
        return File.Exists(fullPath)
            ? ReadRows(fullPath)
            : [];
    }

    private static IReadOnlyList<MetadataLocationEntry> ReadStructureOverlayLocations(
        string? overlayPath,
        IReadOnlyDictionary<long, MetadataSolarSystemEntry> solarSystemsById)
    {
        if (string.IsNullOrWhiteSpace(overlayPath) || !File.Exists(overlayPath))
        {
            return [];
        }

        return ReadRows(overlayPath)
            .Select(row =>
            {
                var solarSystemId = ParseInt64(row, "solar_system_id");
                var solarSystem = solarSystemsById[solarSystemId];
                var dockingAccess = RequiredValue(row, "docking_access");

                return new MetadataLocationEntry(
                    ParseInt64(row, "structure_id"),
                    RequiredValue(row, "name"),
                    Contracts.Market.MarketLocationKind.Structure,
                    solarSystem.RegionId,
                    solarSystem.SolarSystemId,
                    solarSystem.Name,
                    RequiresAccountGrant: !string.Equals(dockingAccess, "public", StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
    }

    private static string RequiredManifestEntry(IReadOnlyDictionary<string, string> manifestEntries, string key)
    {
        return KeyValueManifestFile.RequireEntry(manifestEntries, key, "Metadata");
    }

    private static string? OptionalManifestEntry(IReadOnlyDictionary<string, string> manifestEntries, string key)
    {
        return manifestEntries.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string RequiredValue(IReadOnlyDictionary<string, string> row, string key)
    {
        if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Metadata row is missing '{key}'.");
    }

    private static long ParseInt64(IReadOnlyDictionary<string, string> row, string key)
    {
        return long.Parse(RequiredValue(row, key), CultureInfo.InvariantCulture);
    }

    private static long? OptionalInt64(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? long.Parse(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static decimal ParseDecimal(IReadOnlyDictionary<string, string> row, string key)
    {
        return decimal.Parse(RequiredValue(row, key), CultureInfo.InvariantCulture);
    }

    private static decimal? OptionalDecimal(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? decimal.Parse(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static bool ParseBoolean(IReadOnlyDictionary<string, string> row, string key)
    {
        return bool.Parse(RequiredValue(row, key));
    }

    private static string CreateBlueprintActivityKey(long blueprintTypeId, string activityKind)
    {
        return $"{blueprintTypeId}:{activityKind}";
    }

    internal sealed record MetadataMarketGroupEntry(
        long MarketGroupId,
        long? ParentMarketGroupId,
        string Name,
        string Description);

    internal sealed record MetadataTypeEntry(
        long TypeId,
        string Name,
        string Description,
        string CategoryName,
        string GroupName,
        long? MarketGroupId,
        string MarketGroupName,
        decimal VolumeM3,
        decimal PackagedVolumeM3,
        bool IsPublished);

    internal sealed record MetadataLocationEntry(
        long LocationId,
        string Name,
        Contracts.Market.MarketLocationKind Kind,
        long RegionId,
        long SolarSystemId,
        string SolarSystemName,
        bool RequiresAccountGrant);

    internal sealed record MetadataBlueprintActivityEntry(
        long BlueprintTypeId,
        string ActivityKind,
        string PlannerSemanticClass,
        long? ProductTypeId,
        long? ProductQuantity,
        decimal? ProductProbability,
        long? TimeSeconds,
        long? MaxProductionLimit);

    internal sealed record MetadataBlueprintMaterialEntry(
        long BlueprintTypeId,
        string ActivityKind,
        long MaterialTypeId,
        long Quantity);

    internal sealed record MetadataBlueprintSkillRequirementEntry(
        long BlueprintTypeId,
        string ActivityKind,
        long SkillTypeId,
        string SkillName,
        int RequiredLevel);

    internal sealed record MetadataRegionEntry(
        long RegionId,
        string Name);

    internal sealed record MetadataSolarSystemEntry(
        long SolarSystemId,
        long RegionId,
        string Name,
        decimal? SecurityStatus);
}
