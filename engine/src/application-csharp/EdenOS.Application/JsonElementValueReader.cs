using System.Globalization;
using System.Text.Json;

namespace EdenOS.Application;

internal static class JsonElementValueReader
{
    public static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
