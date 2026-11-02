using WinUtil.Models;

namespace WinUtil.Tests;

internal static class BatteryAlertSettingsTests
{
    internal static void NormalizePreservesDefaultsAndDisabledState()
    {
        TestAssert.Equal(new BatteryAlertSettings(), new BatteryAlertSettings().Normalize());
        TestAssert.False(new BatteryAlertSettings { Enabled = false }.Normalize().Enabled);
        TestAssert.Equal(20, new BatteryAlertSettings().ThresholdPercent);
    }

    internal static void NormalizeClampsThresholdBoundaries()
    {
        TestAssert.Equal(1, new BatteryAlertSettings { ThresholdPercent = int.MinValue }.Normalize().ThresholdPercent);
        TestAssert.Equal(100, new BatteryAlertSettings { ThresholdPercent = int.MaxValue }.Normalize().ThresholdPercent);
        TestAssert.Equal(1, new BatteryAlertSettings { ThresholdPercent = 1 }.Normalize().ThresholdPercent);
        TestAssert.Equal(100, new BatteryAlertSettings { ThresholdPercent = 100 }.Normalize().ThresholdPercent);
    }
}
