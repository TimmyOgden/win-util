using WinUtil.Models;

namespace WinUtil.Tests;

internal static class BatteryDeviceTests
{
    internal static void PercentageTextFormatsMinimumAndMaximum()
    {
        TestAssert.Equal("0%", new BatteryDevice("Mouse", 0).PercentageText);
        TestAssert.Equal("100%", new BatteryDevice("Mouse", 100).PercentageText);
    }

    internal static void PercentageTextHandlesUnknownAndInvalidValues()
    {
        TestAssert.Equal("Unavailable", new BatteryDevice("Mouse", null).PercentageText);
        TestAssert.Equal("Unavailable", new BatteryDevice("Mouse", -1).PercentageText);
        TestAssert.Equal("Unavailable", new BatteryDevice("Mouse", 101).PercentageText);
    }

    internal static void StatusTextDistinguishesChargingPluggedInAndDischarging()
    {
        TestAssert.Equal("Charging", new BatteryDevice("Laptop", 10) { IsCharging = true, IsPluggedIn = true }.StatusText);
        TestAssert.Equal("Plugged in", new BatteryDevice("Laptop", 100) { IsCharging = false, IsPluggedIn = true }.StatusText);
        TestAssert.Equal("On battery", new BatteryDevice("Laptop", 10) { IsCharging = false, IsPluggedIn = false }.StatusText);
        TestAssert.Equal(string.Empty, new BatteryDevice("Mouse", 10).StatusText);
    }

    internal static void StatusToolTipExplainsUnreportedPowerState()
    {
        TestAssert.Equal("Charging and power connection status are not reported by this device.", new BatteryDevice("Mouse", 10).StatusToolTip);
        TestAssert.Equal("Battery is charging.", new BatteryDevice("Laptop", 10) { IsCharging = true }.StatusToolTip);
        TestAssert.Equal("Connected to external power.", new BatteryDevice("Laptop", 10) { IsPluggedIn = true }.StatusToolTip);
        TestAssert.Equal("Running on battery power.", new BatteryDevice("Laptop", 10) { IsCharging = false, IsPluggedIn = false }.StatusToolTip);
    }
}
