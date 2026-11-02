using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class SettingsStoreTests
{
    private const uint PaletteTestVirtualKey = 0x4B;

    internal static void LoadReturnsDefaultsWhenNoFileExists()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));

        var result = store.Load();

        TestAssert.Equal(WidgetSettings.Default, result.Settings);
        TestAssert.Equal(null, result.Warning);
    }

    internal static void SaveThenLoadRoundTripsSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("nested", "settings.json"));
        var settings = WidgetSettings.Default with
        {
            ShowSeconds = !WidgetSettings.Default.ShowSeconds,
            PasswordLength = 32,
            RunAtStartup = false,
            OpenScreenshotEditorAfterCapture = false,
            KeepScreenshotHistory = false,
            ScreenshotEditor = new ScreenshotEditorOptions { Color = "#FF00FFFF", StrokeWidth = 8, FontSize = 32, BlurRadius = 24 }
        };

        store.Save(settings);
        var result = store.Load();

        TestAssert.Equal(settings.ShowSeconds, result.Settings.ShowSeconds);
        TestAssert.Equal(32, result.Settings.PasswordLength);
        TestAssert.False(result.Settings.RunAtStartup);
        TestAssert.False(result.Settings.OpenScreenshotEditorAfterCapture);
        TestAssert.False(result.Settings.KeepScreenshotHistory);
        TestAssert.Equal(settings.ScreenshotEditor, result.Settings.ScreenshotEditor);
        TestAssert.Equal(null, result.Warning);
    }

    internal static void LoadQuarantinesCorruptJsonAndReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        File.WriteAllText(settingsPath, "{ not json");
        var store = new SettingsStore(settingsPath);

        var result = store.Load();

        TestAssert.Equal(WidgetSettings.Default, result.Settings);
        TestAssert.True(result.Warning is not null);
        TestAssert.False(File.Exists(settingsPath));
        TestAssert.True(File.Exists(settingsPath + ".corrupt"));
    }

    internal static void LoadTreatsJsonNullAsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        File.WriteAllText(settingsPath, "null");
        var store = new SettingsStore(settingsPath);

        var result = store.Load();

        TestAssert.Equal(WidgetSettings.Default, result.Settings);
        TestAssert.Equal(null, result.Warning);
    }

    internal static void LoadDefaultsStartupToEnabledForExistingSettings()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        File.WriteAllText(settingsPath, "{\"ShowSeconds\":false}");

        var result = new SettingsStore(settingsPath).Load();

        TestAssert.True(WidgetSettings.Default.RunAtStartup);
        TestAssert.True(result.Settings.RunAtStartup);
        TestAssert.False(result.Settings.ShowSeconds);
    }

    internal static void LoadRestoresMissingOrNullScreenshotOptions()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");

        foreach (var json in new[] { "{}", "{\"ScreenshotEditor\":null}" })
        {
            File.WriteAllText(settingsPath, json);
            var result = new SettingsStore(settingsPath).Load();

            TestAssert.True(result.Settings.OpenScreenshotEditorAfterCapture);
            TestAssert.True(result.Settings.KeepScreenshotHistory);
            TestAssert.Equal(new ScreenshotEditorOptions(), result.Settings.ScreenshotEditor);
            TestAssert.Equal<string?>(null, result.Warning);
        }
    }

    internal static void PaletteShortcutPersistsAndOlderSettingsUseTheDefault()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        var store = new SettingsStore(settingsPath);
        var palette = new GlobalShortcut { Modifiers = GlobalShortcut.ControlModifier, VirtualKey = PaletteTestVirtualKey };
        store.Save(WidgetSettings.Default with { SpotlightShortcuts = new SpotlightShortcuts { Palette = palette } });

        TestAssert.Equal(palette, store.Load().Settings.SpotlightShortcuts.Palette);

        foreach (var json in new[] { "{}", "{\"SpotlightShortcuts\":null}", "{\"SpotlightShortcuts\":{}}", "{\"SpotlightShortcuts\":{\"Palette\":null}}", "{\"SpotlightShortcuts\":{\"Palette\":{}}}" })
        {
            File.WriteAllText(settingsPath, json);
            TestAssert.Equal(SpotlightShortcuts.DefaultPalette, store.Load().Settings.SpotlightShortcuts.Palette);
        }
    }

    internal static void SpotlightPreferencesPersistAndLegacySettingsUseDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("settings.json");
        var store = new SettingsStore(path);
        var preferences = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.Uuid, SpotlightActionIds.Password],
            RecentActionIds = [SpotlightActionIds.FormatJson, SpotlightActionIds.Uuid]
        };
        store.Save(WidgetSettings.Default with { SpotlightPreferences = preferences });
        var saved = store.Load().Settings.SpotlightPreferences;
        TestAssert.SequenceEqual(preferences.FavoriteActionIds, saved.FavoriteActionIds);
        TestAssert.SequenceEqual(preferences.RecentActionIds, saved.RecentActionIds);

        foreach (var json in new[] { "{}", "{\"SpotlightPreferences\":null}", "{\"SpotlightPreferences\":{\"FavoriteActionIds\":null,\"RecentActionIds\":null}}" })
        {
            File.WriteAllText(path, json);
            var defaults = store.Load().Settings.SpotlightPreferences;
            TestAssert.True(defaults.RememberRecentCommands);
            TestAssert.Empty(defaults.FavoriteActionIds);
            TestAssert.Empty(defaults.RecentActionIds);
        }
    }
}
