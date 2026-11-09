using System.IO;
using System.Text.Json;

namespace LeagueDataParser.Helpers;

public static class SavedGameFile
{

    public static (string MatchJson, string? TimelineJson) Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("This file doesn't contain a saved League game.");

        // Newer format: match + timeline bundle
        if (root.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object)
        {
            string? timeline = root.TryGetProperty("timeline", out var t) && t.ValueKind == JsonValueKind.Object
                ? t.GetRawText()
                : null;

            return (match.GetRawText(), timeline);
        }


        throw new InvalidDataException("Invalid file");
    }
}