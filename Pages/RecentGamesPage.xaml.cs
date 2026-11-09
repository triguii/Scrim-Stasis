using LeagueDataParser.Helpers;
using LeagueDataParser.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LeagueDataParser.Pages;

public partial class RecentGamesPage : Page
{
    public ObservableCollection<RecentGameItem> Games { get; } = new();

    public RecentGamesPage()
    {
        InitializeComponent();
        DataContext = this;
        LoadPageAsync();
    }

    private async void LoadPageAsync()
    {
        Games.Clear();
        foreach (var g in await LeagueClientData.GetRecentGamesAsync())
            Games.Add(g);
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentGamesList.SelectedItem is not RecentGameItem game) return;

        NavigationService.Navigate(new GameDetailsPage(game.GameId));

        RecentGamesList.SelectedItem = null;
    }

    private async void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import saved game",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var text = await File.ReadAllTextAsync(dialog.FileName);
            var (matchJson, timelineJson) = SavedGameFile.Parse(text);

            NavigationService?.Navigate(new GameDetailsPage(matchJson, timelineJson));
        }
        catch (JsonException)
        {
            MessageBox.Show("That file isn't valid JSON.", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't import the file:\n" + ex.Message, "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
