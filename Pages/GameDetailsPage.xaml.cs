using BlossomiShymae.Briar;
using Microsoft.Win32;
using ScrimStasis;
using ScrimStasis.DataModels;
using ScrimStasis.Helpers;
using ScrimStasis.ViewModels;
using System.Globalization;
using System.IO;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace ScrimStasis.Pages;

public partial class GameDetailsPage : Page
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _gameId;
    private bool _loaded;
    private string? _rawJson;
    private string? _rawTimelineJson;
    private List<(double Minute, double Diff)> _goldSeries = new();
    private readonly bool _imported;
    private Game? _game;
    private List<TimelineFrame>? _timelineFrames;
    private List<(Participant Blue, Participant Red)> _matchups = new();
    private double _matchupScale = 1000;
    private record KillInfo(long Timestamp, int KillerId, int KillerTeam, double X, double Y, string Tooltip);
    private record KillFilterOption(string Name, int? TeamId = null, int? KillerId = null)
    {
        public bool Matches(int actingTeam, int killerId) =>
            (TeamId is null || actingTeam == TeamId) &&
            (KillerId is null || killerId == KillerId);
    }
    private List<TimelineEventRow> _allEventRows = new();
    private double _eventLimitMs = double.MaxValue;

    private List<KillInfo> _kills = new();

    // In-game coordinate range used by Summoner's Rift positions
    private const double MapMin = -120, MapMax = 14870, MapPx = 512;

    //For slider value
    private int CurrentFrameIndex() =>
    _goldSeries.Count == 0
        ? 0
        : Math.Clamp((int)Math.Round(TimeSlider?.Value ?? 0), 0, _goldSeries.Count - 1);


    public GameDetailsPage(long gameId)
    {
        InitializeComponent();
        _gameId = gameId;
        Loaded += async (_, _) => await LoadAsync();
    }

    // Opened from an imported file
    public GameDetailsPage(string matchJson, string? timelineJson)
    {
        InitializeComponent();
        _imported = true;
        _rawJson = matchJson;
        _rawTimelineJson = timelineJson;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            if (!_imported)
            {
                _rawJson = await LeagueClientData.GetGameDetailsAsync(_gameId);
            }

            var game = JsonSerializer.Deserialize<Game>(_rawJson!, Json)
                ?? throw new InvalidOperationException("The match data is empty.");

            _gameId = game.GameId;

            //Get DataDragon version
            await DataDragon.ResolveVersionAsync(game.GameVersion);

            var viewModel = BuildViewModel(game);
            DataContext = viewModel;
            SetupStatChart(viewModel); 
            
            StatusText.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = true;
            _loaded = true;

            if (_imported)
                ShowSavedTimeline(game);
            else
                await LoadTimelineAsync(game);
        }
    
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't load this game: " + ex.Message;
        }
    }

    //Helper function for adding item slots
    private static List<ItemSlot> ItemSlots(Stats s)
    {
        int[] ids = { s.Item0, s.Item1, s.Item2, s.Item3, s.Item4, s.Item5, s.Item6 };

        return ids
            .Select(id => new ItemSlot(id > 0 
                ? DataDragon.ItemIconUrl(id)
                : null))
            .ToList();
    }

    private static GameDetailsViewModel BuildViewModel(Game game)
    {
        var names = game.ParticipantIdentities
            .GroupBy(i => i.ParticipantId)
            .ToDictionary(g => g.Key, g => g.First().Player);

        var rows = game.Participants.Select(p =>
        {
            names.TryGetValue(p.ParticipantId, out var player);
            var s = p.Stats;

            // Newer accounts use Riot ID (GameName#TagLine); older data uses SummonerName
            var name = !string.IsNullOrWhiteSpace(player?.GameName)
                ? $"{player.GameName}#{player.TagLine}"
                : player?.SummonerName;

            return new PlayerRow
            {
                TeamId = p.TeamId,
                ChampionIconUrl = DataDragon.ChampioIconUrl(p.ChampionId),
                Name = string.IsNullOrWhiteSpace(name) ? $"Player {p.ParticipantId}" : name,
                Level = s.ChampLevel,
                Kda = $"{s.Kills}/{s.Deaths}/{s.Assists}",
                Cs = s.TotalMinionsKilled + s.NeutralMinionsKilled,
                Gold = s.GoldEarned.ToString("N0"),
                Vision = s.VisionScore,
                Damage = s.TotalDamageDealtToChampions,
                DamageText = s.TotalDamageDealtToChampions.ToString("N0"),
                Items = ItemSlots(s),
                RawStats = s,

            };
        }).ToList();

        // Draft

        var drafts = game.Participants
            .OrderBy(p => p.ParticipantId)
            .GroupBy(p => p.TeamId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
        var team = game.Teams.FirstOrDefault(t => t.TeamId == g.Key);
        var result = team?.Win switch
        {
            "Win" => "Victory",
            "Fail" => "Defeat",
            _ => ""
        };

        return new TeamDraft
        {
            TeamId = g.Key,
            Title = $"{(g.Key == 100 ? "Blue side" : "Red side")}" +
                    (result == "" ? "" : $" · {result}"),
            BanIcons = (team?.Bans ?? new())
                .Where(b => b.ChampionId > 0)
                .OrderBy(b => b.PickTurn)
                .Select(b => DataDragon.ChampioIconUrl(b.ChampionId))
                .ToList(),
        };
            }).ToList();

        var date = DateTimeOffset.FromUnixTimeMilliseconds(game.GameCreation).ToLocalTime();

        return new GameDetailsViewModel
        {
            Title = $"{game.GameMode} · {TimeSpan.FromSeconds(game.GameDuration):mm\\:ss}",
            Subtitle = date.ToString("f"),
            Players = rows.OrderBy(r => r.TeamId).ToList(),
            ByDamage = rows.OrderByDescending(r => r.Damage).ToList(),
            MaxDamage = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Damage)),
            Drafts = drafts,                                    // new
        };

    }

    //Stats graph

    private static readonly List<StatOption> StatOptions = new()
{
    new("Damage to champions",        s => s.TotalDamageDealtToChampions),
    new("Physical damage to champions", s => s.PhysicalDamageDealtToChampions),
    new("Magic damage to champions",  s => s.MagicDamageDealtToChampions),
    new("True damage to champions",   s => s.TrueDamageDealtToChampions),
    new("Damage taken",               s => s.TotalDamageTaken),
    new("Damage mitigated",           s => s.DamageSelfMitigated),
    new("Healing done",               s => s.TotalHeal),
    new("Damage to structures",       s => s.DamageDealtToTurrets),
    new("Damage to objectives",       s => s.DamageDealtToObjectives),
    new("Gold earned",                s => s.GoldEarned),
    new("CS (minions + monsters)",    s => s.TotalMinionsKilled + s.NeutralMinionsKilled),
    new("Vision score",               s => s.VisionScore),
    new("Crowd control time",         s => s.TimeCCingOthers, " s"),
};

    private List<PlayerRow> _chartPlayers = new();

    private void SetupStatChart(GameDetailsViewModel vm)
    {
        _chartPlayers = vm.Players;
        StatSelector.ItemsSource = StatOptions;
        StatSelector.SelectedIndex = 0;
    }

    private void StatSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateStatChart();

    private void UpdateStatChart()
    {
        if (StatSelector.SelectedItem is not StatOption option || _chartPlayers.Count == 0)
            return;

        var values = _chartPlayers
            .Select(p => (Player: p, Value: option.Selector(p.RawStats)))
            .ToList();

        double max = Math.Max(1, values.Max(v => v.Value));

        StatChart.ItemsSource = values
            .OrderByDescending(v => v.Value)
            .Select(v => new ChartRow
            {
                ChampionIconUrl = v.Player.ChampionIconUrl,
                Name = v.Player.Name,
                TeamId = v.Player.TeamId,
                Value = v.Value,
                ValueText = $"{v.Value:N0}{option.Suffix}",
                Scale = max,
            })
            .ToList();

        StatChartTitle.Text = option.Name;
    }

    //Timeline

    private async Task LoadTimelineAsync(Game game)
    {
        try
        {
            _rawTimelineJson = await LeagueClientData.GetGameTimelineAsync(game.GameId);

            ApplyTimeline(game, _rawTimelineJson);

        }
        catch (Exception ex)
        {
            TimelineStatus.Text = "Timeline not available: " + ex.Message;
        }
    }

    // When it is imported
    private void ShowSavedTimeline(Game game)
    {
        if (string.IsNullOrWhiteSpace(_rawTimelineJson))
        {
            TimelineStatus.Text = "This save doesn't include timeline data.";
            return;
        }

        try
        {
            ApplyTimeline(game, _rawTimelineJson);
        }
        catch (Exception ex)
        {
            TimelineStatus.Text = "Couldn't read the saved timeline: " + ex.Message;
        }
    }
    private void ApplyTimeline(Game game, string json)
    {
        var timeline = JsonSerializer.Deserialize<GameTimeline>(json, Json);
        if (timeline is null || timeline.Frames.Count == 0)
        {
            TimelineStatus.Text = "No timeline data is available for this game.";
            return;
        }

        BuildTimeline(game, timeline);
        TimelineStatus.Visibility = Visibility.Collapsed;
        TimelinePanel.Visibility = Visibility.Visible;
    }

    private void BuildTimeline(Game game, GameTimeline timeline)
    {
        var blueIds = game.Participants
            .Where(p => p.TeamId == 100)
            .Select(p => p.ParticipantId.ToString())
            .ToHashSet();

        _goldSeries = timeline.Frames.Select(f =>
        {
            double blue = f.ParticipantFrames.Where(kv => blueIds.Contains(kv.Key)).Sum(kv => kv.Value.TotalGold);
            double red = f.ParticipantFrames.Where(kv => !blueIds.Contains(kv.Key)).Sum(kv => kv.Value.TotalGold);
            return (Minute: f.Timestamp / 60000.0, Diff: blue - red);
        }).ToList();

        DrawGoldChart();
        SetupMatchups(game, timeline);
        SetupKillMap(game, timeline);

        var byId = game.Participants.ToDictionary(p => p.ParticipantId);

        _allEventRows = timeline.Frames
            .SelectMany(f => f.Events)
            .OrderBy(ev => ev.Timestamp)
            .Select(ev => ToEventRow(game, byId, ev))
            .OfType<TimelineEventRow>()    
            .ToList();

        RefreshEventsList();
        DrawObjectiveMarkers();
    }

    private void RefreshEventsList()
    {
        if (EventsList is null) return;

        var filter = KillFilter?.SelectedItem as KillFilterOption;

        var shown = _allEventRows
            .Where(r => r.Timestamp <= _eventLimitMs
                        && (filter is null || filter.Matches(r.ActingTeamId, r.KillerId)))
            .ToList();

        EventsList.ItemsSource = shown;
        EventsCountRun.Text = $" · {shown.Count} of {_allEventRows.Count}";
    }

    private static TimelineEventRow? ToEventRow(Game game, Dictionary<int, Participant> byId, TimelineEvent ev)
    {
        if (ev.Type is not ("CHAMPION_KILL" or "ELITE_MONSTER_KILL" or "BUILDING_KILL"))
            return null;

        byId.TryGetValue(ev.KillerId, out var killer);
        byId.TryGetValue(ev.VictimId, out var victim);

        var killerName = killer is null ? "Minions" : ShortName(game, ev.KillerId);
        var time = TimeSpan.FromMilliseconds(ev.Timestamp).ToString(@"m\:ss");

        // Team that performed the action
        var actingTeam = killer?.TeamId
            ?? (ev.Type == "BUILDING_KILL" ? (ev.TeamId == 100 ? 200 : 100) : 0);

        string text;
        switch (ev.Type)
        {
            case "CHAMPION_KILL":
                text = $"{killerName} killed {(victim is null ? "an enemy" : ShortName(game, ev.VictimId))}";
                break;

            case "ELITE_MONSTER_KILL":
                var monster = Pretty(string.IsNullOrEmpty(ev.MonsterSubType) ? ev.MonsterType : ev.MonsterSubType);
                text = $"{killerName} slew {monster}";
                break;

            default: // BUILDING_KILL
                var kind = ev.BuildingType.StartsWith("INHIBITOR") ? "inhibitor" : "tower";
                var lane = string.IsNullOrEmpty(ev.LaneType) ? "" : Pretty(ev.LaneType) + " ";
                text = $"{killerName} destroyed {lane}{kind}";
                break;
        }

        //Icons for objectives

        string? objectiveKind = ev.Type switch
        {
            "BUILDING_KILL" => ev.BuildingType.StartsWith("INHIBITOR") ? "inhibitor" : "tower",
            "ELITE_MONSTER_KILL" => ev.MonsterType switch
            {
                "DRAGON" => "dragon",
                "BARON_NASHOR" => "baron",
                "RIFTHERALD" => "herald",
                "HORDE" => "grubs",
                "ATAKHAN" => "atakhan",
                _ => "objective",
            },
            _ => null,   // CHAMPION_KILL
        };

        return new TimelineEventRow
        {
            Timestamp = ev.Timestamp,
            Time = time,
            Text = text,
            ActorIcon = killer is null ? null : DataDragon.ChampioIconUrl(killer.ChampionId),
            VictimIcon = victim is null ? null : DataDragon.ChampioIconUrl(victim.ChampionId),
            ActingTeamId = actingTeam,
            Kind = objectiveKind,
            ObjectiveIconUrl = DataDragon.ObjectiveIcon(objectiveKind, actingTeam),
            KillerId = ev.KillerId,
        };
    }

    private static string ShortName(Game game, int participantId)
    {
        var player = game.ParticipantIdentities
            .FirstOrDefault(i => i.ParticipantId == participantId)?.Player;

        var name = !string.IsNullOrWhiteSpace(player?.GameName) ? player.GameName : player?.SummonerName;
        return string.IsNullOrWhiteSpace(name) ? $"Player {participantId}" : name;
    }

    private static string Pretty(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' ').ToLowerInvariant());

    // Gold chart 

    private void GoldCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGoldChart();

    private void DrawGoldChart()
    {
        if (GoldCanvas is null) return;
        GoldCanvas.Children.Clear();
        if (_goldSeries.Count < 2 || GoldCanvas.ActualWidth < 50) return;

        double w = GoldCanvas.ActualWidth;
        double h = GoldCanvas.ActualHeight;
        const double left = 52, bottom = 20, top = 8;

        double plotW = w - left - 8;
        double plotH = h - bottom - top;
        double maxMinute = _goldSeries.Max(p => p.Minute);
        double maxAbs = Math.Max(1000, _goldSeries.Max(p => Math.Abs(p.Diff)));

        double X(double minute) => left + minute / maxMinute * plotW;
        double Y(double diff) => top + plotH / 2 - diff / maxAbs * (plotH / 2);

        void Label(string text, double x, double y, Brush? brush = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 11, Foreground = brush ?? Brushes.Gray };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            GoldCanvas.Children.Add(tb);
        }

        GoldCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left + plotW,
            Y1 = Y(0),
            Y2 = Y(0),
            Stroke = Brushes.Gray,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        });

        Label($"+{maxAbs:N0}", 4, top - 2, Brushes.CornflowerBlue);
        Label("0", 4, Y(0) - 8);
        Label($"+{maxAbs:N0}", 4, top + plotH - 14, Brushes.IndianRed);

        for (int m = 0; m <= maxMinute; m += 5)
            Label($"{m}m", X(m) - 8, h - bottom + 3);


        // ---- Gold line ----
        int cur = CurrentFrameIndex();

        var past = new Polyline
        {
            Stroke = Brushes.MediumPurple,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var future = new Polyline
        {
            Stroke = Brushes.MediumPurple,
            StrokeThickness = 2,
            Opacity = 0.3,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        };

        for (int i = 0; i < _goldSeries.Count; i++)
        {
            var pt = new Point(X(_goldSeries[i].Minute), Y(_goldSeries[i].Diff));
            if (i <= cur) past.Points.Add(pt);
            if (i >= cur) future.Points.Add(pt);     // shares the current point so the line stays connected
        }


        GoldCanvas.Children.Add(future);       
        GoldCanvas.Children.Add(past);

        // ---- Marker for the slider's current moment ----
        var now = _goldSeries[cur];
        double nx = X(now.Minute), ny = Y(now.Diff);

        GoldCanvas.Children.Add(new Line
        {
            X1 = nx,
            X2 = nx,
            Y1 = top,
            Y2 = top + plotH,
            Stroke = Brushes.MediumPurple,
            StrokeThickness = 1.5,
        });

        var markerDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = now.Diff >= 0 ? Brushes.CornflowerBlue : Brushes.IndianRed,
            Stroke = Brushes.White,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(markerDot, nx - 6);
        Canvas.SetTop(markerDot, ny - 6);
        GoldCanvas.Children.Add(markerDot);

        var lead = now.Diff switch
        {
            > 0 => $"Blue +{now.Diff:N0}",
            < 0 => $"Red +{-now.Diff:N0}",
            _ => "Even",
        };
        var nowLabel = new TextBlock
        {
            Text = $"{TimeSpan.FromMinutes(now.Minute):m\\:ss} · {lead}",
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x22, 0x22, 0x22)),
            Padding = new Thickness(4, 1, 4, 1),
        };
        nowLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lx = nx + 6 + nowLabel.DesiredSize.Width > w ? nx - 6 - nowLabel.DesiredSize.Width : nx + 6;
        Canvas.SetLeft(nowLabel, lx);
        Canvas.SetTop(nowLabel, top);
        GoldCanvas.Children.Add(nowLabel);
    }

    //Matchups gold
    private void SetupMatchups(Game game, GameTimeline timeline)
    {
        var blue = game.Participants.Where(p => p.TeamId == 100).OrderBy(p => p.ParticipantId);
        var red = game.Participants.Where(p => p.TeamId == 200).OrderBy(p => p.ParticipantId);

        _game = game;
        _matchups = blue.Zip(red, (b, r) => (Blue: b, Red: r)).ToList();
        _timelineFrames = timeline.Frames;

        if (_matchups.Count == 0 || _timelineFrames.Count == 0) return;

        // Fixed scale so the bars are comparable as the slider moves
        _matchupScale = Math.Max(1000, _timelineFrames.Max(f =>
            _matchups.Max(m => Math.Abs(GoldAt(f, m.Blue.ParticipantId) - GoldAt(f, m.Red.ParticipantId)))));

        TimeSlider.Maximum = _timelineFrames.Count - 1;
        TimeSlider.Value = _timelineFrames.Count - 1;   // start at the end of the game
        UpdateMatchups();
    }

    private static int GoldAt(TimelineFrame frame, int participantId) =>
        frame.ParticipantFrames.TryGetValue(participantId.ToString(), out var pf) ? pf.TotalGold : 0;

    private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshKillMap();
        UpdateMatchups();
        DrawGoldChart();
    }
        

    private void UpdateMatchups()
    {
        // This also runs while the XAML loads, before anything is set up
        if (_game is null || _timelineFrames is null || _timelineFrames.Count == 0 || _matchups.Count == 0)
            return;

        int index = Math.Clamp((int)Math.Round(TimeSlider.Value), 0, _timelineFrames.Count - 1);
        var frame = _timelineFrames[index];

        var rows = _matchups.Select(m => new MatchupRow
        {
            BlueIcon = DataDragon.ChampioIconUrl(m.Blue.ChampionId),
            BlueName = ShortName(_game, m.Blue.ParticipantId),
            RedIcon = DataDragon.ChampioIconUrl(m.Red.ChampionId),
            RedName = ShortName(_game, m.Red.ParticipantId),
            Diff = GoldAt(frame, m.Blue.ParticipantId) - GoldAt(frame, m.Red.ParticipantId),
            Scale = _matchupScale,
        }).ToList();

        MatchupList.ItemsSource = rows;

        SliderTimeText.Text = TimeSpan.FromMilliseconds(frame.Timestamp).ToString(@"m\:ss");

        var total = rows.Sum(r => r.Diff);
        TotalText.Text = total switch
        {
            > 0 => $"Team total: blue ahead by {total:N0} gold",
            < 0 => $"Team total: red ahead by {-total:N0} gold",
            _ => "Team total: even",
        };
    }
    //Kill Map
    private void SetupKillMap(Game game, GameTimeline timeline)
    {
        // Positions in the timeline use Summoner's Rift coordinates
        if (game.MapId is not (0 or 11))
        {
            KillMapPanel.Visibility = Visibility.Collapsed;
            MapColumn.Width = new GridLength(0);                       
            return;
        }
        KillMapPanel.Visibility = Visibility.Visible;
        MapColumn.Width = new GridLength(1.2, GridUnitType.Star);

        var byId = game.Participants.ToDictionary(p => p.ParticipantId);

        _kills = timeline.Frames
            .SelectMany(f => f.Events)
            .Where(ev => ev.Type == "CHAMPION_KILL" && ev.Position is not null)
            .OrderBy(ev => ev.Timestamp)
            .Select(ev =>
            {
                byId.TryGetValue(ev.KillerId, out var killer);
                byId.TryGetValue(ev.VictimId, out var victim);

                // If there's no killer (executed by minions or a turret), the killer team is the opposite of the victim's
                int killerTeam = killer?.TeamId ?? (victim?.TeamId == 100 ? 200 : 100);

                var time = TimeSpan.FromMilliseconds(ev.Timestamp).ToString(@"m\:ss");
                var victimName = victim is null ? "an enemy" : ShortName(game, ev.VictimId);
                var tip = killer is null
                    ? $"{time} · {victimName} was executed"
                    : $"{time} · {ShortName(game, ev.KillerId)} killed {victimName}";

                return new KillInfo(ev.Timestamp, ev.KillerId, killerTeam, ev.Position!.X, ev.Position.Y, tip);
            })
            .ToList();

        // Map image
        MapImage.Source = DataDragon.MapIconUrl();


        // Time slider
        //KillTimeSlider.Maximum = Math.Max(1, Math.Ceiling(timeline.Frames.Max(f => f.Timestamp) / 60000.0));
        //KillTimeSlider.Value = KillTimeSlider.Maximum;

        // Filters: everyone, each team, each player
        var filters = new List<KillFilterOption>
        {
            new("All players"),
            new("Blue team", TeamId: 100),
            new("Red team", TeamId: 200),
        };

        foreach (var p in game.Participants.OrderBy(p => p.TeamId).ThenBy(p => p.ParticipantId))
        {
            var side = p.TeamId == 100 ? "blue" : "red";
            filters.Add(new($"{ShortName(game, p.ParticipantId)} ({side})", KillerId: p.ParticipantId));
        }

        KillFilter.ItemsSource = filters;
        KillFilter.SelectedIndex = 0;
        RefreshKillMap();
    }

    private void KillFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshKillMap();
        RefreshEventsList();  
    }
    private void KillTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => RefreshKillMap();

    private void RefreshKillMap()
    {
        // These handlers can fire while the XAML is still loading
        if (KillFilter?.SelectedItem is not KillFilterOption filter || KillMarkers is null)
            return;

        double limitMs = TimeSlider.Value * 60000;
        _eventLimitMs = limitMs;          // new
        RefreshEventsList();              // new
        KillTimeText.Text = $"Up to {TimeSlider.Value:0} min";

        var shown = _kills
            .Where(k => k.Timestamp <= limitMs && filter.Matches(k.KillerTeam, k.KillerId))
            .ToList();

        KillMarkers.Children.Clear();
        foreach (var k in shown)
        {
            double px = (k.X - MapMin) / (MapMax - MapMin) * MapPx;
            double py = MapPx - (k.Y - MapMin) / (MapMax - MapMin) * MapPx;   // game Y grows upward

            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = k.KillerTeam == 100 ? Brushes.CornflowerBlue : Brushes.IndianRed,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Opacity = 0.9,
                ToolTip = k.Tooltip,
            };
            ToolTipService.SetInitialShowDelay(dot, 100);

            Canvas.SetLeft(dot, px - 7);
            Canvas.SetTop(dot, py - 7);
            KillMarkers.Children.Add(dot);
        }

        KillCountRun.Text = shown.Count == 1 ? "1 kill shown" : $"{shown.Count} kills shown";
    }

    //Icons for slider

    private void TimeSlider_SizeChanged(object sender, SizeChangedEventArgs e) => DrawObjectiveMarkers();

    // Maps a timestamp (ms) to the slider's value, which is a frame index
    private double TimestampToSliderValue(long ts)
    {
        var frames = _timelineFrames!;
        if (ts <= frames[0].Timestamp) return 0;

        for (int i = 0; i < frames.Count - 1; i++)
        {
            long a = frames[i].Timestamp, b = frames[i + 1].Timestamp;
            if (ts >= a && ts <= b)
                return b == a ? i : i + (ts - a) / (double)(b - a);
        }
        return frames.Count - 1;
    }

    // Where the slider's thumb can actually travel
    private bool TryGetSliderGeometry(out double start, out double length)
    {
        start = length = 0;
        if (!TimeSlider.IsLoaded) return false;

        TimeSlider.ApplyTemplate();
        if (TimeSlider.Template?.FindName("PART_Track", TimeSlider) is not Track track
            || track.ActualWidth <= 0)
            return false;

        double thumb = track.Thumb?.ActualWidth ?? 0;
        var origin = track.TransformToAncestor(TimeSlider).Transform(new Point(0, 0));

        start = origin.X + thumb / 2;
        length = track.ActualWidth - thumb;
        return length > 0;
    }

    private void DrawObjectiveMarkers()
    {
        if (ObjectiveMarkers is null) return;
        ObjectiveMarkers.Children.Clear();

        if (_timelineFrames is null || _timelineFrames.Count < 2 || TimeSlider.Maximum <= 0) return;
        if (!TryGetSliderGeometry(out var start, out var length)) return;

        const double size = 24, gap = 2;

        var markers = _allEventRows
            .Where(r => r.Kind is not null)                 
            .OrderBy(r => r.Timestamp)
            .Select(r =>
            {
                double value = TimestampToSliderValue(r.Timestamp);
                return (Row: r, Value: value, X: start + value / TimeSlider.Maximum * length);
            })
            .ToList();

        // Stack markers that would overlap into extra rows
        var rowRight = new List<double>();
        var placed = new List<(TimelineEventRow Row, double Value, double X, int Layer)>();

        foreach (var m in markers)
        {
            int layer = rowRight.FindIndex(right => m.X - size / 2 >= right + gap);
            if (layer < 0)
            {
                rowRight.Add(0);
                layer = rowRight.Count - 1;
            }
            rowRight[layer] = m.X + size / 2;
            placed.Add((m.Row, m.Value, m.X, layer));
        }

        int layers = Math.Max(1, rowRight.Count);
        ObjectiveMarkers.Height = layers * (size + gap);

        foreach (var m in placed)
        {
            var el = CreateObjectiveMarker(m.Row, m.Value);
            Canvas.SetLeft(el, m.X - size / 2);
            Canvas.SetTop(el, ObjectiveMarkers.Height - (m.Layer + 1) * (size + gap));  // layer 0 sits nearest the slider
            ObjectiveMarkers.Children.Add(el);
        }
    }

    private FrameworkElement CreateObjectiveMarker(TimelineEventRow row, double sliderValue)
    {
        var teamBrush = row.ActingTeamId == 200 ? Brushes.IndianRed : Brushes.CornflowerBlue;

        TextBlock Letter() => new()
        {
            Text = DataDragon.ObjectiveLetter(row.Kind),
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            BorderBrush = teamBrush,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
            Cursor = Cursors.Hand,
            ToolTip = $"{row.Time} · {row.Text}",
        };
        ToolTipService.SetInitialShowDelay(badge, 100);

        if (row.ObjectiveIconUrl is { } url)
        {
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
            image.ImageFailed += (_, _) =>
            {
                badge.Background = teamBrush;
                badge.Child = Letter();
            };
            image.Source = new BitmapImage(new Uri(url));
            badge.Child = image;
        }
        else
        {
            badge.Background = teamBrush;
            badge.Child = Letter();
        }

        badge.MouseLeftButtonUp += (_, _) => TimeSlider.Value = Math.Round(sliderValue);

        return badge;
    }

    //Buttons
    private async void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        if (_rawJson is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Save game JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"lol-game-{_gameId}.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            using var matchDoc = JsonDocument.Parse(_rawJson);
            using var timelineDoc = _rawTimelineJson is null ? null : JsonDocument.Parse(_rawTimelineJson);

            var bundle = new { match = matchDoc.RootElement, timeline = timelineDoc?.RootElement };

            var pretty = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            await File.WriteAllTextAsync(dialog.FileName, pretty);

            MessageBox.Show($"Saved to:\n{dialog.FileName}", "Game saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't save the file:\n" + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => NavigationService?.GoBack();
}
