using EdenOS.Application.Fitting.Dogma.DataSources;

namespace EdenOS.Application.Fitting;

internal static class DogmaLocalization
{
    public static string ResolveLocalizedTypeName(
        IDogmaDataSource? dataSource,
        int? dogmaTypeId,
        string fallbackName,
        string? locale)
    {
        if (dataSource is null || dogmaTypeId is null or <= 0)
        {
            return fallbackName;
        }

        var type = dataSource.GetType(dogmaTypeId.Value);
        return ResolveLocalizedName(type?.Name, fallbackName, locale);
    }

    public static IReadOnlyList<string> GetCandidateTypeNames(
        IDogmaDataSource? dataSource,
        int? dogmaTypeId,
        string fallbackName,
        string? locale)
    {
        var candidates = new List<string>();
        AddCandidate(candidates, fallbackName);

        if (dataSource is null || dogmaTypeId is null or <= 0)
        {
            return candidates;
        }

        var type = dataSource.GetType(dogmaTypeId.Value);
        if (type?.Name is null || type.Name.Count == 0)
        {
            return candidates;
        }

        foreach (var localeCandidate in BuildLocalePreferenceChain(locale))
        {
            if (TryGetLocalizedValue(type.Name, localeCandidate, out var localizedValue))
            {
                AddCandidate(candidates, localizedValue);
            }
        }

        foreach (var localizedValue in type.Name.Values)
        {
            AddCandidate(candidates, localizedValue);
        }

        return candidates;
    }

    public static string ResolveLocalizedName(
        IReadOnlyDictionary<string, string>? names,
        string fallbackName,
        string? locale)
    {
        if (names is null || names.Count == 0)
        {
            return fallbackName;
        }

        foreach (var localeCandidate in BuildLocalePreferenceChain(locale))
        {
            if (TryGetLocalizedValue(names, localeCandidate, out var localizedValue))
            {
                return localizedValue;
            }
        }

        foreach (var value in names.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return fallbackName;
    }

    public static string? NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        return locale.Trim().Replace('_', '-');
    }

    private static IReadOnlyList<string> BuildLocalePreferenceChain(string? locale)
    {
        var normalized = NormalizeLocale(locale);
        var chain = new List<string>();

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            AddCandidate(chain, normalized);

            var separatorIndex = normalized.IndexOf('-');
            if (separatorIndex > 0)
            {
                AddCandidate(chain, normalized[..separatorIndex]);
            }
        }

        AddCandidate(chain, "en");
        return chain;
    }

    private static bool TryGetLocalizedValue(
        IReadOnlyDictionary<string, string> names,
        string locale,
        out string localizedValue)
    {
        foreach (var (key, value) in names)
        {
            if (!string.Equals(key, locale, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                localizedValue = value.Trim();
                return true;
            }
        }

        localizedValue = string.Empty;
        return false;
    }

    private static void AddCandidate(ICollection<string> candidates, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (candidates.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(normalized);
    }
}
