namespace WinUtil.Models;

internal sealed record AudioEndpoint(string Id, string Name);

internal enum AudioDataFlow
{
    Output = 0,
    Input = 1
}

internal enum AudioDeviceRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}
