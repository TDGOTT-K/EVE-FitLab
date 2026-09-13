using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeTypeDogma
{
    [JsonPropertyName("_key")]
    public int TypeId { get; set; }

    [JsonPropertyName("dogmaAttributes")]
    public List<SdeTypeDogmaAttributeValue> DogmaAttributes { get; set; } = new();

    [JsonPropertyName("dogmaEffects")]
    public List<SdeTypeDogmaEffectValue> DogmaEffects { get; set; } = new();
}

public sealed class SdeTypeDogmaAttributeValue
{
    [JsonPropertyName("attributeID")]
    public int AttributeId { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}

public sealed class SdeTypeDogmaEffectValue
{
    [JsonPropertyName("effectID")]
    public int EffectId { get; set; }

    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; set; }
}
