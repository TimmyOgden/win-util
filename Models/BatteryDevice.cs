using System.Globalization;

namespace WinUtil.Models;

internal sealed record BatteryDevice(string Name, int? Percentage)
{
    internal const string SystemBatteryId = "system-battery";
    internal const int MinimumPercentage = 0;
    internal const int MaximumPercentage = 100;

    public string? Id { get; init; }

    public bool? IsCharging { get; init; }

    public bool? IsPluggedIn { get; init; }

    public string PercentageText => Percentage is >= MinimumPercentage and <= MaximumPercentage
        ? Percentage.Value.ToString(CultureInfo.CurrentCulture) + "%"
        : "Unavailable";

    public string StatusText => IsCharging == true
        ? "Charging"
        : IsPluggedIn == true
            ? "Plugged in"
            : IsCharging == false && IsPluggedIn == false ? "On battery" : string.Empty;

    public string StatusToolTip => IsCharging == true
        ? "Battery is charging."
        : IsPluggedIn == true
            ? "Connected to external power."
            : IsCharging == false && IsPluggedIn == false
                ? "Running on battery power."
                : "Charging and power connection status are not reported by this device.";
}
