using System.Text.Json;
using EdenOS.Application.Fitting.Dogma.Sde;

namespace EdenOS.Application.Fitting.Dogma.DataSources;

internal static class DogmaEffectJsonOverlayStore
{
    public static IReadOnlyDictionary<int, SdeDogmaEffect> Load(string? overlayJsonPath)
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

        var overlays = new Dictionary<int, SdeDogmaEffect>();
        foreach (var property in root.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var effectId) || effectId == 0)
            {
                continue;
            }

            var effect = ParseEffect(effectId, property.Value);
            if (effect is null)
            {
                continue;
            }

            overlays[effectId] = effect;
        }

        return overlays;
    }

    public static SdeDogmaEffect? Merge(SdeDogmaEffect? baseEffect, SdeDogmaEffect? overlay)
    {
        if (overlay is null)
        {
            return baseEffect;
        }

        if (baseEffect is null)
        {
            return overlay;
        }

        return new SdeDogmaEffect
        {
            EffectId = overlay.EffectId != 0 ? overlay.EffectId : baseEffect.EffectId,
            Name = string.IsNullOrWhiteSpace(overlay.Name) ? baseEffect.Name : overlay.Name,
            EffectCategoryId = overlay.EffectCategoryId ?? baseEffect.EffectCategoryId,
            DurationAttributeId = overlay.DurationAttributeId ?? baseEffect.DurationAttributeId,
            DischargeAttributeId = overlay.DischargeAttributeId ?? baseEffect.DischargeAttributeId,
            RangeAttributeId = overlay.RangeAttributeId ?? baseEffect.RangeAttributeId,
            TrackingSpeedAttributeId = overlay.TrackingSpeedAttributeId ?? baseEffect.TrackingSpeedAttributeId,
            FalloffAttributeId = overlay.FalloffAttributeId ?? baseEffect.FalloffAttributeId,
            ModifierInfo = overlay.ModifierInfo.Count > 0
                ? overlay.ModifierInfo
                : baseEffect.ModifierInfo
        };
    }

    private static SdeDogmaEffect? ParseEffect(int effectId, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var effect = new SdeDogmaEffect
        {
            EffectId = effectId,
            Name = TryGetString(element, "name") ?? string.Empty,
            EffectCategoryId = TryGetInt32(element, "effectCategoryID") ?? TryGetInt32(element, "effectCategory"),
            DurationAttributeId = TryGetInt32(element, "durationAttributeID"),
            DischargeAttributeId = TryGetInt32(element, "dischargeAttributeID"),
            RangeAttributeId = TryGetInt32(element, "rangeAttributeID"),
            TrackingSpeedAttributeId = TryGetInt32(element, "trackingSpeedAttributeID"),
            FalloffAttributeId = TryGetInt32(element, "falloffAttributeID")
        };

        if (element.TryGetProperty("modifierInfo", out var modifierArray) &&
            modifierArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var modifierElement in modifierArray.EnumerateArray())
            {
                if (modifierElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                effect.ModifierInfo.Add(new SdeDogmaEffectModifierInfo
                {
                    Domain = TryGetString(modifierElement, "domain") ?? string.Empty,
                    Func = TryGetString(modifierElement, "func") ?? string.Empty,
                    ModifiedAttributeId = TryGetInt32(modifierElement, "modifiedAttributeID"),
                    ModifyingAttributeId = TryGetInt32(modifierElement, "modifyingAttributeID"),
                    Operation = TryGetInt32(modifierElement, "operation"),
                    GroupId = TryGetInt32(modifierElement, "groupID"),
                    SkillTypeId = TryGetInt32(modifierElement, "skillTypeID")
                });
            }
        }

        return effect;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
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

    private static IReadOnlyDictionary<int, SdeDogmaEffect> Empty()
    {
        return new Dictionary<int, SdeDogmaEffect>();
    }
}
