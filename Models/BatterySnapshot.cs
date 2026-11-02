namespace WinUtil.Models;

internal sealed record BatterySnapshot(IReadOnlyList<BatteryDevice> Devices, string? BluetoothErrorMessage, string? SystemErrorMessage = null)
{
    internal bool HasDevices => Devices.Count > 0;

    internal string? ErrorMessage
    {
        get
        {
            var messages = new[] { SystemErrorMessage, BluetoothErrorMessage }
                .Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
            return messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
        }
    }
}
