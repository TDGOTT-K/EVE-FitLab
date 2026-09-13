using System.Text.Json.Serialization;

namespace EdenOS.Application.Fitting.Dogma.Sde;

public sealed class SdeDogmaEffect
{
    [JsonPropertyName("_key")]
    public int EffectId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("effectCategoryID")]
    public int? EffectCategoryId { get; set; }

    [JsonPropertyName("durationAttributeID")]
    public int? DurationAttributeId { get; set; }

    [JsonPropertyName("dischargeAttributeID")]
    public int? DischargeAttributeId { get; set; }

    [JsonPropertyName("rangeAttributeID")]
    public int? RangeAttributeId { get; set; }

    [JsonPropertyName("trackingSpeedAttributeID")]
    public int? TrackingSpeedAttributeId { get; set; }

    [JsonPropertyName("falloffAttributeID")]
    public int? FalloffAttributeId { get; set; }

    [JsonPropertyName("modifierInfo")]
    public List<SdeDogmaEffectModifierInfo> ModifierInfo { get; set; } = new();
}

public sealed class SdeDogmaEffectModifierInfo
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("func")]
    public string Func { get; set; } = string.Empty;

    [JsonPropertyName("modifiedAttributeID")]
    public int? ModifiedAttributeId { get; set; }

    [JsonPropertyName("modifyingAttributeID")]
    public int? ModifyingAttributeId { get; set; }

    [JsonPropertyName("operation")]
    public int? Operation { get; set; }

    [JsonPropertyName("groupID")]
    public int? GroupId { get; set; }

    [JsonPropertyName("skillTypeID")]
    public int? SkillTypeId { get; set; }
}
