using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace LeagueDataParser.DataModels;


public class MatchHistoryResponse
{
    [JsonPropertyName("games")] public GamesWrapper Games { get; set; } = new();
}

public class GamesWrapper
{
    [JsonPropertyName("games")] public List<Game> Games { get; set; } = new();
}
public class Summoner
{
    public ulong SummonerId { get; set; }
    public ulong AccountId { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string InternalName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public uint SummonerLevel { get; set; }

}

public class Game
{
    public long GameId { get; set; }
    public long GameCreation { get; set; }  
    public int GameDuration { get; set; }    // seconds
    public string GameMode { get; set; } = string.Empty;
    public List<Participant> Participants { get; set; } = new();
    public List<ParticipantIdentity> ParticipantIdentities { get; set; } = new();
    public List<Team> Teams { get; set; } = new();

    public string GameVersion { get; set; } = string.Empty;



}

public class Participant
{
    public int ChampionId { get; set; }
    public Stats Stats { get; set; } = new();
    public int ParticipantId { get; set; }     
    public int TeamId { get; set; }           
}

public class Stats
{
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public bool Win { get; set; }
    public int ChampLevel { get; set; }                       
    public int GoldEarned { get; set; }                       
    public int TotalMinionsKilled { get; set; }               
    public int NeutralMinionsKilled { get; set; }             
    public int TotalDamageDealtToChampions { get; set; }      
    public int TotalDamageTaken { get; set; }                 
    public int VisionScore { get; set; }
    public int Item0 { get; set; }
    public int Item1 { get; set; }
    public int Item2 { get; set; }
    public int Item3 { get; set; }
    public int Item4 { get; set; }
    public int Item5 { get; set; }
    public int Item6 { get; set; }   // trinket
}

public class ParticipantIdentity                              
{
    public int ParticipantId { get; set; }
    public PlayerInfo Player { get; set; } = new();
}

public class PlayerInfo                                       
{
    public string SummonerName { get; set; } = "";
    public string GameName { get; set; } = "";
    public string TagLine { get; set; } = "";
}

public class Team                                          
{
    public int TeamId { get; set; }                         // 100 = blue, 200 = red
    public string Win { get; set; } = "";                   // "Win" / "Fail"
    public List<Ban> Bans { get; set; } = new();
}

public class Ban                                            
{
    public int ChampionId { get; set; }                     // 0 or -1 means no ban
    public int PickTurn { get; set; }
}

public class GameTimeline
{
    public int FrameInterval { get; set; }
    public List<TimelineFrame> Frames { get; set; } = new();
}

public class TimelineFrame
{
    public long Timestamp { get; set; }   // ms
    public Dictionary<string, TimelineParticipantFrame> ParticipantFrames { get; set; } = new();
    public List<TimelineEvent> Events { get; set; } = new();
}

public class TimelineParticipantFrame
{
    public int ParticipantId { get; set; }
    public int TotalGold { get; set; }
    public int Level { get; set; }
}

public class TimelineEvent
{
    public string Type { get; set; } = "";
    public long Timestamp { get; set; }
    public int KillerId { get; set; }          // 0 = minions / environment
    public int VictimId { get; set; }
    public int TeamId { get; set; }            // for buildings: the team that LOST the building
    public string MonsterType { get; set; } = "";
    public string MonsterSubType { get; set; } = "";
    public string BuildingType { get; set; } = "";
    public string LaneType { get; set; } = "";
}
