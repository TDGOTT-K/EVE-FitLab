using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeDogmaAttribute
{
    [JsonPropertyName("_key")]
    public int AttributeId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("defaultValue")]
    public double? DefaultValue { get; set; }

    [JsonPropertyName("published")]
    public bool Published { get; set; }

    [JsonPropertyName("stackable")]
    public bool Stackable { get; set; }

    [JsonPropertyName("highIsGood")]
    public bool HighIsGood { get; set; }
}
