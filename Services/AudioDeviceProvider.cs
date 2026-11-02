using System.ComponentModel;
using System.Runtime.InteropServices;
using WinUtil.Models;

namespace WinUtil.Services;

internal sealed class AudioDeviceProvider : IAudioDeviceProvider
{
    internal const double MinimumVolumePercent = 0;
    internal const double MaximumVolumePercent = 100;
    private const double VolumeVerificationTolerance = 1;
    private static readonly AudioDeviceRole[] DefaultRoles = Enum.GetValues<AudioDeviceRole>();
    private readonly IAudioNativeAdapter native;

    internal AudioDeviceProvider() : this(new WindowsAudioNativeAdapter()) { }

    internal AudioDeviceProvider(IAudioNativeAdapter native)
    {
        ArgumentNullException.ThrowIfNull(native);
        this.native = native;
    }

    public AudioDeviceSnapshot GetSnapshot()
    {
        var errors = new List<string>();
        var outputDevices = ReadEndpoints(AudioDataFlow.Output, errors);
        var inputDevices = ReadEndpoints(AudioDataFlow.Input, errors);
        var output = ReadDefault(AudioDataFlow.Output, outputDevices, errors);
        var input = ReadDefault(AudioDataFlow.Input, inputDevices, errors);
        double? volume = null;
        bool? muted = null;
        if (output is not null)
        {
            try
            {
                var reportedVolume = native.GetVolumePercent(output.Id);
                ValidateVolume(reportedVolume);
                volume = reportedVolume;
            }
            catch (Exception exception) when (IsAudioFailure(exception))
            {
                errors.Add($"Could not read output volume. {exception.Message}");
            }
        }
        if (input is not null)
        {
            try { muted = native.GetMute(input.Id); }
            catch (Exception exception) when (IsAudioFailure(exception))
            {
                errors.Add($"Could not read microphone mute. {exception.Message}");
            }
        }
        return new AudioDeviceSnapshot(output?.Name, input?.Name, errors.Count == 0 ? null : string.Join(" ", errors))
        {
            OutputDevices = outputDevices,
            InputDevices = inputDevices,
            DefaultOutputDeviceId = output?.Id,
            DefaultInputDeviceId = input?.Id,
            OutputVolumePercent = volume,
            IsMicrophoneMuted = muted
        };
    }

    public AudioOperationResult SetDefaultOutput(string deviceId) => SetDefaultDevice(AudioDataFlow.Output, deviceId);

    public AudioOperationResult SetDefaultInput(string deviceId) => SetDefaultDevice(AudioDataFlow.Input, deviceId);

    public AudioOperationResult SetOutputVolume(double percentage) => Execute("Output volume could not be changed.", () =>
    {
        ValidateVolume(percentage);
        var device = RequireCurrentDevice(AudioDataFlow.Output);
        native.SetVolumePercent(device.Id, percentage);
        var actual = native.GetVolumePercent(device.Id);
        ValidateVolume(actual);
        if (Math.Abs(actual - percentage) > VolumeVerificationTolerance)
        {
            throw new InvalidOperationException("Windows did not confirm the requested volume. Check the current volume and retry.");
        }
    });

    public AudioOperationResult SetMicrophoneMute(bool muted) => Execute("Microphone mute could not be changed.", () =>
    {
        var device = RequireCurrentDevice(AudioDataFlow.Input);
        native.SetMute(device.Id, muted);
        if (native.GetMute(device.Id) != muted)
        {
            throw new InvalidOperationException("Windows did not confirm the microphone state. Check the current state and retry.");
        }
    });

