using System.ComponentModel;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class SpotlightShortcutManagerTests
{
    private const uint LetterJVirtualKey = 0x4A;
    private const uint LetterKVirtualKey = 0x4B;
    private const uint SampleScanCode = 0x25;
    private const int UnregistrationErrorCode = 5;
    private static readonly GlobalShortcut CustomPalette = new()
    {
        Modifiers = GlobalShortcut.ControlModifier | GlobalShortcut.AltModifier,
        VirtualKey = LetterKVirtualKey
    };
    private static readonly GlobalShortcut ActionShortcut = new()
    {
        Modifiers = GlobalShortcut.ControlModifier,
        VirtualKey = LetterJVirtualKey
    };

    internal static void ChangingPaletteRegistersReplacementBeforeReleasingPreviousShortcut()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        TestAssert.Equal<string?>(null, manager.TryApply(new SpotlightShortcuts()));
        var previousId = native.Active.Keys.Single();

        TestAssert.Equal<string?>(null, manager.TryApply(new SpotlightShortcuts { Palette = CustomPalette, Uuid = ActionShortcut }));

        TestAssert.Equal(2, native.Attempts.Count);
        TestAssert.True(native.Attempts.Last().PreviousIds.Contains(previousId));
        TestAssert.False(native.Active.ContainsKey(previousId));
        TestAssert.Equal(CustomPalette, native.Active.Values.Single());
        TestAssert.Equal(new SpotlightShortcutBinding(SpotlightActionIds.Uuid, ActionShortcut), actions.Bindings.Single());
    }

    internal static void ConflictingPaletteKeepsPreviousRegistrationAndActionBindings()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        var initial = new SpotlightShortcuts { Uuid = ActionShortcut };
        TestAssert.Equal<string?>(null, manager.TryApply(initial));
        native.BlockedShortcut = CustomPalette;

        var error = manager.TryApply(new SpotlightShortcuts { Palette = CustomPalette, Password = ActionShortcut });

        TestAssert.Equal("Ctrl+Alt+K is already in use by another app.", error);
        TestAssert.Equal(SpotlightShortcuts.DefaultPalette, native.Active.Values.Single());
        TestAssert.SequenceEqual(initial.GetBindings(), actions.Bindings);
    }

    internal static void StartupPaletteConflictStillAllowsCaptureAndDirectActions()
    {
        var native = new RegistrationStub { BlockedShortcut = SpotlightShortcuts.DefaultPalette };
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        var initial = new SpotlightShortcuts { Uuid = ActionShortcut };

        TestAssert.Equal("Alt+2 is already in use by another app.", manager.TryApply(initial));
        TestAssert.SequenceEqual(initial.GetBindings(), actions.Bindings);
        ShortcutCaptureResult? captured = null;
        manager.BeginShortcutCapture(result => captured = result);
        actions.CaptureCallback!(ShortcutCaptureResult.Capture(CustomPalette));
        TestAssert.Equal(CustomPalette, captured?.Shortcut);
        manager.EndShortcutCapture();
        TestAssert.Equal<Action<ShortcutCaptureResult>?>(null, actions.CaptureCallback);

        TestAssert.Equal<string?>(null, manager.TryApply(initial with { Palette = CustomPalette }));
        TestAssert.Equal(CustomPalette, native.Active.Values.Single());
        TestAssert.False(actions.IsDisposed);
    }

    internal static void UnchangedPaletteUpdatesActionsWithoutNativeReregistration()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        TestAssert.Equal<string?>(null, manager.TryApply(new SpotlightShortcuts { Palette = CustomPalette }));

        TestAssert.Equal<string?>(null, manager.TryApply(new SpotlightShortcuts
        {
            Palette = CustomPalette with { ScanCode = SampleScanCode },
            Uuid = ActionShortcut
        }));

        TestAssert.Equal(1, native.Attempts.Count);
        TestAssert.Equal(SpotlightActionIds.Uuid, actions.Bindings.Single().ActionId);
    }

    internal static void FailedUnregistrationRollsBackReplacementShortcut()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        var initial = new SpotlightShortcuts { Uuid = ActionShortcut };
        TestAssert.Equal<string?>(null, manager.TryApply(initial));
        native.UnregistrationFailureId = native.Active.Keys.Single();

        try
        {
            TestAssert.Equal("Test unregister failure.", manager.TryApply(new SpotlightShortcuts { Palette = CustomPalette }));
            TestAssert.Equal(SpotlightShortcuts.DefaultPalette, native.Active.Values.Single());
            TestAssert.SequenceEqual(initial.GetBindings(), actions.Bindings);
        }
        finally
        {
            native.UnregistrationFailureId = null;
        }
    }

    internal static void InvalidBindingsDoNotChangeRunningShortcuts()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        using var manager = new SpotlightShortcutManager(() => actions, native.Register);
        var initial = new SpotlightShortcuts { Uuid = ActionShortcut };
        TestAssert.Equal<string?>(null, manager.TryApply(initial));

        TestAssert.Equal("Ctrl+J is reserved for opening Spotlight.", manager.TryApply(initial with { Palette = ActionShortcut }));
        TestAssert.Equal(1, native.Attempts.Count);
        TestAssert.SequenceEqual(initial.GetBindings(), actions.Bindings);
    }

    internal static void DisposeReleasesBothListenersAndCanBeRepeated()
    {
        var native = new RegistrationStub();
        var actions = new ActionListenerStub();
        var manager = new SpotlightShortcutManager(() => actions, native.Register);
        TestAssert.Equal<string?>(null, manager.TryApply(new SpotlightShortcuts()));

        manager.Dispose();
        manager.Dispose();

        TestAssert.True(actions.IsDisposed);
        TestAssert.Equal(0, native.Active.Count);
    }

    private sealed class ActionListenerStub : IPhysicalShortcutListener
    {
        internal IReadOnlyList<SpotlightShortcutBinding> Bindings { get; private set; } = [];
        internal Action<ShortcutCaptureResult>? CaptureCallback { get; private set; }
        internal bool IsDisposed { get; private set; }

        public void BeginCapture(Action<ShortcutCaptureResult> captureCallback) => CaptureCallback = captureCallback;

        public void EndCapture() => CaptureCallback = null;

        public void UpdateBindings(IReadOnlyList<SpotlightShortcutBinding> bindings) => Bindings = bindings;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RegistrationStub
    {
        internal Dictionary<int, GlobalShortcut> Active { get; } = [];
        internal List<(int Id, IReadOnlyList<int> PreviousIds)> Attempts { get; } = [];
        internal GlobalShortcut? BlockedShortcut { get; set; }
        internal int? UnregistrationFailureId { get; set; }

        internal IDisposable Register(GlobalShortcut shortcut, int id)
        {
            Attempts.Add((id, Active.Keys.ToArray()));

            if (shortcut == BlockedShortcut)
            {
                throw new Win32Exception(SpotlightHotkeyListener.AlreadyRegisteredErrorCode);
            }

            Active.Add(id, shortcut);
            return new RegistrationLease(() =>
            {
                if (UnregistrationFailureId == id)
                {
                    throw new Win32Exception(UnregistrationErrorCode, "Test unregister failure.");
                }

                Active.Remove(id);
            });
        }
    }

    private sealed class RegistrationLease(Action unregister) : IDisposable
    {
        private bool isDisposed;

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            unregister();
            isDisposed = true;
        }
    }
}
