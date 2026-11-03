using System.Windows;
using System.Windows.Controls;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static partial class SettingsWindowTests
{
    internal static void SpotlightSettingsSavePreservesNewerFavoritesAndHistory() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var initial = WidgetSettings.Default with
        {
            SpotlightPreferences = new SpotlightPreferences { FavoriteActionIds = [SpotlightActionIds.Uuid] }
        };
        var latest = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.Password],
            RecentActionIds = [SpotlightActionIds.Base64Encode, SpotlightActionIds.FormatJson]
        };
        var window = new SettingsWindow(store, initial, _ => null, _ => { }, () => { }, _ => null,
            getSpotlightPreferences: () => latest);

        try
        {
            window.Show();
            SaveSettings(window);
            var saved = store.Load().Settings.SpotlightPreferences;
            TestAssert.SequenceEqual(latest.FavoriteActionIds, saved.FavoriteActionIds);
            TestAssert.SequenceEqual(latest.RecentActionIds, saved.RecentActionIds);
            TestAssert.True(saved.RememberRecentCommands);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void ClearingSpotlightHistoryRequiresSaveAndKeepsFavorites() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var initial = WidgetSettings.Default with
        {
            SpotlightPreferences = new SpotlightPreferences
            {
                FavoriteActionIds = [SpotlightActionIds.Uuid],
                RecentActionIds = [SpotlightActionIds.Password]
            }
        };
        store.Save(initial);

        foreach (var save in new[] { false, true })
        {
            var window = new SettingsWindow(store, initial, _ => null, _ => { }, () => { }, _ => null);

            try
            {
                window.Show();
                window.ClearSpotlightHistoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.False(window.ClearSpotlightHistoryButton.IsEnabled);
                TestAssert.SequenceEqual(initial.SpotlightPreferences.RecentActionIds, store.Load().Settings.SpotlightPreferences.RecentActionIds);

                if (save)
                {
                    SaveSettings(window);
                }

                var saved = store.Load().Settings.SpotlightPreferences;
                TestAssert.SequenceEqual(initial.SpotlightPreferences.FavoriteActionIds, saved.FavoriteActionIds);
                TestAssert.Equal(save ? 0 : 1, saved.RecentActionIds.Length);
            }
            finally
            {
                window.Close();
            }
        }
    });

    internal static void DisablingSpotlightHistoryPersistsAndClearsRecentCommands() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var initial = WidgetSettings.Default with
        {
            SpotlightPreferences = new SpotlightPreferences
            {
                FavoriteActionIds = [SpotlightActionIds.Uuid],
                RecentActionIds = [SpotlightActionIds.Password]
            }
        };
        var window = new SettingsWindow(store, initial, _ => null, _ => { }, () => { }, _ => null);

        try
        {
            window.Show();
            window.RememberRecentCommandsCheckBox.IsChecked = false;
            SaveSettings(window);
            var saved = store.Load().Settings.SpotlightPreferences;
            TestAssert.False(saved.RememberRecentCommands);
            TestAssert.Empty(saved.RecentActionIds);
            TestAssert.SequenceEqual(initial.SpotlightPreferences.FavoriteActionIds, saved.FavoriteActionIds);
        }
        finally
        {
            window.Close();
        }
    });
}
