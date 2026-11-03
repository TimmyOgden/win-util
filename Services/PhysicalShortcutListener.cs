using System.ComponentModel;
using System.Runtime.InteropServices;
using WinUtil.Models;

namespace WinUtil.Services;

internal interface IPhysicalShortcutListener : IDisposable
{
    void BeginCapture(Action<ShortcutCaptureResult> captureCallback);

    void EndCapture();

    void UpdateBindings(IReadOnlyList<SpotlightShortcutBinding> bindings);
}

internal sealed class PhysicalShortcutListener : IPhysicalShortcutListener
{
    private const int ExtendedKeyFlag = 0x0001;
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;

    private readonly Action<string> executeAction;
    private readonly IntPtr hookHandle;
    private readonly HookProcedure hookProcedure;
    private readonly PhysicalShortcutProcessor processor = new();
    private readonly SynchronizationContext synchronizationContext;
    private Action<ShortcutCaptureResult>? captureCallback;

    internal PhysicalShortcutListener(Action<string> executeAction)
    {
        this.executeAction = executeAction;
        synchronizationContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("A UI synchronization context is required to listen for Spotlight shortcuts.");
        hookProcedure = HandleKeyboardEvent;
        hookHandle = SetWindowsHookEx(LowLevelKeyboardHook, hookProcedure, GetModuleHandle(null), 0);

        if (hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not listen for Spotlight shortcuts.");
        }
    }

    public void Dispose()
    {
        if (!UnhookWindowsHookEx(hookHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not stop listening for Spotlight shortcuts.");
        }
    }

    internal void BeginCapture(Action<ShortcutCaptureResult> captureCallback)
    {
        this.captureCallback = captureCallback;
        processor.BeginCapture();
    }

    internal void EndCapture()
    {
        captureCallback = null;
        processor.EndCapture();
    }

    internal void UpdateBindings(IReadOnlyList<SpotlightShortcutBinding> bindings) => processor.UpdateBindings(bindings);

    void IPhysicalShortcutListener.BeginCapture(Action<ShortcutCaptureResult> captureCallback) => BeginCapture(captureCallback);

    void IPhysicalShortcutListener.EndCapture() => EndCapture();

    void IPhysicalShortcutListener.UpdateBindings(IReadOnlyList<SpotlightShortcutBinding> bindings) => UpdateBindings(bindings);

    private IntPtr HandleKeyboardEvent(int code, IntPtr windowParameter, IntPtr keyboardData)
    {
        if (code < 0)
        {
            return CallNextHookEx(hookHandle, code, windowParameter, keyboardData);
        }

        var message = windowParameter.ToInt32();

        if (message is not (KeyDownMessage or KeyUpMessage or SystemKeyDownMessage or SystemKeyUpMessage))
        {
            return CallNextHookEx(hookHandle, code, windowParameter, keyboardData);
        }

        var nativeKeyboardData = Marshal.PtrToStructure<LowLevelKeyboardInput>(keyboardData);
        var keyboardEvent = new PhysicalKeyboardEvent(
            new PhysicalKey(
                nativeKeyboardData.VirtualKey,
                nativeKeyboardData.ScanCode,
                (nativeKeyboardData.Flags & ExtendedKeyFlag) != 0),
            message is KeyDownMessage or SystemKeyDownMessage);
        var processingResult = processor.Process(keyboardEvent);

        if (processingResult.CaptureResult is { } captureResult)
        {
            var callback = captureCallback
                ?? throw new InvalidOperationException("A Spotlight shortcut capture result has no destination.");

            if (captureResult.Shortcut is not null)
            {
                captureCallback = null;
            }

            synchronizationContext.Post(_ => callback(captureResult), null);
        }

        if (processingResult.ActionId is { } actionId)
        {
            synchronizationContext.Post(_ => executeAction(actionId), null);
        }

        return processingResult.Suppress
            ? new IntPtr(1)
            : CallNextHookEx(hookHandle, code, windowParameter, keyboardData);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr windowParameter, IntPtr keyboardData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookIdentifier, HookProcedure procedure, IntPtr moduleHandle, uint threadIdentifier);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    private delegate IntPtr HookProcedure(int code, IntPtr windowParameter, IntPtr keyboardData);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal int Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }
}
