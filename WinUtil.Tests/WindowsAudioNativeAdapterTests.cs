using System.Reflection;
using System.Runtime.InteropServices;
using WinUtil.Models;
using WinUtil.Services;
using static WinUtil.Services.AudioComInterop;

namespace WinUtil.Tests;

internal static class WindowsAudioNativeAdapterTests
{
    internal static void NativeAdapterRejectsInvalidFlowAndRolesBeforeCallingWindows()
    {
        var adapter = new WindowsAudioNativeAdapter();
        var invalidFlow = (AudioDataFlow)int.MaxValue;
        var invalidRole = (AudioDeviceRole)int.MinValue;

        AssertThrows<ArgumentOutOfRangeException>(() => adapter.GetActiveEndpoints(invalidFlow));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.GetDefaultDeviceId(invalidFlow, AudioDeviceRole.Console));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.GetDefaultDeviceId(AudioDataFlow.Output, invalidRole));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetDefaultDevice("unused-device", invalidRole));
    }

    internal static void NativeAdapterRejectsMissingDeviceIdsBeforeCallingWindows()
    {
        var adapter = new WindowsAudioNativeAdapter();

        AssertThrows<ArgumentNullException>(() => adapter.SetDefaultDevice(null!, AudioDeviceRole.Console));
        AssertThrows<ArgumentException>(() => adapter.SetDefaultDevice(string.Empty, AudioDeviceRole.Console));
        AssertThrows<ArgumentNullException>(() => adapter.GetVolumePercent(null!));
        AssertThrows<ArgumentException>(() => adapter.GetVolumePercent(" "));
        AssertThrows<ArgumentException>(() => adapter.SetVolumePercent(string.Empty, 100));
        AssertThrows<ArgumentNullException>(() => adapter.SetVolumePercent(null!, 0));
        AssertThrows<ArgumentNullException>(() => adapter.GetMute(null!));
        AssertThrows<ArgumentException>(() => adapter.GetMute(string.Empty));
        AssertThrows<ArgumentNullException>(() => adapter.SetMute(null!, true));
        AssertThrows<ArgumentException>(() => adapter.SetMute(" ", false));
    }

    internal static void NativeAdapterRejectsNonfiniteAndOutOfRangeVolumeBeforeCallingWindows()
    {
        var adapter = new WindowsAudioNativeAdapter();
        const string unusedDevice = "unused-device";

        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetVolumePercent(unusedDevice, double.NaN));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetVolumePercent(unusedDevice, double.PositiveInfinity));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetVolumePercent(unusedDevice, double.NegativeInfinity));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetVolumePercent(unusedDevice, -1));
        AssertThrows<ArgumentOutOfRangeException>(() => adapter.SetVolumePercent(unusedDevice, 101));
    }

    internal static void EnsureSuccessAcceptsAllSuccessHResultsAndPreservesFailureCode()
    {
        const int success = 0;
        const int unchangedSuccess = 1;
        const int failure = unchecked((int)0x80004005);
        EnsureSuccess(success);
        EnsureSuccess(unchangedSuccess);
        EnsureSuccess(int.MaxValue);

        var exception = AssertThrows<COMException>(() => EnsureSuccess(failure));

        TestAssert.Equal(failure, exception.HResult);
    }

    internal static void EnsureSuccessNormalizesNativeFailuresToComExceptions()
    {
        const int notImplemented = unchecked((int)0x80004001);
        const int accessDenied = unchecked((int)0x80070005);

        TestAssert.Equal(notImplemented, AssertThrows<COMException>(() => EnsureSuccess(notImplemented)).HResult);
        TestAssert.Equal(accessDenied, AssertThrows<COMException>(() => EnsureSuccess(accessDenied)).HResult);
    }

    internal static void NativeAudioReadOnlySmokeWhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("WINUTIL_NATIVE_AUDIO_SMOKE") != "1")
        {
            return;
        }

        var provider = new AudioDeviceProvider(new WindowsAudioNativeAdapter());
        var snapshot = provider.GetSnapshot();

        Console.WriteLine($"Native audio: {snapshot.OutputDevices.Count} outputs, {snapshot.InputDevices.Count} inputs; errors: {snapshot.ErrorMessage ?? "none"}.");
        TestAssert.Equal<string?>(null, snapshot.ErrorMessage);
        TestAssert.True(snapshot.OutputDevices.All(device => !string.IsNullOrWhiteSpace(device.Id) && !string.IsNullOrWhiteSpace(device.Name)));
        TestAssert.True(snapshot.InputDevices.All(device => !string.IsNullOrWhiteSpace(device.Id) && !string.IsNullOrWhiteSpace(device.Name)));
    }

    internal static void ComInterfacesPreserveNativeHResultsAndIdentity()
    {
        AssertComInterface(typeof(IMMDeviceEnumerator), "A95664D2-9614-4F35-A746-DE8DB63617E6");
        AssertComInterface(typeof(IMMDeviceCollection), "0BD7A1BE-7A1A-44DB-8397-CC5392387B5E");
        AssertComInterface(typeof(IMMDevice), "D666063F-1587-4E43-81F1-B948E807363F");
        AssertComInterface(typeof(IPropertyStore), "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        AssertComInterface(typeof(IAudioEndpointVolume), "5CDF2C82-841E-4546-9722-0CF74078229A");
        AssertComInterface(typeof(IPolicyConfig), "F8679F50-850A-41CF-9C72-430F290290C8");
        TestAssert.Equal(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), typeof(MultimediaDeviceEnumerator).GUID);
        TestAssert.Equal(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), typeof(PolicyConfigClient).GUID);
    }

    internal static void ComDeviceVtablesMatchWindowsAbi()
    {
        TestAssert.SequenceEqual(["EnumAudioEndpoints", "GetDefaultAudioEndpoint", "GetDevice",
            "RegisterEndpointNotificationCallback", "UnregisterEndpointNotificationCallback"], MethodNames(typeof(IMMDeviceEnumerator)));
        TestAssert.SequenceEqual(["GetCount", "Item"], MethodNames(typeof(IMMDeviceCollection)));
        TestAssert.SequenceEqual(["Activate", "OpenPropertyStore", "GetId", "GetState"], MethodNames(typeof(IMMDevice)));
        TestAssert.SequenceEqual(["GetCount", "GetAt", "GetValue"], MethodNames(typeof(IPropertyStore)));
    }

    internal static void ComVolumeAndPolicyVtablesMatchWindowsAbi()
    {
        TestAssert.SequenceEqual(["RegisterControlChangeNotify", "UnregisterControlChangeNotify", "GetChannelCount",
            "SetMasterVolumeLevel", "SetMasterVolumeLevelScalar", "GetMasterVolumeLevel", "GetMasterVolumeLevelScalar",
            "SetChannelVolumeLevel", "SetChannelVolumeLevelScalar", "GetChannelVolumeLevel", "GetChannelVolumeLevelScalar",
            "SetMute", "GetMute", "GetVolumeStepInfo", "VolumeStepUp", "VolumeStepDown", "QueryHardwareSupport", "GetVolumeRange"],
            MethodNames(typeof(IAudioEndpointVolume)));
        const int policySetDefaultMethodIndex = 10;
        TestAssert.Equal("SetDefaultEndpoint", MethodNames(typeof(IPolicyConfig))[policySetDefaultMethodIndex]);
        TestAssert.Equal(0, (int)AudioDeviceRole.Console);
        TestAssert.Equal(1, (int)AudioDeviceRole.Multimedia);
        TestAssert.Equal(2, (int)AudioDeviceRole.Communications);
        TestAssert.Equal(0, (int)AudioDataFlow.Output);
        TestAssert.Equal(1, (int)AudioDataFlow.Input);
    }

    internal static void ComParametersUseWin32BoolAndUnicodeIds()
    {
        var muteSetter = typeof(IAudioEndpointVolume).GetMethod(nameof(IAudioEndpointVolume.SetMute))!;
        var muteGetter = typeof(IAudioEndpointVolume).GetMethod(nameof(IAudioEndpointVolume.GetMute))!;
        var deviceGetter = typeof(IMMDeviceEnumerator).GetMethod(nameof(IMMDeviceEnumerator.GetDevice))!;
        var policySetter = typeof(IPolicyConfig).GetMethod(nameof(IPolicyConfig.SetDefaultEndpoint))!;

        TestAssert.Equal<UnmanagedType?>(UnmanagedType.Bool, muteSetter.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>()?.Value);
        TestAssert.Equal<UnmanagedType?>(UnmanagedType.Bool, muteGetter.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>()?.Value);
        TestAssert.True(muteGetter.GetParameters()[0].IsOut);
        TestAssert.Equal<UnmanagedType?>(UnmanagedType.LPWStr, deviceGetter.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>()?.Value);
        TestAssert.Equal<UnmanagedType?>(UnmanagedType.LPWStr, policySetter.GetParameters()[0].GetCustomAttribute<MarshalAsAttribute>()?.Value);
    }

    internal static void PropertyStructLayoutsMatchNativeArchitecture()
    {
        const int pointerOffset = 8;
        const int wideVariantBytes = 24;
        const int narrowVariantBytes = 16;
        const int propertyKeyBytes = 20;
        const int propertyIdOffset = 16;

        TestAssert.Equal(IntPtr.Size == sizeof(long) ? wideVariantBytes : narrowVariantBytes, Marshal.SizeOf<PropVariant>());
        TestAssert.Equal(new IntPtr(pointerOffset), Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Pointer)));
        TestAssert.Equal(new IntPtr(pointerOffset), Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Array)));
        TestAssert.Equal(propertyKeyBytes, Marshal.SizeOf<PropertyKey>());
        TestAssert.Equal(new IntPtr(propertyIdOffset), Marshal.OffsetOf<PropertyKey>(nameof(PropertyKey.PropertyId)));
    }

    internal static void PropVariantReadsUnicodeAndClearsAllocatedMemory()
    {
        const string expected = "Microphone \u00e9 \u4e2d\u6587";
        var value = new PropVariant { VariantType = PropVariant.WideStringType, Pointer = Marshal.StringToCoTaskMemUni(expected) };
        try
        {
            TestAssert.Equal(expected, value.ReadString());
        }
        finally
        {
            EnsureSuccess(PropVariantClear(ref value));
        }

        TestAssert.Equal<ushort>(0, value.VariantType);
    }

    internal static void PropVariantHandlesEmptyNullAndUnsupportedValues()
    {
        var empty = new PropVariant { VariantType = PropVariant.WideStringType, Pointer = Marshal.StringToCoTaskMemUni(string.Empty) };
        try
        {
            TestAssert.Equal(string.Empty, empty.ReadString());
        }
        finally
        {
            EnsureSuccess(PropVariantClear(ref empty));
        }

        TestAssert.Equal<string?>(null, new PropVariant { VariantType = PropVariant.WideStringType }.ReadString());
        TestAssert.Equal<string?>(null, new PropVariant().ReadString());
    }

    private static string[] MethodNames(Type interfaceType) => interfaceType.GetMethods()
        .OrderBy(method => method.MetadataToken).Select(method => method.Name).ToArray();

    private static void AssertComInterface(Type interfaceType, string expectedGuid)
    {
        TestAssert.True(interfaceType.IsImport);
        TestAssert.Equal(new Guid(expectedGuid), interfaceType.GUID);
        TestAssert.Equal(ComInterfaceType.InterfaceIsIUnknown, interfaceType.GetCustomAttribute<InterfaceTypeAttribute>()!.Value);
        foreach (var method in interfaceType.GetMethods())
        {
            TestAssert.Equal(typeof(int), method.ReturnType);
            TestAssert.True((method.GetMethodImplementationFlags() & MethodImplAttributes.PreserveSig) != 0);
        }
    }

    private static TException AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}
