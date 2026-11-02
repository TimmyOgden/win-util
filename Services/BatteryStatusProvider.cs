using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinUtil.Models;

namespace WinUtil.Services;

internal interface IBatteryStatusProvider
{
    Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

internal sealed class BatteryStatusProvider : IBatteryStatusProvider
{
    private const byte NoBattery = 128;
    private const byte ChargingBattery = 8;
    private const byte UnknownPowerStatus = byte.MaxValue;
    private const byte AcPowerOffline = 0;
    private const byte AcPowerOnline = 1;
    private static readonly TimeSpan BluetoothQueryTimeout = TimeSpan.FromSeconds(20);
    private readonly Func<BatteryQueryResult> querySystemBattery;
    private readonly Func<CancellationToken, Task<BatteryQueryResult>> queryBluetoothBatteries;
    private const string BluetoothBatteryCommand = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        [Windows.Devices.Bluetooth.BluetoothLEDevice,Windows.Devices.Bluetooth,ContentType=WindowsRuntime] | Out-Null
        [Windows.Devices.Bluetooth.BluetoothDevice,Windows.Devices.Bluetooth,ContentType=WindowsRuntime] | Out-Null
        [Windows.Devices.Bluetooth.BluetoothConnectionStatus,Windows.Devices.Bluetooth,ContentType=WindowsRuntime] | Out-Null

