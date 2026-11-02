using WinUtil.Models;

namespace WinUtil.Services;

internal interface IAudioNativeAdapter
{
    IReadOnlyList<AudioEndpoint> GetActiveEndpoints(AudioDataFlow flow);

    string? GetDefaultDeviceId(AudioDataFlow flow, AudioDeviceRole role);

    void SetDefaultDevice(string deviceId, AudioDeviceRole role);

    double GetVolumePercent(string deviceId);

    void SetVolumePercent(string deviceId, double percentage);

    bool GetMute(string deviceId);

    void SetMute(string deviceId, bool muted);
}
