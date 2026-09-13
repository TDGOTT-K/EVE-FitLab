// File: EveAnalyzerCore/EveAnalyzerCore/EveData/Sde/SdeGroup.cs
// Summary: Defines the Sde Group component for this project.

using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeGroup
{
    [JsonPropertyName("_key")]
    public int GroupId { get; set; }

    [JsonPropertyName("categoryID")]
    public int CategoryId { get; set; }

    [JsonPropertyName("name")]
    public Dictionary<string, string>? Name { get; set; }

    [JsonPropertyName("published")]
    public bool Published { get; set; }
}
