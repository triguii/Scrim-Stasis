using BlossomiShymae.Briar;
using ScrimStasis;
using ScrimStasis.ViewModels;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.Win32;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Shapes;
using ScrimStasis.Helpers;
using ScrimStasis.DataModels;

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

            await DataDragon.ResolveVersionAsync(game.GameVersion);

            DataContext = BuildViewModel(game);
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
                DamageTaken = s.TotalDamageTaken.ToString("N0"),
                Vision = s.VisionScore,
                Damage = s.TotalDamageDealtToChampions,
                DamageText = s.TotalDamageDealtToChampions.ToString("N0"),
                Items = ItemSlots(s),

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

        var byId = game.Participants.ToDictionary(p => p.ParticipantId);

        EventsList.ItemsSource = timeline.Frames
            .SelectMany(f => f.Events)
            .OrderBy(ev => ev.Timestamp)
            .Select(ev => ToEventRow(game, byId, ev))
            .Where(r => r is not null)
            .ToList();
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

        return new TimelineEventRow
        {
            Time = time,
            Text = text,
            ActorIcon = killer is null ? null : DataDragon.ChampioIconUrl(killer.ChampionId),
            VictimIcon = victim is null ? null : DataDragon.ChampioIconUrl(victim.ChampionId),
            ActingTeamId = actingTeam,
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


        var line = new Polyline
        {
            Stroke = Brushes.MediumPurple,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        foreach (var p in _goldSeries)
            line.Points.Add(new Point(X(p.Minute), Y(p.Diff)));

        GoldCanvas.Children.Add(line);
    }

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
        => UpdateMatchups();

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
