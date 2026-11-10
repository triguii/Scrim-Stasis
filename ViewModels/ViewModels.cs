using ScrimStasis.DataModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace ScrimStasis.ViewModels
{
    public record ItemSlot(string? IconUrl);

    public class RecentGameItem
    {
        public long GameId { get; init; }
        public string ChampionIconUrl { get; init; } = "";
        public string Kda { get; init; } = "";
        public string Result { get; init; } = "";
        public string GameMode { get; init; } = "";
        public string Duration { get; init; } = "";
    }

    public class PlayerRow
    {
        public int TeamId { get; init; }
        public bool IsBlueTeam => TeamId == 100;
        public string ChampionIconUrl { get; init; } = "";
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public string Kda { get; init; } = "";
        public int Cs { get; init; }
        public string Gold { get; init; } = "";
        public string DamageTaken { get; init; } = "";
        public int Vision { get; init; }
        public double Damage { get; init; }
        public string DamageText { get; init; } = "";
        public List<ItemSlot> Items { get; init; } = new();
        public Stats RawStats { get; init; } = new(); 


    }

    public class GameDetailsViewModel
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public List<PlayerRow> Players { get; init; } = new();  
        public List<PlayerRow> ByDamage { get; init; } = new();   
        public double MaxDamage { get; init; }
        public List<TeamDraft> Drafts { get; init; } = new();
    }

    public class TeamDraft
    {
        public int TeamId { get; init; }
        public bool IsBlueTeam => TeamId == 100;
        public string Title { get; init; } = "";
        public List<string> BanIcons { get; init; } = new();
        public bool HasBans => BanIcons.Count > 0;
    }

    public class TimelineEventRow
    {
        public string Time { get; init; } = "";
        public string Text { get; init; } = "";
        public string? ActorIcon { get; init; }
        public string? VictimIcon { get; init; }
        public int ActingTeamId { get; init; }
        public bool IsBlueTeam => ActingTeamId == 100;
    }

    public class MatchupRow
    {
        public string BlueIcon { get; init; } = "";
        public string BlueName { get; init; } = "";
        public string RedIcon { get; init; } = "";
        public string RedName { get; init; } = "";
        public double Diff { get; init; }          // blue minus red
        public double Scale { get; init; }

        public double BlueLead => Math.Max(Diff, 0);
        public double RedLead => Math.Max(-Diff, 0);
        public int Leader => Diff > 0 ? 1 : Diff < 0 ? -1 : 0;
        public string DiffText => Diff.ToString("+#,##0;-#,##0;0");
    }

    public record StatOption(string Name, Func<Stats, double> Selector, string Suffix = "");

    public class ChartRow
    {
        public string ChampionIconUrl { get; init; } = "";
        public string Name { get; init; } = "";
        public int TeamId { get; init; }
        public bool IsBlueTeam => TeamId == 100;
        public double Value { get; init; }
        public string ValueText { get; init; } = "";
        public double Scale { get; init; } = 1;
    }
}
