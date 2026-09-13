using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Storage;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeTypeDogmaStore
{
    private readonly string _filePath;
    private readonly JsonlOffsetIndex _index;
    private readonly JsonSerializerOptions _jsonOptions;

    public SdeTypeDogmaStore(string filePath, JsonlOffsetIndex index)
    {
        _filePath = filePath;
        _index = index;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public SdeTypeDogma? GetById(int typeId)
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

        return JsonSerializer.Deserialize<SdeTypeDogma>(line, _jsonOptions);
    }
}
