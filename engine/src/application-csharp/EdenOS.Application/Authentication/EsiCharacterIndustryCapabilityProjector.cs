using EdenOS.Application.Accounts;
using EdenOS.Contracts.CharacterPool;

namespace EdenOS.Application.Authentication;

internal static class EsiCharacterIndustryCapabilityProjector
{
    public static CharacterCapabilityProfile Project(
        CharacterCapabilityProfile currentProfile,
        IReadOnlyList<CharacterIndustrySkillLevel> normalizedSkillLevels,
        DateTimeOffset observedAtUtc)
    {
        return currentProfile with
        {
            SkillProfile = "esi_live_skill_snapshot",
            Industry = IndustrySkillCapabilityProjector.Project(
                currentProfile.Industry,
                normalizedSkillLevels,
                $"Projected from authenticated ESI skills at {FormatInstant(observedAtUtc)}.")
        };
    }

    private static string FormatInstant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
