using System.Runtime.InteropServices;
using WinUtil.Models;
using static WinUtil.Services.AudioComInterop;

namespace WinUtil.Services;

internal sealed class WindowsAudioNativeAdapter : IAudioNativeAdapter
{
    private const double MinimumVolumePercent = 0;
    private const double MaximumVolumePercent = 100;
    private const float MinimumVolumeScalar = 0;
    private const float MaximumVolumeScalar = 1;
    private const uint ActiveDeviceState = 1;
    private const uint ReadPropertyStore = 0;
    private const uint InProcessServer = 1;
    private const uint FriendlyNamePropertyId = 14;
    private const int NoDefaultDevice = unchecked((int)0x80070490);
    private static readonly Guid ChangeContext = new("6C87D471-E3E4-454D-BB7F-F489C670D830");
    private static readonly PropertyKey FriendlyNameKey = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), FriendlyNamePropertyId);

    public IReadOnlyList<AudioEndpoint> GetActiveEndpoints(AudioDataFlow flow)
    {
        ValidateFlow(flow);
        return WithEnumerator(enumerator =>
        {
            EnsureSuccess(enumerator.EnumAudioEndpoints(flow, ActiveDeviceState, out var collection));
            try
            {
                EnsureSuccess(collection.GetCount(out var count));
                var endpoints = new List<AudioEndpoint>();
                for (uint index = 0; index < count; index++)
                {
                    EnsureSuccess(collection.Item(index, out var device));
                    try
                    {
                        EnsureSuccess(device.GetId(out var id));
                        endpoints.Add(new AudioEndpoint(id, ReadFriendlyName(device, id)));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                return (IReadOnlyList<AudioEndpoint>)endpoints.ToArray();
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        });
    }

    public string? GetDefaultDeviceId(AudioDataFlow flow, AudioDeviceRole role)
    {
        ValidateFlow(flow);
        ValidateRole(role);
        return WithEnumerator(enumerator =>
        {
            var result = enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
            if (result == NoDefaultDevice)
            {
                return null;
            }

            EnsureSuccess(result);
            if (device is null)
            {
                throw new InvalidOperationException("Windows returned no default audio endpoint.");
            }

            try
            {
                EnsureSuccess(device.GetId(out var id));
                return id;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        });
    }

    public void SetDefaultDevice(string deviceId, AudioDeviceRole role)
    {
        ValidateDeviceId(deviceId);
        ValidateRole(role);
        WithDevice(deviceId, _ =>
        {
            var policyObject = new PolicyConfigClient();
            try
            {
                var policy = (IPolicyConfig)policyObject;
                EnsureSuccess(policy.SetDefaultEndpoint(deviceId, role));
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(policyObject);
            }
        });
    }

    public double GetVolumePercent(string deviceId)
    {
        ValidateDeviceId(deviceId);
        return WithEndpointVolume(deviceId, volume =>
        {
            EnsureSuccess(volume.GetMasterVolumeLevelScalar(out var scalar));
            if (!float.IsFinite(scalar) || scalar is < MinimumVolumeScalar or > MaximumVolumeScalar)
            {
                throw new InvalidOperationException("Windows returned an invalid audio volume level.");
            }

            return scalar * MaximumVolumePercent;
        });
    }

    public void SetVolumePercent(string deviceId, double percentage)
    {
        ValidateDeviceId(deviceId);
        if (!double.IsFinite(percentage) || percentage is < MinimumVolumePercent or > MaximumVolumePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Choose an output volume between 0 and 100 percent.");
        }

        WithEndpointVolume(deviceId, volume =>
        {
            var context = ChangeContext;
            EnsureSuccess(volume.SetMasterVolumeLevelScalar((float)(percentage / MaximumVolumePercent), ref context));
            return true;
        });
    }

    public bool GetMute(string deviceId)
    {
        ValidateDeviceId(deviceId);
        return WithEndpointVolume(deviceId, volume =>
        {
            EnsureSuccess(volume.GetMute(out var muted));
            return muted;
        });
    }

    public void SetMute(string deviceId, bool muted)
    {
        ValidateDeviceId(deviceId);
        WithEndpointVolume(deviceId, volume =>
        {
            var context = ChangeContext;
            EnsureSuccess(volume.SetMute(muted, ref context));
            return true;
        });
    }

    private static string ReadFriendlyName(IMMDevice device, string fallbackName)
    {
        EnsureSuccess(device.OpenPropertyStore(ReadPropertyStore, out var store));
        try
        {
            var key = FriendlyNameKey;
            EnsureSuccess(store.GetValue(ref key, out var value));
            try
            {
                var name = value.ReadString();
                return string.IsNullOrWhiteSpace(name) ? fallbackName : name;
            }
            finally
            {
                EnsureSuccess(PropVariantClear(ref value));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static T WithEnumerator<T>(Func<IMMDeviceEnumerator, T> operation)
    {
        var enumeratorObject = new MultimediaDeviceEnumerator();
        try
        {
            return operation((IMMDeviceEnumerator)enumeratorObject);
        }
        finally
        {
            Marshal.ReleaseComObject(enumeratorObject);
        }
    }

    private static T WithDevice<T>(string deviceId, Func<IMMDevice, T> operation) => WithEnumerator(enumerator =>
    {
        EnsureSuccess(enumerator.GetDevice(deviceId, out var device));
        try
        {
            EnsureSuccess(device.GetState(out var state));
            if ((state & ActiveDeviceState) == 0)
            {
                throw new InvalidOperationException("The audio device is no longer active. Refresh the device list and try again.");
            }

            return operation(device);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    });

    private static T WithEndpointVolume<T>(string deviceId, Func<IAudioEndpointVolume, T> operation) => WithDevice(deviceId, device =>
    {
        var interfaceId = typeof(IAudioEndpointVolume).GUID;
        EnsureSuccess(device.Activate(ref interfaceId, InProcessServer, IntPtr.Zero, out var activated));
        try
        {
            return operation((IAudioEndpointVolume)activated);
        }
        finally
        {
            Marshal.ReleaseComObject(activated);
        }
    });

    private static void ValidateDeviceId(string deviceId) => ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

    private static void ValidateFlow(AudioDataFlow flow)
    {
        if (!Enum.IsDefined(flow))
        {
            throw new ArgumentOutOfRangeException(nameof(flow));
        }
    }

    private static void ValidateRole(AudioDeviceRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }
}

internal static class AudioComInterop
{
    internal static void EnsureSuccess(int result)
    {
        if (result < 0)
        {
            throw new COMException(Marshal.GetExceptionForHR(result)?.Message ?? "A Windows audio operation failed.", result);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey(Guid formatId, uint propertyId)
    {
        internal Guid FormatId = formatId;
        internal uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CountedArray
    {
        internal uint Count;
        internal IntPtr Pointer;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        internal const ushort WideStringType = 31;

        [FieldOffset(0)]
        internal ushort VariantType;

        [FieldOffset(8)]
        internal IntPtr Pointer;

        // The largest native union member determines the structure size on both 32-bit and 64-bit Windows.
        [FieldOffset(8)]
        internal CountedArray Array;

        internal readonly string? ReadString() => VariantType == WideStringType ? Marshal.PtrToStringUni(Pointer) : null;
    }

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant value);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MultimediaDeviceEnumerator;

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(AudioDataFlow flow, uint stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(AudioDataFlow flow, AudioDeviceRole role, out IMMDevice? endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IMMDevice endpoint);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object activated);

        [PreserveSig]
        int OpenPropertyStore(uint accessMode, out IPropertyStore store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr callback);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr callback);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float decibels, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float scalar, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float decibels);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float scalar);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float decibels, ref Guid context);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float scalar, ref Guid context);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float decibels);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float scalar);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);

        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);

        [PreserveSig]
        int VolumeStepUp(ref Guid context);

        [PreserveSig]
        int VolumeStepDown(ref Guid context);

        [PreserveSig]
        int QueryHardwareSupport(out uint mask);

        [PreserveSig]
        int GetVolumeRange(out float minimumDecibels, out float maximumDecibels, out float incrementDecibels);
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        // Windows exposes SetDefaultEndpoint after ten policy methods; removing unused slots changes its native address.
        [PreserveSig]
        int UnusedGetMixFormat();

        [PreserveSig]
        int UnusedGetDeviceFormat();

        [PreserveSig]
        int UnusedResetDeviceFormat();

        [PreserveSig]
        int UnusedSetDeviceFormat();

        [PreserveSig]
        int UnusedGetProcessingPeriod();

        [PreserveSig]
        int UnusedSetProcessingPeriod();

        [PreserveSig]
        int UnusedGetShareMode();

        [PreserveSig]
        int UnusedSetShareMode();

        [PreserveSig]
        int UnusedGetPropertyValue();

        [PreserveSig]
        int UnusedSetPropertyValue();

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, AudioDeviceRole role);
    }
}
