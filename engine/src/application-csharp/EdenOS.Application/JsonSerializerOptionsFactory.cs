using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenOS.Application;

public static class JsonSerializerOptionsFactory
{
    public static JsonSerializerOptions CreateSnakeCase(
        bool writeIndented = false,
        bool propertyNameCaseInsensitive = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
