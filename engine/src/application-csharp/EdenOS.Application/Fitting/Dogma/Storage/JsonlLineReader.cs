// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Storage/JsonlLineReader.cs
// Summary: Defines the Jsonl Line Reader component for this project.

using System.Buffers;
using System.Text;

namespace EdenOS.Application.Fitting.Dogma.Storage;

public static class JsonlLineReader
{
    public static string? ReadLineAtOffset(string filePath, long offset)
    {
        if (offset < 0)
        {
            return null;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(offset, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var lineBuffer = new MemoryStream();
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var newlineIndex = Array.IndexOf(buffer, (byte)'\n', 0, read);
                if (newlineIndex >= 0)
                {
                    lineBuffer.Write(buffer, 0, newlineIndex);
                    break;
                }

                lineBuffer.Write(buffer, 0, read);
            }

            var bytes = lineBuffer.ToArray();
            if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            {
                Array.Resize(ref bytes, bytes.Length - 1);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
