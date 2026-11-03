using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class SpotlightShortcutsTests
{
    private const uint LetterJVirtualKey = 0x4A;
    private const uint SampleScanCode = 0x24;

    internal static void NormalizeRemovesInvalidShortcuts()
    {
        var shortcuts = new SpotlightShortcuts
        {
            Uuid = new GlobalShortcut { Modifiers = 0, VirtualKey = LetterJVirtualKey }
        };

        var normalized = shortcuts.Normalize();

        TestAssert.Equal<GlobalShortcut?>(null, normalized.Uuid);
    }

    internal static void NormalizePreservesValidShortcuts()
    {
        var shortcut = new GlobalShortcut
        {
            Modifiers = GlobalShortcut.ControlModifier | GlobalShortcut.ShiftModifier,
            VirtualKey = LetterJVirtualKey
        };
        var shortcuts = new SpotlightShortcuts { FormatJson = shortcut };

        var normalized = shortcuts.Normalize();

        TestAssert.Equal(shortcut, normalized.FormatJson);
    }

    internal static void ValidateRejectsDuplicateFunctionShortcuts()
    {
        var shortcut = new GlobalShortcut
        {
            Modifiers = GlobalShortcut.ControlModifier,
            VirtualKey = LetterJVirtualKey
        };
        var shortcuts = new SpotlightShortcuts { Password = shortcut, Uuid = shortcut };

        var error = shortcuts.Validate();

        TestAssert.Equal("Each Spotlight function needs a different shortcut.", error);
    }

    internal static void SupportedActionIdsMatchTheCatalog()
    {
        var catalogIds = SpotlightActionCatalog.All.Select(action => action.Id).Order().ToArray();
        var shortcutIds = SpotlightShortcuts.SupportedActionIds.Order().ToArray();

        TestAssert.SequenceEqual(catalogIds, shortcutIds);
    }

    internal static void WithShortcutRoundTripsThroughGetShortcut()
    {
        var shortcut = new GlobalShortcut { Modifiers = GlobalShortcut.AltModifier, VirtualKey = LetterJVirtualKey };

        var shortcuts = new SpotlightShortcuts().WithShortcut(SpotlightActionIds.UrlDecode, shortcut);

        TestAssert.Equal(shortcut, shortcuts.GetShortcut(SpotlightActionIds.UrlDecode));
        TestAssert.Equal(shortcut, shortcuts.UrlDecode);
        TestAssert.Equal<GlobalShortcut?>(null, shortcuts.GetShortcut(SpotlightActionIds.UrlEncode));
    }

    internal static void GetShortcutRejectsUnknownActions()
    {
        var threw = false;

        try
        {
            new SpotlightShortcuts().GetShortcut("not-an-action");
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        TestAssert.True(threw);
    }

    internal static void ValidateReservesThePaletteShortcut()
    {
        var shortcuts = new SpotlightShortcuts
        {
            JwtSecret = new GlobalShortcut
            {
                Modifiers = SpotlightShortcuts.PaletteModifiers,
                VirtualKey = SpotlightShortcuts.PaletteVirtualKey
            }
        };

        var error = shortcuts.Validate();

        TestAssert.Equal("Alt+2 is reserved for opening Spotlight.", error);
    }

    internal static void NormalizeRestoresMissingOrInvalidPaletteShortcuts()
    {
        foreach (var palette in new GlobalShortcut?[]
        {
            null,
            new() { Modifiers = 0, VirtualKey = LetterJVirtualKey },
            new() { Modifiers = GlobalShortcut.ControlModifier, VirtualKey = GlobalShortcut.MaximumVirtualKey + 1 },
            new() { Modifiers = GlobalShortcut.WindowsModifier, VirtualKey = LetterJVirtualKey }
        })
        {
            TestAssert.Equal(SpotlightShortcuts.DefaultPalette, (new SpotlightShortcuts { Palette = palette! }).Normalize().Palette);
        }
    }

    internal static void ValidateUsesTheConfiguredPaletteShortcut()
    {
        var palette = new GlobalShortcut { Modifiers = GlobalShortcut.ControlModifier, VirtualKey = LetterJVirtualKey };
        var shortcuts = new SpotlightShortcuts { Palette = palette, Uuid = SpotlightShortcuts.DefaultPalette };

        TestAssert.Equal<string?>(null, shortcuts.Validate());
        TestAssert.Equal(palette, shortcuts.Normalize().Palette);
        TestAssert.Equal("Ctrl+J is reserved for opening Spotlight.", (shortcuts with { Uuid = palette }).Validate());
        TestAssert.Equal("Ctrl+J is reserved for opening Spotlight.", (shortcuts with { Uuid = palette with { ScanCode = SampleScanCode } }).Validate());
        TestAssert.True((shortcuts with { Palette = null! }).Validate() is not null);
    }
}
