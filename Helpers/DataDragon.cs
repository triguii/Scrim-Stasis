using System.Net.Http;
using System.Net.Http.Json;

namespace ScrimStasis.Helpers;

public static class DataDragon
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static List<string>? _versions;
    private static string _latestVersion = string.Empty;

    public static async Task<string?> ResolveVersionAsync(string? gameVersion)
    {
        try
        {
            _versions ??= await Http.GetFromJsonAsync<List<string>>(
                "https://ddragon.leagueoflegends.com/api/versions.json");

            if (_versions is null || _versions.Count == 0) return null;

            var parts = gameVersion?.Split('.');
            if (parts is { Length: >= 2 })
            {
                var prefix = $"{parts[0]}.{parts[1]}.";
                var match = _versions.FirstOrDefault(v => v.StartsWith(prefix));
                if (match is not null)
                {
                    _latestVersion = _versions[0];

                    return match;
                }
            }

            _latestVersion = _versions[0];

            return _versions[0]; // latest
        }
        catch
        {
            return null;
        }
    }

    public static string ItemIconUrl(int itemId) =>
        $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/item/{itemId}.png";
    public static string ChampioIconUrl(int championId) =>
            $"https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/{championId}.png";
}
