using System.Windows;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static partial class SettingsWindowTests
{
    internal static void BatterySettingsNavigateSaveAndReload() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var window = CreateStartupSettingsWindow(store, _ => null);
        try
        {
            window.Show();
            window.SettingsNavigationListBox.SelectedIndex = 2;
            TestAssert.Equal(Visibility.Visible, window.BatterySettingsPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.WidgetSettingsPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.ScreenshotSettingsPanel.Visibility);
            TestAssert.Equal((double)BatteryAlertSettings.DefaultThresholdPercent, window.BatteryAlertThresholdSlider.Value);
            window.BatteryAlertThresholdSlider.Value = BatteryAlertSettings.MaximumThresholdPercent;
            window.BatteryAlertsEnabledCheckBox.IsChecked = false;
            UpdateBindingsAndLayout(window);
            TestAssert.False(window.BatteryAlertThresholdSlider.IsEnabled);
            SaveSettings(window);
            var saved = store.Load().Settings;
            TestAssert.False(saved.BatteryAlerts.Enabled);
            TestAssert.Equal(BatteryAlertSettings.MaximumThresholdPercent, saved.BatteryAlerts.ThresholdPercent);

            var reopened = new SettingsWindow(store, saved, _ => null, _ => { }, () => { }, _ => null);
            try
            {
                TestAssert.Equal(false, reopened.BatteryAlertsEnabledCheckBox.IsChecked);
                TestAssert.Equal((double)BatteryAlertSettings.MaximumThresholdPercent, reopened.BatteryAlertThresholdSlider.Value);
            }
            finally { reopened.Close(); }
        }
        finally { window.Close(); }
    });

    internal static void BatterySettingsCancelKeepsSavedPreferences() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        store.Save(WidgetSettings.Default);
        var window = CreateStartupSettingsWindow(store, _ => null);
        window.BatteryAlertsEnabledCheckBox.IsChecked = false;
        window.BatteryAlertThresholdSlider.Value = BatteryAlertSettings.MinimumThresholdPercent;
        window.Close();
        TestAssert.Equal(new BatteryAlertSettings(), store.Load().Settings.BatteryAlerts);
    });
}