    private AudioOperationResult SetDefaultDevice(AudioDataFlow flow, string deviceId)
    {
        var previous = new Dictionary<AudioDeviceRole, string?>();
        var attempted = new List<AudioDeviceRole>();
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
            var endpoint = FindDevice(native.GetActiveEndpoints(flow), deviceId)
                ?? throw new InvalidOperationException($"The selected {FlowName(flow)} device is no longer active. Choose an available device.");
            foreach (var role in DefaultRoles) { previous.Add(role, native.GetDefaultDeviceId(flow, role)); }
            foreach (var role in DefaultRoles)
            {
                if (string.Equals(previous[role], endpoint.Id, StringComparison.OrdinalIgnoreCase)) { continue; }
                attempted.Add(role);
                native.SetDefaultDevice(endpoint.Id, role);
            }
            foreach (var role in DefaultRoles)
            {
                if (!string.Equals(native.GetDefaultDeviceId(flow, role), endpoint.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Windows did not confirm the default {FlowName(flow)} device for {role}.");
                }
            }
            return new AudioOperationResult(null);
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            var rollbackErrors = RestoreDefaults(flow, previous, attempted);
            var suffix = rollbackErrors.Count == 0 ? string.Empty : $" Previous defaults could not be fully restored or verified: {string.Join(" ", rollbackErrors)}";
            return new AudioOperationResult($"Default {FlowName(flow)} device could not be changed. {exception.Message}{suffix}");
        }
    }

    private List<string> RestoreDefaults(AudioDataFlow flow, IReadOnlyDictionary<AudioDeviceRole, string?> previous, IEnumerable<AudioDeviceRole> attempted)
    {
        var errors = new List<string>();
        foreach (var role in attempted.Reverse())
        {
            try
            {
                var oldId = previous[role];
                if (oldId is null)
                {
                    if (native.GetDefaultDeviceId(flow, role) is null) { continue; }
                    throw new InvalidOperationException($"{role} previously had no default device.");
                }
                native.SetDefaultDevice(oldId, role);
                if (!string.Equals(native.GetDefaultDeviceId(flow, role), oldId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Windows did not restore {role}.");
                }
            }
            catch (Exception exception) when (IsAudioFailure(exception)) { errors.Add($"{role}: {exception.Message}"); }
        }
        return errors;
    }

    private IReadOnlyList<AudioEndpoint> ReadEndpoints(AudioDataFlow flow, List<string> errors)
    {
        try { return native.GetActiveEndpoints(flow); }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            errors.Add($"Could not list {FlowName(flow)} devices. {exception.Message}");
            return [];
        }
    }

    private AudioEndpoint? ReadDefault(AudioDataFlow flow, IReadOnlyList<AudioEndpoint> devices, List<string> errors)
    {
        try
        {
            var deviceId = native.GetDefaultDeviceId(flow, AudioDeviceRole.Multimedia);
            if (deviceId is null) { return null; }
            return FindDevice(devices, deviceId)
                ?? throw new InvalidOperationException($"The default {FlowName(flow)} device is no longer active.");
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            errors.Add($"Could not read the default {FlowName(flow)} device. {exception.Message}");
            return null;
        }
    }

    private AudioEndpoint RequireCurrentDevice(AudioDataFlow flow)
    {
        var id = native.GetDefaultDeviceId(flow, AudioDeviceRole.Multimedia);
        return id is null ? throw new InvalidOperationException($"No default {FlowName(flow)} device is available.")
            : FindDevice(native.GetActiveEndpoints(flow), id)
                ?? throw new InvalidOperationException($"The default {FlowName(flow)} device is no longer active.");
    }

    private static AudioEndpoint? FindDevice(IEnumerable<AudioEndpoint> devices, string id) =>
        devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));

    private static AudioOperationResult Execute(string failureMessage, Action action)
    {
        try { action(); return new AudioOperationResult(null); }
        catch (Exception exception) when (IsAudioFailure(exception)) { return new AudioOperationResult($"{failureMessage} {exception.Message}"); }
    }

    private static void ValidateVolume(double percentage)
    {
        if (!double.IsFinite(percentage) || percentage < MinimumVolumePercent || percentage > MaximumVolumePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Volume must be a finite percentage between 0 and 100.");
        }
    }

    private static string FlowName(AudioDataFlow flow) => flow == AudioDataFlow.Output ? "output" : "input";

    private static bool IsAudioFailure(Exception exception) => exception is COMException or Win32Exception or InvalidOperationException
        or ArgumentException or NotSupportedException or NotImplementedException or InvalidCastException or UnauthorizedAccessException or System.Security.SecurityException;
}
