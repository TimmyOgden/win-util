using System.ComponentModel;
using WinUtil.Models;

namespace WinUtil.Services;

internal interface ISpotlightShortcutManager : IDisposable
{
    void BeginShortcutCapture(Action<ShortcutCaptureResult> captureCallback);

    void EndShortcutCapture();

    string? TryApply(SpotlightShortcuts shortcuts);
}

internal sealed class SpotlightShortcutManager : ISpotlightShortcutManager
{
    private const int FirstPaletteHotkeyId = 1;
    private const int SecondPaletteHotkeyId = 2;

    private readonly Func<IPhysicalShortcutListener> createActionListener;
    private readonly Func<GlobalShortcut, int, IDisposable> createPaletteListener;
    private IPhysicalShortcutListener? actionShortcutListener;
    private IDisposable? paletteHotkeyListener;
    private GlobalShortcut? registeredPalette;
    private int nextPaletteHotkeyId = FirstPaletteHotkeyId;
    private bool isCapturing;

    internal SpotlightShortcutManager(IntPtr sourceWindowHandle, Action toggleSpotlight, Action<string> executeAction)
    {
        createActionListener = () => new PhysicalShortcutListener(executeAction);
        createPaletteListener = (shortcut, hotkeyId) => new SpotlightHotkeyListener(sourceWindowHandle, () =>
        {
            if (!isCapturing)
            {
                toggleSpotlight();
            }
        }, shortcut, hotkeyId);
    }

    internal SpotlightShortcutManager(
        Func<IPhysicalShortcutListener> createActionListener,
        Func<GlobalShortcut, int, IDisposable> createPaletteListener)
    {
        ArgumentNullException.ThrowIfNull(createActionListener);
        ArgumentNullException.ThrowIfNull(createPaletteListener);
        this.createActionListener = createActionListener;
        this.createPaletteListener = createPaletteListener;
    }

    public void Dispose()
    {
        actionShortcutListener?.Dispose();
        actionShortcutListener = null;
        paletteHotkeyListener?.Dispose();
        paletteHotkeyListener = null;
        registeredPalette = null;
    }

    internal void BeginShortcutCapture(Action<ShortcutCaptureResult> captureCallback)
    {
        if (actionShortcutListener is null)
        {
            captureCallback(ShortcutCaptureResult.Error("Windows could not listen for Spotlight shortcuts."));
            return;
        }

        isCapturing = true;
        actionShortcutListener.BeginCapture(captureCallback);
    }

    internal void EndShortcutCapture()
    {
        isCapturing = false;
        actionShortcutListener?.EndCapture();
    }

    internal string? TryApply(SpotlightShortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var normalizedShortcuts = shortcuts.Normalize();
        var validationError = normalizedShortcuts.Validate();

        if (validationError is not null)
        {
            return validationError;
        }

        if (actionShortcutListener is null)
        {
            try
            {
                actionShortcutListener = createActionListener();
                actionShortcutListener.UpdateBindings(normalizedShortcuts.GetBindings());
            }
            catch (Win32Exception exception)
            {
                return exception.Message;
            }
        }

        var registrationError = TryApplyPalette(normalizedShortcuts.Palette);

        if (registrationError is not null)
        {
            return registrationError;
        }

        actionShortcutListener.UpdateBindings(normalizedShortcuts.GetBindings());
        return null;
    }

    private string? TryApplyPalette(GlobalShortcut shortcut)
    {
        if (registeredPalette is { } current && current.Modifiers == shortcut.Modifiers && current.VirtualKey == shortcut.VirtualKey)
        {
            return null;
        }

        IDisposable replacement;

        try
        {
            // Windows hotkeys cannot be replaced atomically under the same registration identifier.
            replacement = createPaletteListener(shortcut, nextPaletteHotkeyId);
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode == SpotlightHotkeyListener.AlreadyRegisteredErrorCode
                ? $"{ShortcutFormatter.Format(shortcut)} is already in use by another app."
                : exception.Message;
        }

        try
        {
            paletteHotkeyListener?.Dispose();
        }
        catch (Win32Exception exception)
        {
            try
            {
                replacement.Dispose();
            }
            catch (Win32Exception rollbackException)
            {
                throw new AggregateException("The Spotlight shortcut could not be changed or rolled back.", exception, rollbackException);
            }

            return exception.Message;
        }

        paletteHotkeyListener = replacement;
        registeredPalette = shortcut;
        nextPaletteHotkeyId = nextPaletteHotkeyId == FirstPaletteHotkeyId ? SecondPaletteHotkeyId : FirstPaletteHotkeyId;
        return null;
    }

    void ISpotlightShortcutManager.BeginShortcutCapture(Action<ShortcutCaptureResult> captureCallback) => BeginShortcutCapture(captureCallback);

    void ISpotlightShortcutManager.EndShortcutCapture() => EndShortcutCapture();

    string? ISpotlightShortcutManager.TryApply(SpotlightShortcuts shortcuts) => TryApply(shortcuts);
}
