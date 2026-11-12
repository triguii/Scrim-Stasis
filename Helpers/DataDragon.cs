using System.Drawing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows.Media.Imaging;

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

    public static string? ObjectiveIcon(string? kind, int team)
    {

        return $"https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-match-history/global/default/{kind}-{team}.png";
    }

    public static string ObjectiveLetter(string? kind) => kind switch
    {
        "baron" => "B",
        "dragon" => "D",
        "herald" => "H",
        "grubs" => "V",
        "tower" => "T",
        "inhibitor" => "I",
        "atakhan" => "A",
        _ => "•",
    };

    public static string ItemIconUrl(int itemId) =>
        $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/item/{itemId}.png";
    public static string ChampioIconUrl(int championId) =>
            $"https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/champion-icons/{championId}.png";

    public static BitmapImage MapIconUrl() =>
        new BitmapImage(new Uri( $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/map/map11.png"));
}
