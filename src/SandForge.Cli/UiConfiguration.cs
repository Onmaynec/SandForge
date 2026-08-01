using System.Text.Json;

namespace SandForge.Cli;

internal sealed record UiConfiguration(string Language, bool Animations)
{
    public static UiConfiguration Load(string baseDirectory)
    {
        string? environmentLanguage = Environment.GetEnvironmentVariable("SANDFORGE_LANGUAGE");
        foreach (string candidate in CandidatePaths(baseDirectory))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (!document.RootElement.TryGetProperty("ui", out JsonElement ui)) break;
                string language = environmentLanguage
                    ?? (ui.TryGetProperty("language", out JsonElement languageElement) ? languageElement.GetString() : null)
                    ?? "ru";
                bool animations = ui.TryGetProperty("animations", out JsonElement animationsElement)
                    && animationsElement.ValueKind is JsonValueKind.True;
                return new UiConfiguration(NormalizeLanguage(language), animations);
            }
            catch (JsonException)
            {
                break;
            }
        }
        return new UiConfiguration(NormalizeLanguage(environmentLanguage ?? "ru"), false);
    }

    private static IEnumerable<string> CandidatePaths(string baseDirectory)
    {
        yield return Path.Combine(baseDirectory, "sandforge.json");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "sandforge.json");
        yield return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "sandforge.json"));
    }

    private static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "en" => "en",
        "auto" => "auto",
        _ => "ru"
    };
}
