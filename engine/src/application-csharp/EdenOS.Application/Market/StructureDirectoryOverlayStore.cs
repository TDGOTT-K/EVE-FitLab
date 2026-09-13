using System.Globalization;
using System.Text;

namespace EdenOS.Application.Market;

internal sealed class StructureDirectoryOverlayStore
{
    private const string HeaderLine = "structure_id\tsolar_system_id\tname\towner_corporation_name\tdocking_access";

    private readonly string _path;

    public StructureDirectoryOverlayStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Structure directory path is required.", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public IReadOnlyDictionary<long, StructureDirectoryEntry> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<long, StructureDirectoryEntry>();
        }

        var lines = File.ReadAllLines(_path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToArray();

        if (lines.Length == 0)
        {
            return new Dictionary<long, StructureDirectoryEntry>();
        }

        var rows = new Dictionary<long, StructureDirectoryEntry>();
        foreach (var line in lines.Skip(1))
        {
            var values = line.Split('\t');
            if (values.Length < 5)
            {
                continue;
            }

            var entry = new StructureDirectoryEntry(
                long.Parse(values[0], CultureInfo.InvariantCulture),
                long.Parse(values[1], CultureInfo.InvariantCulture),
                values[2],
                values[3],
                values[4]);
            rows[entry.StructureId] = entry;
        }

        return rows;
    }

    public StructureDirectoryOverlayWriteResult Upsert(IEnumerable<StructureDirectoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var merged = new Dictionary<long, StructureDirectoryEntry>(Load());
        var upserted = 0;
        foreach (var entry in entries)
        {
            merged[entry.StructureId] = entry;
            upserted++;
        }

        WriteAll(merged.Values.OrderBy(value => value.StructureId).ToArray());
        return new StructureDirectoryOverlayWriteResult(_path, upserted, merged.Count);
    }

    private void WriteAll(IReadOnlyList<StructureDirectoryEntry> entries)
    {
        var directoryPath = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var writer = new StreamWriter(tempPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(HeaderLine);
                foreach (var entry in entries)
                {
                    writer.Write(entry.StructureId.ToString(CultureInfo.InvariantCulture));
                    writer.Write('\t');
                    writer.Write(entry.SolarSystemId.ToString(CultureInfo.InvariantCulture));
                    writer.Write('\t');
                    writer.Write(entry.Name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                    writer.Write('\t');
                    writer.Write(entry.OwnerCorporationName.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                    writer.Write('\t');
                    writer.Write(entry.DockingAccess.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
                    writer.WriteLine();
                }
            }

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _path);
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
}

internal sealed record StructureDirectoryEntry(
    long StructureId,
    long SolarSystemId,
    string Name,
    string OwnerCorporationName,
    string DockingAccess);

internal sealed record StructureDirectoryOverlayWriteResult(
    string Path,
    int UpsertedEntryCount,
    int TotalEntryCount);
