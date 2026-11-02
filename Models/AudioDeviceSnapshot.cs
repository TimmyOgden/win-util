namespace WinUtil.Models;

internal sealed record AudioDeviceSnapshot(string? OutputDeviceName, string? InputDeviceName, string? ErrorMessage)
{
    public IReadOnlyList<AudioEndpoint> OutputDevices { get; init; } = [];

    public IReadOnlyList<AudioEndpoint> InputDevices { get; init; } = [];

    public string? DefaultOutputDeviceId { get; init; }

    public string? DefaultInputDeviceId { get; init; }

    public double? OutputVolumePercent { get; init; }

    public bool? IsMicrophoneMuted { get; init; }

    internal bool HasOutputDevice => !string.IsNullOrWhiteSpace(OutputDeviceName);

    internal bool HasInputDevice => !string.IsNullOrWhiteSpace(InputDeviceName);
}
