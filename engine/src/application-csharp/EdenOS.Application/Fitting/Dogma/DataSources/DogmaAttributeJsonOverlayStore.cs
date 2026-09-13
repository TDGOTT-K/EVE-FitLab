using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Sde;

namespace EdenOS.Application.Fitting.Dogma.DataSources;

internal static class DogmaAttributeJsonOverlayStore
{
    public static IReadOnlyDictionary<int, SdeDogmaAttribute> Load(string? overlayJsonPath)
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

        var overlays = new Dictionary<int, SdeDogmaAttribute>();
        foreach (var property in root.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var attributeId))
            {
                continue;
            }

            var attribute = ParseAttribute(attributeId, property.Value);
            if (attribute is null)
            {
                continue;
            }

            overlays[attributeId] = attribute;
        }

        return overlays;
    }

    public static SdeDogmaAttribute? Merge(SdeDogmaAttribute? baseAttribute, SdeDogmaAttribute? overlay)
    {
        if (overlay is null)
        {
            return baseAttribute;
        }

        if (baseAttribute is null)
        {
            return overlay;
        }

        return new SdeDogmaAttribute
        {
            AttributeId = overlay.AttributeId != 0 ? overlay.AttributeId : baseAttribute.AttributeId,
            Name = string.IsNullOrWhiteSpace(overlay.Name) ? baseAttribute.Name : overlay.Name,
            DefaultValue = overlay.DefaultValue ?? baseAttribute.DefaultValue,
            Published = overlay.Published || baseAttribute.Published,
            Stackable = overlay.Stackable || baseAttribute.Stackable,
            HighIsGood = overlay.HighIsGood || baseAttribute.HighIsGood
        };
    }

    private static SdeDogmaAttribute? ParseAttribute(int attributeId, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new SdeDogmaAttribute
        {
            AttributeId = attributeId,
            Name = TryGetString(element, "name") ?? string.Empty,
            DefaultValue = TryGetDouble(element, "defaultValue"),
            Published = TryGetBoolean(element, "published"),
            Stackable = TryGetBoolean(element, "stackable"),
            HighIsGood = TryGetBoolean(element, "highIsGood")
        };
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static IReadOnlyDictionary<int, SdeDogmaAttribute> Empty()
    {
        return new Dictionary<int, SdeDogmaAttribute>();
    }
}
