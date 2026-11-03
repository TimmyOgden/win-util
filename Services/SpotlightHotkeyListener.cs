using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinUtil.Models;

namespace WinUtil.Services;

internal sealed class SpotlightHotkeyListener : IDisposable
{
    internal const int AlreadyRegisteredErrorCode = 1409;
    private const int HotkeyMessage = 0x0312;

    private readonly HwndSource source;
    private readonly HwndSourceHook sourceHook;
    private readonly Action toggleSpotlight;
    private readonly GlobalShortcut shortcut;
    private readonly int hotkeyId;
    private readonly Action<IntPtr, int> unregister;
    private bool hotkeyRegistered;

    internal SpotlightHotkeyListener(IntPtr sourceWindowHandle, Action toggleSpotlight, GlobalShortcut shortcut, int hotkeyId)
        : this(sourceWindowHandle, toggleSpotlight, shortcut, hotkeyId, RegisterNativeHotkey, UnregisterNativeHotkey)
    {
    }

    internal SpotlightHotkeyListener(
        IntPtr sourceWindowHandle,
        Action toggleSpotlight,
        GlobalShortcut shortcut,
        int hotkeyId,
        Action<IntPtr, int, GlobalShortcut> register,
        Action<IntPtr, int> unregister)
    {
        ArgumentNullException.ThrowIfNull(toggleSpotlight);
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(unregister);

        if (!shortcut.IsValid)
        {
            throw new ArgumentException("The Spotlight shortcut must include modifiers and a regular key.", nameof(shortcut));
        }

        source = HwndSource.FromHwnd(sourceWindowHandle)
            ?? throw new InvalidOperationException("The widget window must have a Win32 source before registering the Spotlight shortcut.");
        this.toggleSpotlight = toggleSpotlight;
        this.shortcut = shortcut;
        this.hotkeyId = hotkeyId;
        this.unregister = unregister;
        sourceHook = HandleWindowMessage;
        source.AddHook(sourceHook);

        try
        {
            register(sourceWindowHandle, hotkeyId, shortcut);
        }
        catch
        {
            source.RemoveHook(sourceHook);
            throw;
        }

        hotkeyRegistered = true;
    }

    public void Dispose()
    {
        if (!hotkeyRegistered)
        {
            return;
        }

        if (!source.IsDisposed)
        {
            try
            {
                unregister(source.Handle, hotkeyId);
            }
            catch (Win32Exception exception)
            {
                throw new Win32Exception(exception.NativeErrorCode, $"Unable to unregister the {ShortcutFormatter.Format(shortcut)} shortcut. {exception.Message}");
            }
        }

        hotkeyRegistered = false;
        if (!source.IsDisposed)
        {
            source.RemoveHook(sourceHook);
        }
    }

    private IntPtr HandleWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != HotkeyMessage)
        {
            return IntPtr.Zero;
        }

        if (wParam.ToInt32() != hotkeyId)
        {
            return IntPtr.Zero;
        }

        toggleSpotlight();
        handled = true;
        return IntPtr.Zero;
    }

    private static void RegisterNativeHotkey(IntPtr windowHandle, int hotkeyId, GlobalShortcut shortcut)
    {
        if (!RegisterHotKey(windowHandle, hotkeyId, shortcut.Modifiers, shortcut.VirtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to register the {ShortcutFormatter.Format(shortcut)} shortcut.");
        }
    }

    private static void UnregisterNativeHotkey(IntPtr windowHandle, int hotkeyId)
    {
        if (!UnregisterHotKey(windowHandle, hotkeyId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int identifier, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int identifier);
}