        $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        })[0]

        function Await($WinRtTask, $ResultType) {
            $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
            $netTask = $asTask.Invoke($null, @($WinRtTask))
            $netTask.Wait(-1) | Out-Null
            $netTask.Result
        }

        function Test-BluetoothDeviceConnected([string]$deviceAddressHex, [bool]$isLowEnergy) {
            $address = [Convert]::ToUInt64($deviceAddressHex, 16)
            $device = if ($isLowEnergy) {
                Await ([Windows.Devices.Bluetooth.BluetoothLEDevice]::FromBluetoothAddressAsync($address)) ([Windows.Devices.Bluetooth.BluetoothLEDevice])
            } else {
                Await ([Windows.Devices.Bluetooth.BluetoothDevice]::FromBluetoothAddressAsync($address)) ([Windows.Devices.Bluetooth.BluetoothDevice])
            }

            if ($null -eq $device) {
                throw 'Bluetooth device connection status is unavailable.'
            }

            try {
                return $device.ConnectionStatus -eq [Windows.Devices.Bluetooth.BluetoothConnectionStatus]::Connected
            }
            finally {
                $device.Dispose()
            }
        }

        $devices = Get-PnpDevice -Class Bluetooth -PresentOnly | Where-Object {
            $_.FriendlyName -and ($_.InstanceId -like 'BTHLE\DEV_*' -or $_.InstanceId -like 'BTHENUM\DEV_*')
        } | ForEach-Object {
            $deviceId = $_.InstanceId
            $deviceName = $_.FriendlyName
            $battery = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_BatteryLevel' -ErrorAction SilentlyContinue
            if ($null -ne $battery.Data) {
                $percentage = [int]$battery.Data
                if ($percentage -ge 0 -and $percentage -le 100) {
                    $isLowEnergy = $_.InstanceId -like 'BTHLE\DEV_*'
                    $addressProperty = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Bluetooth_DeviceAddress' -ErrorAction SilentlyContinue
                    $isConnected = $null
                    $queryError = $null
                    try {
                        $isConnected = if ($addressProperty.Data) {
                            Test-BluetoothDeviceConnected -deviceAddressHex $addressProperty.Data -isLowEnergy $isLowEnergy
                        } else {
                            throw 'Bluetooth device connection status could not be confirmed.'
                        }
                    }
                    catch {
                        $queryError = "Bluetooth connection status could not be confirmed for '$deviceName'."
                    }

                    [PSCustomObject]@{
                        Id = $deviceId
                        Name = $deviceName
                        Percentage = $percentage
                        IsConnected = $isConnected
                        QueryError = $queryError
                    }
                }
            }
        }
        $jsonDevices = @($devices | ForEach-Object { $_ | ConvertTo-Json -Compress })
        [System.Console]::Out.Write("[$($jsonDevices -join ',')]")
        """;

    internal BatteryStatusProvider()
        : this(GetSystemBattery, GetBluetoothBatteriesAsync)
    {
    }

    internal BatteryStatusProvider(Func<BatteryQueryResult> querySystemBattery, Func<CancellationToken, Task<BatteryQueryResult>> queryBluetoothBatteries)
    {
        ArgumentNullException.ThrowIfNull(querySystemBattery);
        ArgumentNullException.ThrowIfNull(queryBluetoothBatteries);
        this.querySystemBattery = querySystemBattery;
        this.queryBluetoothBatteries = queryBluetoothBatteries;
    }

    Task<BatterySnapshot> IBatteryStatusProvider.GetSnapshotAsync(CancellationToken cancellationToken) => GetSnapshotAsync(cancellationToken);

    private async Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bluetoothResult = await queryBluetoothBatteries(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var systemResult = querySystemBattery();
        var devices = systemResult.Devices.Concat(bluetoothResult.Devices).ToArray();

        return new BatterySnapshot(devices, bluetoothResult.ErrorMessage, systemResult.ErrorMessage);
    }

    private static BatteryQueryResult GetSystemBattery()
    {
        if (!GetSystemPowerStatus(out var powerStatus))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            return new BatteryQueryResult([], $"System battery status could not be refreshed: {error.Message}");
        }

        var device = ParseSystemBattery(powerStatus.AcLineStatus, powerStatus.BatteryFlag, powerStatus.BatteryLifePercent);
        return new BatteryQueryResult(device is null ? [] : [device], null);
    }

    internal static BatteryDevice? ParseSystemBattery(byte acLineStatus, byte batteryFlag, byte batteryLifePercent)
    {
        if (batteryFlag != UnknownPowerStatus && (batteryFlag & NoBattery) != 0)
        {
            return null;
        }

        return new BatteryDevice("This device", batteryLifePercent <= BatteryDevice.MaximumPercentage ? batteryLifePercent : null)
        {
            Id = BatteryDevice.SystemBatteryId,
            IsCharging = batteryFlag == UnknownPowerStatus ? null : (batteryFlag & ChargingBattery) != 0,
            IsPluggedIn = acLineStatus switch
            {
                AcPowerOffline => false,
                AcPowerOnline => true,
                _ => null
            }
        };
    }

    private static async Task<BatteryQueryResult> GetBluetoothBatteriesAsync(CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(BluetoothQueryTimeout);
        using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        using var process = new Process
        {
            StartInfo = CreateBluetoothQueryStartInfo()
        };

        try
        {
            if (!process.Start())
            {
                return new BatteryQueryResult([], CreateBluetoothErrorMessage(string.Empty));
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(queryCancellation.Token);
            var errorTask = process.StandardError.ReadToEndAsync(queryCancellation.Token);

            try
            {
                await process.WaitForExitAsync(queryCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return new BatteryQueryResult([], CreateBluetoothErrorMessage(error));
            }

            return ParseBluetoothQueryResult(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BatteryQueryResult([], "Bluetooth battery discovery timed out.");
        }
        catch (JsonException)
        {
            return new BatteryQueryResult([], "Bluetooth battery discovery returned unreadable data.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return new BatteryQueryResult([], $"Bluetooth battery discovery could not start: {exception.Message}");
        }
    }

    internal static IReadOnlyList<BatteryDevice> ParseBluetoothDevices(string output) => ParseBluetoothQueryResult(output).Devices;

    internal static BatteryQueryResult ParseBluetoothQueryResult(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var queriedDevices = JsonSerializer.Deserialize<BluetoothBatteryDevice?[]>(output) ?? [];

        var devices = queriedDevices
            .OfType<BluetoothBatteryDevice>()
            .Where(device => device.IsConnected == true && !string.IsNullOrWhiteSpace(device.Id)
                && !string.IsNullOrWhiteSpace(device.Name)
                && (device.Percentage is null or >= BatteryDevice.MinimumPercentage and <= BatteryDevice.MaximumPercentage))
            .GroupBy(device => device.Id!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new BatteryDevice(group.First().Name!.Trim(), group.Max(device => device.Percentage)) { Id = group.Key })
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var errors = queriedDevices.OfType<BluetoothBatteryDevice>()
            .Select(device => device.QueryError)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.Ordinal).ToArray();
        return new BatteryQueryResult(devices, errors.Length == 0 ? null : string.Join(Environment.NewLine, errors));
    }

    private static ProcessStartInfo CreateBluetoothQueryStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            FileName = "powershell.exe",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(BluetoothBatteryCommand);
        return startInfo;
    }

    private static string CreateBluetoothErrorMessage(string error) => string.IsNullOrWhiteSpace(error)
        ? "Bluetooth battery discovery could not complete."
        : "Bluetooth battery discovery could not complete. Check device connections and the Windows PnPDevice module.";

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private sealed record BluetoothBatteryDevice(string? Id, string? Name, int? Percentage, bool? IsConnected, string? QueryError);

    internal sealed record BatteryQueryResult(IReadOnlyList<BatteryDevice> Devices, string? ErrorMessage);
}
