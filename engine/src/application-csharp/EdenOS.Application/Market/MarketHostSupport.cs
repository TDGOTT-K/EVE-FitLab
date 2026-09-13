using System.Text.Json;

namespace EdenOS.Application.Market;

public static class MarketHostSupport
{
    public static JsonSerializerOptions CreateHostJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        };
    }

    public static string RequireValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count)
        {
            throw new InvalidOperationException($"Option '{optionName}' requires a value.");
        }

        index++;
        return args[index];
    }

    public static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"Option '{optionName}' must be a positive integer.");
        }

        return parsed;
    }

    public static T LoadJsonConfig<T>(string repoRoot, string? configuredPath, string defaultRelativePath, string missingConfigMessage, string invalidConfigMessage)
    {
        var configPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(repoRoot, defaultRelativePath)
            : RootedPathResolver.ResolvePath(repoRoot, configuredPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"{missingConfigMessage} '{configPath}'.");
        }

        return JsonSerializer.Deserialize<T>(
            File.ReadAllText(configPath),
            CreateHostJsonOptions())
            ?? throw new InvalidOperationException(invalidConfigMessage);
    }
}
