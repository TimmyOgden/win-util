using System.IO;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class BatterySettingsPersistenceTests
{
    internal static void BatteryPreferencesRoundTripAndOldSettingsUseDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("settings.json");
        var store = new SettingsStore(path);
        var preferences = new BatteryAlertSettings { Enabled = false, ThresholdPercent = BatteryAlertSettings.MinimumThresholdPercent };
        store.Save(WidgetSettings.Default with { BatteryAlerts = preferences });
        TestAssert.Equal(preferences, store.Load().Settings.BatteryAlerts);

        foreach (var json in new[] { "{}", "{\"BatteryAlerts\":null}" })
        {
            File.WriteAllText(path, json);
            TestAssert.Equal(new BatteryAlertSettings(), store.Load().Settings.BatteryAlerts);
        }
    }
}
