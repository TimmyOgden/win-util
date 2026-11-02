using WinUtil.Models;

namespace WinUtil.Services;

internal sealed class BatteryAlertPolicy
{
    private readonly HashSet<string> alertedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<BatteryDevice> Evaluate(BatterySnapshot snapshot, BatteryAlertSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot.Devices);
        var normalizedSettings = settings.Normalize();
        var alerts = new List<BatteryDevice>();
        var validDeviceGroups = snapshot.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id)
                && device.Percentage is >= BatteryDevice.MinimumPercentage and <= BatteryDevice.MaximumPercentage)
            .GroupBy(device => device.Id!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in validDeviceGroups)
        {
            if (group.Any(device => device.Percentage > normalizedSettings.ThresholdPercent))
            {
                alertedDeviceIds.Remove(group.Key);
                continue;
            }

            if (!normalizedSettings.Enabled || group.Any(device => device.IsCharging == true || device.IsPluggedIn == true)
                || (string.Equals(group.Key, BatteryDevice.SystemBatteryId, StringComparison.OrdinalIgnoreCase)
                    && group.Any(device => device.IsCharging != false || device.IsPluggedIn != false)))
            {
                continue;
            }

            if (alertedDeviceIds.Add(group.Key))
            {
                alerts.Add(group.First());
            }
        }

        return alerts;
    }
}
