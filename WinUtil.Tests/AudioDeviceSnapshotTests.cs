using WinUtil.Models;

namespace WinUtil.Tests;

internal static class AudioDeviceSnapshotTests
{
    internal static void HasDevicesTreatsBlankNamesAsUnavailable()
    {
        var snapshot = new AudioDeviceSnapshot(null, "   ", null);

        TestAssert.False(snapshot.HasOutputDevice);
        TestAssert.False(snapshot.HasInputDevice);
    }

    internal static void HasDevicesRecognizesReportedNames()
    {
        var snapshot = new AudioDeviceSnapshot("Speakers", "Microphone", null);

        TestAssert.True(snapshot.HasOutputDevice);
        TestAssert.True(snapshot.HasInputDevice);
    }

    internal static void LegacySnapshotDoesNotInventControllableDevices()
    {
        var snapshot = new AudioDeviceSnapshot("Speakers", "Microphone", null);

        TestAssert.Empty(snapshot.OutputDevices);
        TestAssert.Empty(snapshot.InputDevices);
        TestAssert.Equal<string?>(null, snapshot.DefaultOutputDeviceId);
        TestAssert.Equal<string?>(null, snapshot.DefaultInputDeviceId);
        TestAssert.Equal<double?>(null, snapshot.OutputVolumePercent);
        TestAssert.Equal<bool?>(null, snapshot.IsMicrophoneMuted);
    }

    internal static void OperationResultDistinguishesSuccessAndFailure()
    {
        TestAssert.True(new AudioOperationResult(null).Succeeded);
        TestAssert.False(new AudioOperationResult("Device disconnected.").Succeeded);
        TestAssert.False(new AudioOperationResult(string.Empty).Succeeded);
    }
}
