namespace WinUtil.Models;

internal sealed record BatteryAlertSettings
{
    internal const int MinimumThresholdPercent = 1;
    internal const int MaximumThresholdPercent = 100;
    internal const int DefaultThresholdPercent = 20;

    public bool Enabled { get; init; } = true;

    public int ThresholdPercent { get; init; } = DefaultThresholdPercent;

    internal BatteryAlertSettings Normalize() => this with
    {
        ThresholdPercent = Math.Clamp(ThresholdPercent, MinimumThresholdPercent, MaximumThresholdPercent)
    };
}
