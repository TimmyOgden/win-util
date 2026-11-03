using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class BatteryAlertPolicyTests
{
    internal static void EvaluateAlertsOnceAtThresholdAndAtZero()
    {
        var policy = new BatteryAlertPolicy();
        var snapshot = Snapshot(Device("mouse", 20), Device("keyboard", 0));

        TestAssert.SequenceEqual(snapshot.Devices, policy.Evaluate(snapshot, new()));
        TestAssert.Empty(policy.Evaluate(snapshot, new()));
    }

    internal static void EvaluateRearmsOnlyAfterHealthyReading()
    {
        var policy = new BatteryAlertPolicy();
        var low = Snapshot(Device("mouse", 20));
        TestAssert.Single(policy.Evaluate(low, new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(Device("mouse", 21)), new()));

        TestAssert.Single(policy.Evaluate(low, new()));
    }

    internal static void EvaluateRetainsEpisodeAcrossMissingErrorAndUnknownReadings()
    {
        var policy = new BatteryAlertPolicy();
        var low = Snapshot(Device("mouse", 15));
        TestAssert.Single(policy.Evaluate(low, new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(), new()));
        TestAssert.Empty(policy.Evaluate(new([], "Bluetooth unavailable", "System unavailable"), new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(Device("mouse", null)), new()));

        TestAssert.Empty(policy.Evaluate(low, new()));
    }

    internal static void EvaluateSuppressesChargingAndPluggedInDevices()
    {
        var policy = new BatteryAlertPolicy();
        var charging = Device("mouse", 15) with { IsCharging = true };
        var pluggedIn = Device("keyboard", 15) with { IsPluggedIn = true };

        TestAssert.Empty(policy.Evaluate(Snapshot(charging, pluggedIn), new()));
        TestAssert.Single(policy.Evaluate(Snapshot(charging with { IsCharging = false }), new()));
    }

    internal static void EvaluateChargingDoesNotRearmAnExistingLowEpisode()
    {
        var policy = new BatteryAlertPolicy();
        var device = Device("mouse", 15);
        TestAssert.Single(policy.Evaluate(Snapshot(device), new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(device with { IsCharging = true }), new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(device with { IsPluggedIn = true }), new()));

        TestAssert.Empty(policy.Evaluate(Snapshot(device), new()));
    }

    internal static void EvaluateDisabledAlertsDoNotMarkOrClearEpisodes()
    {
        var policy = new BatteryAlertPolicy();
        var low = Snapshot(Device("mouse", 15));
        var disabled = new BatteryAlertSettings { Enabled = false };
        TestAssert.Empty(policy.Evaluate(low, disabled));
        TestAssert.Single(policy.Evaluate(low, new()));
        TestAssert.Empty(policy.Evaluate(low, disabled));

        TestAssert.Empty(policy.Evaluate(low, new()));
    }

    internal static void EvaluateHealthyReadingRearmsWhileDisabled()
    {
        var policy = new BatteryAlertPolicy();
        var low = Snapshot(Device("mouse", 15));
        TestAssert.Single(policy.Evaluate(low, new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(Device("mouse", 100)), new() { Enabled = false }));

        TestAssert.Single(policy.Evaluate(low, new()));
    }

    internal static void EvaluateUsesStableCaseInsensitiveIds()
    {
        var policy = new BatteryAlertPolicy();
        var first = Device("DEVICE-A", 10);
        var second = Device("device-b", 10);
        TestAssert.Equal(2, policy.Evaluate(Snapshot(first, second), new()).Count);

        TestAssert.Empty(policy.Evaluate(Snapshot(first with { Id = " device-a ", Name = "Renamed" }), new()));
    }

    internal static void EvaluateRejectsUnknownIdsAndInvalidPercentages()
    {
        var snapshot = Snapshot(new("Legacy", 10), Device(" ", 10), Device("unknown", null),
            Device("negative", -1), Device("excess", 101), Device("healthy", 100));

        TestAssert.Empty(new BatteryAlertPolicy().Evaluate(snapshot, new()));
    }

    internal static void EvaluateSystemBatteryRequiresKnownOfflineNonchargingState()
    {
        var policy = new BatteryAlertPolicy();
        var device = Device(BatteryDevice.SystemBatteryId, 10);
        TestAssert.Empty(policy.Evaluate(Snapshot(device), new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(device with { IsPluggedIn = false }), new()));
        TestAssert.Empty(policy.Evaluate(Snapshot(device with { IsCharging = false }), new()));

        TestAssert.Single(policy.Evaluate(Snapshot(device with { IsCharging = false, IsPluggedIn = false }), new()));
    }

    internal static void EvaluateMaximumThresholdAndDuplicateIds()
    {
        var policy = new BatteryAlertPolicy();
        var snapshot = Snapshot(Device("mouse", 100), Device("MOUSE", 100));

        TestAssert.Single(policy.Evaluate(snapshot, new() { ThresholdPercent = 100 }));
    }

    internal static void EvaluateValidSourceDespiteOtherProviderError()
    {
        var device = Device("mouse", 10);

        TestAssert.Equal(device, TestAssert.Single(new BatteryAlertPolicy().Evaluate(new([device], null, "System unavailable"), new())));
    }

    internal static void EvaluateValidatesNullArguments()
    {
        AssertArgumentNull(() => new BatteryAlertPolicy().Evaluate(null!, new()));
        AssertArgumentNull(() => new BatteryAlertPolicy().Evaluate(Snapshot(), null!));
    }

    private static BatteryDevice Device(string id, int? percentage) => new("Battery", percentage) { Id = id };

    private static BatterySnapshot Snapshot(params BatteryDevice[] devices) => new(devices, null);

    private static void AssertArgumentNull(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected a null argument error.");
        }
        catch (ArgumentNullException)
        {
        }
    }
}
