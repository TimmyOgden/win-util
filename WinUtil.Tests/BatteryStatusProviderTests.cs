using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class BatteryStatusProviderTests
{
    internal static void ParseBluetoothDevicesExcludesDisconnectedDevices()
    {
        const string output = """
            [
              { "Id": "keyboard", "Name": "Keyboard", "Percentage": 64, "IsConnected": false },
              { "Id": "mouse", "Name": "Mouse", "Percentage": 82, "IsConnected": true }
            ]
            """;

        var devices = BatteryStatusProvider.ParseBluetoothDevices(output);

        TestAssert.SequenceEqual([new BatteryDevice("Mouse", 82) { Id = "mouse" }], devices);
    }

    internal static void ParseBluetoothDevicesNormalizesConnectedDevices()
    {
        const string output = """
            [
              { "Id": " mouse ", "Name": " Mouse ", "Percentage": 45, "IsConnected": true },
              { "Id": "MOUSE", "Name": "mouse", "Percentage": 72, "IsConnected": true },
              { "Id": "headset", "Name": "Headset", "Percentage": 100, "IsConnected": true },
              { "Id": "unnamed", "Name": "", "Percentage": 40, "IsConnected": true },
              { "Id": "invalid", "Name": "Invalid", "Percentage": 101, "IsConnected": true }
            ]
            """;

        var devices = BatteryStatusProvider.ParseBluetoothDevices(output);

        TestAssert.SequenceEqual(
            [new BatteryDevice("Headset", 100) { Id = "headset" }, new BatteryDevice("Mouse", 72) { Id = "mouse" }],
            devices);
    }

    internal static void ParseBluetoothDevicesKeepsSameNameDevicesWithDifferentIds()
    {
        const string output = """
            [
              { "Id": "first", "Name": "Mouse", "Percentage": 10, "IsConnected": true },
              { "Id": "second", "Name": "Mouse", "Percentage": 90, "IsConnected": true }
            ]
            """;

        TestAssert.SequenceEqual(["first", "second"], BatteryStatusProvider.ParseBluetoothDevices(output).Select(device => device.Id));
    }

    internal static void ParseBluetoothDevicesRejectsUnknownConnectionAndIdentity()
    {
        const string output = """
            [
              null,
              { "Id": "unknown", "Name": "Mouse", "Percentage": 10 },
              { "Id": "null-connection", "Name": "Mouse", "Percentage": 10, "IsConnected": null },
              { "Id": " ", "Name": "Mouse", "Percentage": 10, "IsConnected": true },
              { "Name": "Mouse", "Percentage": 10, "IsConnected": true },
              { "Id": "unnamed", "Name": null, "Percentage": 10, "IsConnected": true },
              { "Id": "negative", "Name": "Mouse", "Percentage": -1, "IsConnected": true }
            ]
            """;

        TestAssert.Empty(BatteryStatusProvider.ParseBluetoothDevices(output));
    }

    internal static void ParseBluetoothDevicesHandlesEmptyAndUnknownPercentages()
    {
        TestAssert.Empty(BatteryStatusProvider.ParseBluetoothDevices("[]"));
        TestAssert.Empty(BatteryStatusProvider.ParseBluetoothDevices("null"));
        var device = TestAssert.Single(BatteryStatusProvider.ParseBluetoothDevices("""
            [{ "Id": "mouse", "Name": "Mouse", "Percentage": null, "IsConnected": true }]
            """));

        TestAssert.Equal<int?>(null, device.Percentage);
        TestAssert.Equal<bool?>(null, device.IsCharging);
        TestAssert.Equal<bool?>(null, device.IsPluggedIn);
    }

    internal static void ParseBluetoothDevicesRejectsNullAndMalformedInput()
    {
        AssertThrows<ArgumentNullException>(() => BatteryStatusProvider.ParseBluetoothDevices(null!));
        AssertThrows<System.Text.Json.JsonException>(() => BatteryStatusProvider.ParseBluetoothDevices(string.Empty));
        AssertThrows<System.Text.Json.JsonException>(() => BatteryStatusProvider.ParseBluetoothDevices("{}"));
    }

    internal static void ParseBluetoothQueryResultPreservesValidDevicesAndReportsConnectionErrors()
    {
        const string output = """
            [
              { "Id": "mouse", "Name": "Mouse", "Percentage": 10, "IsConnected": true },
              { "Id": "headset", "Name": "Headset", "Percentage": 15, "IsConnected": null,
                "QueryError": "Headset connection could not be confirmed." }
            ]
            """;

        var result = BatteryStatusProvider.ParseBluetoothQueryResult(output);

        TestAssert.Equal("mouse", TestAssert.Single(result.Devices).Id);
        TestAssert.Equal("Headset connection could not be confirmed.", result.ErrorMessage);
        TestAssert.Equal<string?>(null, BatteryStatusProvider.ParseBluetoothQueryResult("[]").ErrorMessage);
    }

    internal static void ParseSystemBatteryRecognizesCombinedChargingFlags()
    {
        const byte online = 1;
        const byte lowAndCharging = 2 | 8;
        var device = BatteryStatusProvider.ParseSystemBattery(online, lowAndCharging, 20)!;

        TestAssert.Equal(BatteryDevice.SystemBatteryId, device.Id);
        TestAssert.Equal<bool?>(true, device.IsCharging);
        TestAssert.Equal<bool?>(true, device.IsPluggedIn);
        TestAssert.Equal<int?>(20, device.Percentage);
    }

    internal static void ParseSystemBatteryDistinguishesUnknownFlagsFromNoBattery()
    {
        var unknown = BatteryStatusProvider.ParseSystemBattery(byte.MaxValue, byte.MaxValue, byte.MaxValue)!;

        TestAssert.Equal<int?>(null, unknown.Percentage);
        TestAssert.Equal<bool?>(null, unknown.IsCharging);
        TestAssert.Equal<bool?>(null, unknown.IsPluggedIn);
        TestAssert.Equal<BatteryDevice?>(null, BatteryStatusProvider.ParseSystemBattery(1, 128, 255));
        TestAssert.Equal<BatteryDevice?>(null, BatteryStatusProvider.ParseSystemBattery(1, 128 | 2, 10));
    }

    internal static void ParseSystemBatteryHandlesPercentageAndAcBoundaries()
    {
        var empty = BatteryStatusProvider.ParseSystemBattery(0, 0, 0)!;
        var full = BatteryStatusProvider.ParseSystemBattery(1, 1, 100)!;
        var invalid = BatteryStatusProvider.ParseSystemBattery(2, 0, 101)!;

        TestAssert.Equal<int?>(0, empty.Percentage);
        TestAssert.Equal<bool?>(false, empty.IsCharging);
        TestAssert.Equal<bool?>(false, empty.IsPluggedIn);
        TestAssert.Equal<int?>(100, full.Percentage);
        TestAssert.Equal<int?>(null, invalid.Percentage);
        TestAssert.Equal<bool?>(null, invalid.IsPluggedIn);
    }

    internal static void GetSnapshotCombinesIndependentSourcesAndErrors()
    {
        var system = new BatteryDevice("This device", 20) { Id = BatteryDevice.SystemBatteryId };
        var bluetooth = new BatteryDevice("Mouse", 30) { Id = "mouse" };
        IBatteryStatusProvider provider = new BatteryStatusProvider(
            () => new([system], "System warning"),
            _ => Task.FromResult(new BatteryStatusProvider.BatteryQueryResult([bluetooth], "Bluetooth warning")));

        var snapshot = provider.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();

        TestAssert.SequenceEqual([system, bluetooth], snapshot.Devices);
        TestAssert.Equal("System warning", snapshot.SystemErrorMessage);
        TestAssert.Equal("Bluetooth warning", snapshot.BluetoothErrorMessage);
    }

    internal static void GetSnapshotPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IBatteryStatusProvider provider = new BatteryStatusProvider(
            () => throw new InvalidOperationException("Cancelled queries must not start."),
            _ => throw new InvalidOperationException("Cancelled queries must not start."));

        AssertThrows<OperationCanceledException>(() => provider.GetSnapshotAsync(cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void GetSnapshotRejectsResultsAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IBatteryStatusProvider provider = new BatteryStatusProvider(
            () => throw new InvalidOperationException("Cancelled queries must not publish system status."),
            _ =>
            {
                cancellation.Cancel();
                return Task.FromResult(new BatteryStatusProvider.BatteryQueryResult([], null));
            });

        AssertThrows<OperationCanceledException>(() => provider.GetSnapshotAsync(cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void ConstructorValidatesQueryDependencies()
    {
        AssertThrows<ArgumentNullException>(() => new BatteryStatusProvider(null!, _ => Task.FromResult(new BatteryStatusProvider.BatteryQueryResult([], null))));
        AssertThrows<ArgumentNullException>(() => new BatteryStatusProvider(() => new([], null), null!));
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }
}
