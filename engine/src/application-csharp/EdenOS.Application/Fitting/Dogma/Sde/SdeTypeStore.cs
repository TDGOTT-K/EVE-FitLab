// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Sde/SdeTypeStore.cs
// Summary: Defines the Sde Type Store component for this project.

using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Storage;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeTypeStore
{
    private readonly JsonlOffsetIndex _index;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public SdeTypeStore(string filePath, JsonlOffsetIndex index)
    {
        _filePath = filePath;
        _index = index;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public SdeType? GetById(int typeId)
    {
        if (!_index.TryGetOffset(typeId, out var offset))
        {
            return null;
        }

        var line = JsonlLineReader.ReadLineAtOffset(_filePath, offset);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SdeType>(line, _jsonOptions);
    }
}
