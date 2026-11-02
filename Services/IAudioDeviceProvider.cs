using WinUtil.Models;

namespace WinUtil.Services;

internal interface IAudioDeviceProvider
{
    AudioDeviceSnapshot GetSnapshot();

    AudioOperationResult SetDefaultOutput(string deviceId);

    AudioOperationResult SetDefaultInput(string deviceId);

    AudioOperationResult SetOutputVolume(double percentage);

    AudioOperationResult SetMicrophoneMute(bool muted);
}
