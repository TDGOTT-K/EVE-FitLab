using System.Globalization;

namespace EdenOS.Application.Planning;

internal static class PlanningMetadataReader
{
    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public static bool TryReadString(IReadOnlyDictionary<string, string> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        var normalized = NormalizeOptional(rawValue);
        if (normalized is null)
        {
            return false;
        }

        value = normalized;
        return true;
    }

    public static bool TryReadLong(IReadOnlyDictionary<string, string> metadata, string key, out long value)
    {
        value = default;
        return TryReadString(metadata, key, out var rawValue)
               && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static decimal? ReadDecimal(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return TryReadString(metadata, key, out var rawValue) &&
               decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static bool TryParseTypeId(string? rawValue, out long typeId)
    {
        typeId = default;
        var normalized = NormalizeOptional(rawValue);
        return normalized is not null &&
               long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out typeId);
    }
}
