using System.Runtime.InteropServices;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class AudioDeviceProviderTests
{
    private const double TestVolume = 37.5;

    internal static void AudioSnapshotReportsActiveDevicesDefaultsAndControls()
    {
        var native = new FakeAudioNativeAdapter { Volume = TestVolume, Muted = true };
        var snapshot = new AudioDeviceProvider(native).GetSnapshot();
        TestAssert.Equal("Speakers", snapshot.OutputDeviceName);
        TestAssert.Equal("Microphone", snapshot.InputDeviceName);
        TestAssert.Equal("speakers", snapshot.DefaultOutputDeviceId);
        TestAssert.Equal("microphone", snapshot.DefaultInputDeviceId);
        TestAssert.Equal<double?>(TestVolume, snapshot.OutputVolumePercent);
        TestAssert.Equal<bool?>(true, snapshot.IsMicrophoneMuted);
        TestAssert.Equal<string?>(null, snapshot.ErrorMessage);
        TestAssert.Equal(2, snapshot.OutputDevices.Count);
        TestAssert.Equal(2, snapshot.InputDevices.Count);
        TestAssert.Empty(native.Writes);
    }

    internal static void AudioSnapshotHandlesMissingAndRemovedDevices()
    {
        var native = new FakeAudioNativeAdapter();
        native.Outputs = [];
        native.Inputs = [];
        foreach (var key in native.Defaults.Keys.ToArray()) { native.Defaults[key] = null; }
        var provider = new AudioDeviceProvider(native);
        var empty = provider.GetSnapshot();
        TestAssert.False(empty.HasOutputDevice);
        TestAssert.False(empty.HasInputDevice);
        TestAssert.Equal<string?>(null, empty.ErrorMessage);
        TestAssert.Equal<double?>(null, empty.OutputVolumePercent);
        TestAssert.Equal<bool?>(null, empty.IsMicrophoneMuted);
        native.Defaults[(AudioDataFlow.Output, AudioDeviceRole.Multimedia)] = "removed";
        TestAssert.True(provider.GetSnapshot().ErrorMessage!.Contains("no longer active", StringComparison.Ordinal));
        TestAssert.False(provider.SetOutputVolume(TestVolume).Succeeded);
        TestAssert.False(provider.SetMicrophoneMute(true).Succeeded);
        TestAssert.Empty(native.Writes);
    }

    internal static void AudioSnapshotKeepsIndependentDevicesAndControlsOnReadFailure()
    {
        var native = new FakeAudioNativeAdapter { FailedEnumeration = AudioDataFlow.Output };
        var snapshot = new AudioDeviceProvider(native).GetSnapshot();
        TestAssert.Equal("microphone", snapshot.DefaultInputDeviceId);
        TestAssert.Equal<bool?>(false, snapshot.IsMicrophoneMuted);
        TestAssert.Equal<double?>(null, snapshot.OutputVolumePercent);
        TestAssert.True(snapshot.ErrorMessage!.Contains("Could not list output", StringComparison.Ordinal));

        native.FailedEnumeration = null;
        native.VolumeReadFails = true;
        native.MuteReadFails = true;
        snapshot = new AudioDeviceProvider(native).GetSnapshot();
        TestAssert.Equal(2, snapshot.OutputDevices.Count);
        TestAssert.Equal("speakers", snapshot.DefaultOutputDeviceId);
        TestAssert.True(snapshot.ErrorMessage!.Contains("output volume", StringComparison.Ordinal));
        TestAssert.True(snapshot.ErrorMessage.Contains("microphone mute", StringComparison.Ordinal));
        TestAssert.Equal<double?>(null, snapshot.OutputVolumePercent);
        TestAssert.Equal<bool?>(null, snapshot.IsMicrophoneMuted);
    }

    internal static void AudioSnapshotRejectsInvalidReportedVolume()
    {
        foreach (var volume in new[] { double.NaN, double.PositiveInfinity, -1, AudioDeviceProvider.MaximumVolumePercent + 1 })
        {
            var snapshot = new AudioDeviceProvider(new FakeAudioNativeAdapter { Volume = volume }).GetSnapshot();
            TestAssert.Equal<double?>(null, snapshot.OutputVolumePercent);
            TestAssert.True(snapshot.ErrorMessage!.Contains("finite percentage", StringComparison.Ordinal));
        }
    }

    internal static void DefaultAudioChangesAllRolesAndSkipsUnchangedDefaults()
    {
        var native = new FakeAudioNativeAdapter();
        var provider = new AudioDeviceProvider(native);
        TestAssert.True(provider.SetDefaultOutput("HEADPHONES").Succeeded);
        TestAssert.True(provider.SetDefaultInput("line-in").Succeeded);
        foreach (var role in Enum.GetValues<AudioDeviceRole>())
        {
            TestAssert.Equal("headphones", native.Defaults[(AudioDataFlow.Output, role)]);
            TestAssert.Equal("line-in", native.Defaults[(AudioDataFlow.Input, role)]);
        }
        var writes = native.Writes.Count;
        TestAssert.True(provider.SetDefaultOutput("headphones").Succeeded);
        TestAssert.True(provider.SetDefaultInput("line-in").Succeeded);
        TestAssert.Equal(writes, native.Writes.Count);
    }

    internal static void DefaultAudioRejectsMissingWrongDirectionAndInvalidIds()
    {
        var native = new FakeAudioNativeAdapter();
        var provider = new AudioDeviceProvider(native);
        foreach (var id in new[] { null, "", " ", "removed", "microphone" })
        {
            TestAssert.False(provider.SetDefaultOutput(id!).Succeeded);
        }
        foreach (var id in new[] { null, "", " ", "removed", "speakers" })
        {
            TestAssert.False(provider.SetDefaultInput(id!).Succeeded);
        }
        TestAssert.Empty(native.Writes);
    }

    internal static void DefaultAudioPartialFailureRestoresPreviousRoleDefaults()
    {
        var native = new FakeAudioNativeAdapter
        {
            DefaultFailure = (id, role) => id == "headphones" && role == AudioDeviceRole.Multimedia
        };
        var previous = native.Defaults.ToDictionary(pair => pair.Key, pair => pair.Value);
        var result = new AudioDeviceProvider(native).SetDefaultOutput("headphones");
        TestAssert.False(result.Succeeded);
        TestAssert.True(result.ErrorMessage!.Contains("Native audio failure", StringComparison.Ordinal));
        TestAssert.False(result.ErrorMessage.Contains("could not be fully restored", StringComparison.Ordinal));
        foreach (var (key, value) in previous) { TestAssert.Equal(value, native.Defaults[key]); }
    }

    internal static void DefaultAudioReportsRollbackFailureAndMissingPreviousDefault()
    {
        foreach (var missingPrevious in new[] { false, true })
        {
            var native = new FakeAudioNativeAdapter
            {
                DefaultFailure = (id, role) => (id == "headphones" && role == AudioDeviceRole.Multimedia)
                    || id == "speakers"
            };
            if (missingPrevious) { native.Defaults[(AudioDataFlow.Output, AudioDeviceRole.Console)] = null; }
            var result = new AudioDeviceProvider(native).SetDefaultOutput("headphones");
            TestAssert.False(result.Succeeded);
            TestAssert.True(result.ErrorMessage!.Contains("Previous defaults could not be fully restored", StringComparison.Ordinal));
        }
    }

    internal static void DefaultAudioRestoresDistinctRolesAfterAWriteChangesStateThenFails()
    {
        var native = new FakeAudioNativeAdapter
        {
            MutateBeforeDefaultFailure = true,
            DefaultFailure = (id, role) => id == "headphones" && role == AudioDeviceRole.Multimedia
        };
        native.Outputs = native.Outputs.Append(new AudioEndpoint("monitor", "Monitor")).ToArray();
        native.Defaults[(AudioDataFlow.Output, AudioDeviceRole.Multimedia)] = "monitor";
        var previous = native.Defaults.ToDictionary(pair => pair.Key, pair => pair.Value);
        TestAssert.False(new AudioDeviceProvider(native).SetDefaultOutput("headphones").Succeeded);
        foreach (var (key, value) in previous) { TestAssert.Equal(value, native.Defaults[key]); }
    }

    internal static void DefaultAudioRequiresWindowsConfirmation()
    {
        var native = new FakeAudioNativeAdapter { IgnoreDefaultChanges = true };
        var result = new AudioDeviceProvider(native).SetDefaultInput("line-in");
        TestAssert.False(result.Succeeded);
        TestAssert.True(result.ErrorMessage!.Contains("did not confirm", StringComparison.Ordinal));
        TestAssert.Equal("microphone", native.Defaults[(AudioDataFlow.Input, AudioDeviceRole.Multimedia)]);
    }

    internal static void DefaultAudioAttemptsRollbackEvenWhenVerificationReadsFail()
    {
        var native = new FakeAudioNativeAdapter { FailDefaultReadsAfterWrites = true };
        var result = new AudioDeviceProvider(native).SetDefaultOutput("headphones");
        TestAssert.False(result.Succeeded);
        foreach (var role in Enum.GetValues<AudioDeviceRole>())
        {
            TestAssert.Equal("speakers", native.Defaults[(AudioDataFlow.Output, role)]);
        }
        TestAssert.True(native.Writes.Any(write => write.StartsWith("default:speakers:", StringComparison.Ordinal)));
        TestAssert.True(result.ErrorMessage!.Contains("verified", StringComparison.Ordinal));
    }

    internal static void AudioProviderReportsAccessDeniedAndUnsupportedNativeOperations()
    {
        foreach (var failure in new Exception[] { new UnauthorizedAccessException("Denied"), new NotImplementedException("Unsupported") })
        {
            var native = new FakeAudioNativeAdapter { EnumerationFailure = failure, FailedEnumeration = AudioDataFlow.Output };
            var provider = new AudioDeviceProvider(native);
            TestAssert.True(provider.GetSnapshot().ErrorMessage!.Contains(failure.Message, StringComparison.Ordinal));
            TestAssert.False(provider.SetDefaultOutput("headphones").Succeeded);
            TestAssert.False(provider.SetOutputVolume(TestVolume).Succeeded);
            TestAssert.Empty(native.Writes);
        }
    }

    internal static void OutputVolumeAcceptsBoundariesAndRejectsInvalidValuesBeforeWriting()
    {
        var native = new FakeAudioNativeAdapter();
        var provider = new AudioDeviceProvider(native);
        foreach (var volume in new[] { AudioDeviceProvider.MinimumVolumePercent, TestVolume, AudioDeviceProvider.MaximumVolumePercent })
        {
            TestAssert.True(provider.SetOutputVolume(volume).Succeeded);
            TestAssert.Equal(volume, native.Volume);
        }
        var writes = native.Writes.Count;
        foreach (var volume in new[] { -1, AudioDeviceProvider.MaximumVolumePercent + 1, double.NaN, double.NegativeInfinity, double.PositiveInfinity, double.MaxValue })
        {
            TestAssert.False(provider.SetOutputVolume(volume).Succeeded);
        }
        TestAssert.Equal(writes, native.Writes.Count);
    }

    internal static void AudioControlsReportNativeFailuresAndUnconfirmedChanges()
    {
        var native = new FakeAudioNativeAdapter { ControlWritesFail = true };
        var provider = new AudioDeviceProvider(native);
        TestAssert.False(provider.SetOutputVolume(TestVolume).Succeeded);
        TestAssert.False(provider.SetMicrophoneMute(true).Succeeded);
        native.ControlWritesFail = false;
        native.IgnoreControlChanges = true;
        TestAssert.True(provider.SetOutputVolume(TestVolume).ErrorMessage!.Contains("did not confirm", StringComparison.Ordinal));
        TestAssert.True(provider.SetMicrophoneMute(true).ErrorMessage!.Contains("did not confirm", StringComparison.Ordinal));
    }

    internal static void MicrophoneMuteTogglesAndRejectsRemovedInput()
    {
        var native = new FakeAudioNativeAdapter();
        var provider = new AudioDeviceProvider(native);
        foreach (var muted in new[] { true, false })
        {
            TestAssert.True(provider.SetMicrophoneMute(muted).Succeeded);
            TestAssert.Equal(muted, native.Muted);
        }
        native.Inputs = [];
        var writes = native.Writes.Count;
        TestAssert.False(provider.SetMicrophoneMute(true).Succeeded);
        TestAssert.Equal(writes, native.Writes.Count);
    }

    internal static void AudioProviderRejectsNullNativeAdapter()
    {
        try { _ = new AudioDeviceProvider(null!); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("Expected a missing native adapter to be rejected.");
    }

    private sealed class FakeAudioNativeAdapter : IAudioNativeAdapter
    {
        internal IReadOnlyList<AudioEndpoint> Outputs { get; set; } = [new("speakers", "Speakers"), new("headphones", "Headphones")];
        internal IReadOnlyList<AudioEndpoint> Inputs { get; set; } = [new("microphone", "Microphone"), new("line-in", "Line in")];
        internal Dictionary<(AudioDataFlow Flow, AudioDeviceRole Role), string?> Defaults { get; } = Enum.GetValues<AudioDataFlow>()
            .SelectMany(flow => Enum.GetValues<AudioDeviceRole>().Select(role => (flow, role)))
            .ToDictionary(key => key, key => (string?)(key.flow == AudioDataFlow.Output ? "speakers" : "microphone"));
        internal List<string> Writes { get; } = [];
        internal double Volume { get; set; } = AudioDeviceProvider.MaximumVolumePercent;
        internal bool Muted { get; set; }
        internal AudioDataFlow? FailedEnumeration { get; set; }
        internal Exception? EnumerationFailure { get; init; }
        internal bool FailDefaultReadsAfterWrites { get; init; }
        internal bool VolumeReadFails { get; set; }
        internal bool MuteReadFails { get; set; }
        internal bool ControlWritesFail { get; set; }
        internal bool IgnoreControlChanges { get; set; }
        internal bool IgnoreDefaultChanges { get; set; }
        internal bool MutateBeforeDefaultFailure { get; init; }
        internal Func<string, AudioDeviceRole, bool>? DefaultFailure { get; init; }

        public IReadOnlyList<AudioEndpoint> GetActiveEndpoints(AudioDataFlow flow) => FailedEnumeration == flow
            ? throw EnumerationFailure ?? new COMException("Native audio failure") : flow == AudioDataFlow.Output ? Outputs : Inputs;
        public string? GetDefaultDeviceId(AudioDataFlow flow, AudioDeviceRole role) => FailDefaultReadsAfterWrites && Writes.Count > 0
            ? throw new COMException("Native audio failure") : Defaults[(flow, role)];
        public void SetDefaultDevice(string deviceId, AudioDeviceRole role)
        {
            Writes.Add($"default:{deviceId}:{role}");
            var fail = DefaultFailure?.Invoke(deviceId, role) == true;
            if (fail && !MutateBeforeDefaultFailure) { throw new COMException("Native audio failure"); }
            var flow = Outputs.Any(device => device.Id == deviceId) ? AudioDataFlow.Output : AudioDataFlow.Input;
            if (!IgnoreDefaultChanges) { Defaults[(flow, role)] = deviceId; }
            if (fail) { throw new COMException("Native audio failure"); }
        }
        public double GetVolumePercent(string deviceId) => VolumeReadFails ? throw new COMException("Native audio failure") : Volume;
        public bool GetMute(string deviceId) => MuteReadFails ? throw new COMException("Native audio failure") : Muted;
        public void SetVolumePercent(string deviceId, double percentage)
        {
            Writes.Add($"volume:{deviceId}");
            if (ControlWritesFail) { throw new COMException("Native audio failure"); }
            if (!IgnoreControlChanges) { Volume = percentage; }
        }
        public void SetMute(string deviceId, bool muted)
        {
            Writes.Add($"mute:{deviceId}");
            if (ControlWritesFail) { throw new COMException("Native audio failure"); }
            if (!IgnoreControlChanges) { Muted = muted; }
        }
    }
}
