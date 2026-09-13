// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Sde/SdeType.cs
// Summary: Defines the Sde Type component for this project.

using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeType
{
    [JsonPropertyName("_key")]
    public int TypeId { get; set; }

    [JsonPropertyName("groupID")]
    public int GroupId { get; set; }

    [JsonPropertyName("marketGroupID")]
    public int? MarketGroupId { get; set; }

    [JsonPropertyName("metaGroupID")]
    public int? MetaGroupId { get; set; }

    [JsonPropertyName("mass")]
    public double? Mass { get; set; }

    [JsonPropertyName("capacity")]
    public double? Capacity { get; set; }

    [JsonPropertyName("volume")]
    public double? Volume { get; set; }

    [JsonPropertyName("radius")]
    public double? Radius { get; set; }

    [JsonPropertyName("portionSize")]
    public int? PortionSize { get; set; }

    [JsonPropertyName("published")]
    public bool Published { get; set; }

    [JsonPropertyName("name")]
    public Dictionary<string, string>? Name { get; set; }
}
