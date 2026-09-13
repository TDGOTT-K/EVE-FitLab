// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Sde/SdeGroupStore.cs
// Summary: Defines the Sde Group Store component for this project.

using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Storage;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeGroupStore
{
    private readonly JsonlOffsetIndex _index;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public SdeGroupStore(string filePath, JsonlOffsetIndex index)
    {
        _filePath = filePath;
        _index = index;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public SdeGroup? GetById(int groupId)
    {
        if (!_index.TryGetOffset(groupId, out var offset))
        {
            return null;
        }

        var line = JsonlLineReader.ReadLineAtOffset(_filePath, offset);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SdeGroup>(line, _jsonOptions);
    }
}
