using WinUtil.Models;

namespace WinUtil.Tests;

internal static class BatterySnapshotTests
{
    internal static void HasDevicesIsFalseForAnEmptySnapshot()
    {
        var snapshot = new BatterySnapshot([], null);

        TestAssert.False(snapshot.HasDevices);
    }

    internal static void HasDevicesIsTrueWhenABatteryIsAvailable()
    {
        var snapshot = new BatterySnapshot([new BatteryDevice("Mouse", 84)], null);

        TestAssert.True(snapshot.HasDevices);
    }

    internal static void ErrorMessageCombinesOnlyNonemptyProviderErrors()
    {
        TestAssert.Equal<string?>(null, new BatterySnapshot([], null).ErrorMessage);
        TestAssert.Equal<string?>(null, new BatterySnapshot([], " ", "").ErrorMessage);
        TestAssert.Equal("Bluetooth unavailable", new BatterySnapshot([], "Bluetooth unavailable").ErrorMessage);
        TestAssert.Equal("System unavailable", new BatterySnapshot([], null, "System unavailable").ErrorMessage);
        TestAssert.Equal("System unavailable" + Environment.NewLine + "Bluetooth unavailable",
            new BatterySnapshot([], "Bluetooth unavailable", "System unavailable").ErrorMessage);
    }
}
