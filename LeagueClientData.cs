using BlossomiShymae.Briar;
using BlossomiShymae.Briar.Rest;
using LeagueDataParser.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LeagueDataParser.DataModels;
using LeagueDataParser.Helpers;

namespace LeagueDataParser;
public static class LeagueClientData
{
    private const int DELAY_TIME = 1000;
    private static LcuHttpClient client = Connector.GetLcuHttpClientInstance();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Summoner currentSummoner = new Summoner();


    public static async Task GetCurrentSummonerAsync()
    {
        var response = await client.SendAsync(new(HttpMethod.Get, "/lol-summoner/v1/current-summoner"));

        currentSummoner = await response.Content.ReadFromJsonAsync<Summoner>()
            ?? throw new Exception("Failed to get summoner");

        await Task.Delay(DELAY_TIME);
    }

    public static async Task<List<RecentGameItem>> GetRecentGamesAsync()
    {

        var data = await client.GetFromJsonAsync<MatchHistoryResponse>(
            "/lol-match-history/v1/products/lol/current-summoner/matches", Json);


        return data?.Games.Games
            .Where(g => g.Participants.Count > 0)
            .Select(g =>
            {
                var p = g.Participants[0];
                return new RecentGameItem
                {
                    GameId = g.GameId,
                    ChampionIconUrl = DataDragon.ChampioIconUrl(p.ChampionId),
                    Kda = $"{p.Stats.Kills}/{p.Stats.Deaths}/{p.Stats.Assists}",
                    Result = p.Stats.Win ? "Victory" : "Defeat",
                    GameMode = g.GameMode,
                    Duration = TimeSpan.FromSeconds(g.GameDuration).ToString(@"mm\:ss"),
                };
            }).ToList() ?? new();
    }

    public async static Task<String> GetGameDetailsAsync(long gameId)
    {

        var rawJson = await client.GetStringAsync($"/lol-match-history/v1/games/{gameId}")
                        ?? throw new InvalidOperationException("Game not found");           

        //We return the raw Json instead of the game data object to be able to save it to disk
        return rawJson;

    }

    public async static Task<String> GetGameTimelineAsync(long gameId)
    {

        var rawJson = await client.GetStringAsync(
                $"/lol-match-history/v1/game-timelines/{gameId}");

        //We return the raw Json instead of the game data object to be able to save it to disk
        return rawJson;

    }
}

