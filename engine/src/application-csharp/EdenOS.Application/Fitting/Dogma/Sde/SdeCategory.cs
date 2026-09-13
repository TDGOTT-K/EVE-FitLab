using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

internal sealed class SdeCategory
{
    [JsonPropertyName("_key")]
    public int CategoryId { get; set; }

    [JsonPropertyName("name")]
    public Dictionary<string, string>? Name { get; set; }
}
