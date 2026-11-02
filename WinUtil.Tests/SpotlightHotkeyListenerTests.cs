using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class SpotlightHotkeyListenerTests
{
    private const int TestHotkeyId = 19;
    private const int OtherHotkeyId = 20;
    private const int HotkeyMessage = 0x0312;
    private const int OtherMessage = 0;
    private const uint LetterKVirtualKey = 0x4B;
    private static readonly GlobalShortcut TestShortcut = new()
    {
        Modifiers = GlobalShortcut.ControlModifier | GlobalShortcut.ShiftModifier,
        VirtualKey = LetterKVirtualKey
    };

    internal static void NativeListenerUsesConfiguredShortcutAndRoutesOnlyItsRegistration() => RunOnStaThread(() =>
    {
        using var source = CreateSource();
        var toggleCount = 0;
        var registrations = new List<(IntPtr Window, int Id, GlobalShortcut Shortcut)>();
        var unregistrations = new List<(IntPtr Window, int Id)>();
        var listener = new SpotlightHotkeyListener(source.Handle, () => toggleCount++, TestShortcut, TestHotkeyId,
            (window, id, shortcut) => registrations.Add((window, id, shortcut)),
            (window, id) => unregistrations.Add((window, id)));

        try
        {
            TestAssert.Equal((source.Handle, TestHotkeyId, TestShortcut), registrations.Single());
            SendMessage(source.Handle, HotkeyMessage, new IntPtr(OtherHotkeyId), IntPtr.Zero);
            SendMessage(source.Handle, OtherMessage, new IntPtr(TestHotkeyId), IntPtr.Zero);
            TestAssert.Equal(0, toggleCount);
            SendMessage(source.Handle, HotkeyMessage, new IntPtr(TestHotkeyId), IntPtr.Zero);
            TestAssert.Equal(1, toggleCount);

            listener.Dispose();
            listener.Dispose();

            TestAssert.Equal((source.Handle, TestHotkeyId), unregistrations.Single());
            SendMessage(source.Handle, HotkeyMessage, new IntPtr(TestHotkeyId), IntPtr.Zero);
            TestAssert.Equal(1, toggleCount);
        }
        finally
        {
            listener.Dispose();
        }
    });

    internal static void FailedNativeRegistrationRemovesItsWindowHook() => RunOnStaThread(() =>
    {
        using var source = CreateSource();
        var toggled = false;
        var unregistered = false;
        var failed = false;

        try
        {
            using var listener = new SpotlightHotkeyListener(source.Handle, () => toggled = true, TestShortcut, TestHotkeyId,
                (_, _, _) => throw new Win32Exception(SpotlightHotkeyListener.AlreadyRegisteredErrorCode),
                (_, _) => unregistered = true);
        }
        catch (Win32Exception exception)
        {
            TestAssert.Equal(SpotlightHotkeyListener.AlreadyRegisteredErrorCode, exception.NativeErrorCode);
            failed = true;
        }

        SendMessage(source.Handle, HotkeyMessage, new IntPtr(TestHotkeyId), IntPtr.Zero);
        TestAssert.True(failed);
        TestAssert.False(toggled);
        TestAssert.False(unregistered);
    });

    internal static void DestroyedWindowDoesNotNeedHotkeyUnregistration() => RunOnStaThread(() =>
    {
        var source = CreateSource();
        var unregistered = false;
        using var listener = new SpotlightHotkeyListener(source.Handle, () => { }, TestShortcut, TestHotkeyId,
            (_, _, _) => { }, (_, _) => unregistered = true);

        source.Dispose();
        listener.Dispose();

        TestAssert.False(unregistered);
    });

    private static HwndSource CreateSource() => new(new HwndSourceParameters("WinUtil shortcut test") { WindowStyle = 0 });

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);
}
