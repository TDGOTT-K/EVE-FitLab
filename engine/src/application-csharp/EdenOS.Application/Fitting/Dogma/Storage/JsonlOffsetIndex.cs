// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Storage/JsonlOffsetIndex.cs
// Summary: Defines the Jsonl Offset Index component for this project.

using System.Text;
using System.Text.Json;

namespace EdenOS.Application.Fitting.Dogma.Storage;

public sealed class JsonlOffsetIndex
{
    private const int CurrentVersion = 2;
    private readonly Dictionary<long, long> _offsets;

    public string SourceFilePath { get; }
    public long SourceFileLength { get; }
    public long SourceLastWriteUtcTicks { get; }
    public int BuildNumber { get; }

    private JsonlOffsetIndex(
        string sourceFilePath,
        long sourceFileLength,
        long sourceLastWriteUtcTicks,
        int buildNumber,
        Dictionary<long, long> offsets)
    {
        SourceFilePath = sourceFilePath;
        SourceFileLength = sourceFileLength;
        SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
        BuildNumber = buildNumber;
        _offsets = offsets;
    }

    public bool TryGetOffset(long id, out long offset) => _offsets.TryGetValue(id, out offset);

    public static JsonlOffsetIndex? Load(string indexPath, int expectedBuildNumber, string? expectedSourceFilePath = null)
    {
        if (!File.Exists(indexPath))
        {
            return null;
        }

        using var stream = File.OpenRead(indexPath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var magic = reader.ReadInt32();
        if (magic != 0x58444945) // "EIDX"
        {
            return null;
        }

        var version = reader.ReadInt32();
        if (version != CurrentVersion)
        {
            return null;
        }

        var buildNumber = reader.ReadInt32();
        if (buildNumber != expectedBuildNumber)
        {
            return null;
        }

        var sourcePath = reader.ReadString();
        if (!string.IsNullOrWhiteSpace(expectedSourceFilePath))
        {
            var expectedFullPath = Path.GetFullPath(expectedSourceFilePath);
            var sourceFullPath = Path.GetFullPath(sourcePath);
            if (!string.Equals(expectedFullPath, sourceFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var sourceLength = reader.ReadInt64();
        var sourceLastWriteUtcTicks = reader.ReadInt64();
        if (!string.IsNullOrWhiteSpace(expectedSourceFilePath) && File.Exists(expectedSourceFilePath))
        {
            var info = new FileInfo(expectedSourceFilePath);
            if (info.Length != sourceLength || info.LastWriteTimeUtc.Ticks != sourceLastWriteUtcTicks)
            {
                return null;
            }
        }

        var count = reader.ReadInt32();
        var offsets = new Dictionary<long, long>(count);
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadInt64();
            var offset = reader.ReadInt64();
            offsets[key] = offset;
        }

        return new JsonlOffsetIndex(sourcePath, sourceLength, sourceLastWriteUtcTicks, buildNumber, offsets);
    }

    public void Save(string indexPath)
    {
        var dir = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(indexPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(0x58444945); // "EIDX"
        writer.Write(CurrentVersion);
        writer.Write(BuildNumber);
        writer.Write(SourceFilePath);
        writer.Write(SourceFileLength);
        writer.Write(SourceLastWriteUtcTicks);
        writer.Write(_offsets.Count);
        foreach (var (key, offset) in _offsets)
        {
            writer.Write(key);
            writer.Write(offset);
        }
    }

    public static JsonlOffsetIndex BuildIndex(string filePath, string idFieldName, int buildNumber)
    {
        var offsets = new Dictionary<long, long>();
        var (bomLength, newlineLength) = DetectBomAndNewline(filePath);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        long offset = bomLength;
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty(idFieldName, out var idProp))
                    {
                        if (TryReadId(idProp, out var id))
                        {
                            offsets[id] = offset;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed lines; keep index build resilient.
                }
            }

            var byteCount = encoding.GetByteCount(line);
            if (reader.EndOfStream)
            {
                offset += byteCount;
            }
            else
            {
                offset += byteCount + newlineLength;
            }
        }

        var sourceInfo = new FileInfo(filePath);
        return new JsonlOffsetIndex(
            sourceInfo.FullName,
            sourceInfo.Length,
            sourceInfo.LastWriteTimeUtc.Ticks,
            buildNumber,
            offsets);
    }

    private static bool TryReadId(JsonElement element, out long id)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out id))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out id))
        {
            return true;
        }

        id = 0;
        return false;
    }

    private static (long bomLength, int newlineLength) DetectBomAndNewline(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[Math.Min(1024 * 1024, stream.Length)];
        var read = stream.Read(buffer, 0, buffer.Length);

        long bomLength = 0;
        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            bomLength = 3;
        }

        var newlineLength = 1;
        for (var i = 0; i < read; i++)
        {
            if (buffer[i] == (byte)'\n')
            {
                if (i > 0 && buffer[i - 1] == (byte)'\r')
                {
                    newlineLength = 2;
                }
                break;
            }
        }

        return (bomLength, newlineLength);
    }
}
