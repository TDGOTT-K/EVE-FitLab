using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Sde;

namespace EdenOS.Application.Fitting.Dogma.DataSources;

internal static class TypeDogmaJsonOverlayStore
{
    public static IReadOnlyDictionary<int, SdeTypeDogma> Load(string? overlayJsonPath)
    {
        if (string.IsNullOrWhiteSpace(overlayJsonPath))
        {
            return Empty();
        }

        var fullPath = Path.GetFullPath(overlayJsonPath);
        if (!File.Exists(fullPath))
        {
            return Empty();
        }

        using var stream = File.OpenRead(fullPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Object)
        {
            root = entriesElement;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Empty();
        }

        var overlays = new Dictionary<int, SdeTypeDogma>();
        foreach (var property in root.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var typeId) || property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var typeDogma = JsonSerializer.Deserialize<SdeTypeDogma>(property.Value.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (typeDogma is null)
            {
                continue;
            }

            if (typeDogma.TypeId == 0)
            {
                typeDogma.TypeId = typeId;
            }

            overlays[typeId] = typeDogma;
        }

        return overlays;
    }

    private static IReadOnlyDictionary<int, SdeTypeDogma> Empty()
    {
        return new Dictionary<int, SdeTypeDogma>();
    }
}
