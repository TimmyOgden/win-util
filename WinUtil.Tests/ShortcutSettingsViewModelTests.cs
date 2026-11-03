using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;

namespace WinUtil.Tests;

internal static class ShortcutSettingsViewModelTests
{
    private const uint LetterKVirtualKey = 0x4B;

    private static readonly GlobalShortcut SampleShortcut = new()
    {
        Modifiers = GlobalShortcut.ControlModifier | GlobalShortcut.AltModifier,
        VirtualKey = LetterKVirtualKey
    };

    internal static void ConstructorExposesEveryCatalogAction()
    {
        var viewModel = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, new SpotlightShortcuts());

        TestAssert.Equal(SpotlightActionCatalog.All.Count, viewModel.Shortcuts.Count);
        TestAssert.Equal("Generate UUID", viewModel[SpotlightActionIds.Uuid].Title);
    }

    internal static void TextToolGroupsOnlyContainSelectedTextActionsInCatalogOrder()
    {
        var viewModel = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, new SpotlightShortcuts());

        var groupTitles = viewModel.TextToolGroups.Select(group => group.Title).ToArray();
        var groupedIds = viewModel.TextToolGroups.SelectMany(group => group.Shortcuts).Select(setting => setting.ActionId).ToArray();
        var expectedIds = SpotlightActionCatalog.All.Where(action => action.RequiresSelectedText).Select(action => action.Id).ToArray();

        TestAssert.SequenceEqual(
            [SpotlightActionCategories.Json, SpotlightActionCategories.Encoding, SpotlightActionCategories.Case],
            groupTitles);
        TestAssert.SequenceEqual(expectedIds, groupedIds);
    }

    internal static void ToSpotlightShortcutsRoundTripsAssignedShortcuts()
    {
        var initial = new SpotlightShortcuts { Password = SampleShortcut };
        var viewModel = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, initial);

        viewModel[SpotlightActionIds.Uppercase].Shortcut = SampleShortcut with { Modifiers = GlobalShortcut.ShiftModifier };
        viewModel[SpotlightActionIds.Password].Shortcut = null;
        var result = viewModel.ToSpotlightShortcuts();

        TestAssert.Equal<GlobalShortcut?>(null, result.Password);
        TestAssert.Equal(GlobalShortcut.ShiftModifier, result.Uppercase!.Modifiers);
        TestAssert.Equal(1, result.GetBindings().Count);
    }

    internal static void SettingAShortcutUpdatesDisplayTextAndNotifies()
    {
        var setting = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, new SpotlightShortcuts())[SpotlightActionIds.Uuid];
        var changedProperties = new List<string>();
        setting.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? string.Empty);

        TestAssert.Equal(ShortcutFormatter.UnassignedText, setting.DisplayText);
        TestAssert.False(setting.IsAssigned);

        setting.Shortcut = SampleShortcut;

        TestAssert.Equal("Ctrl+Alt+K", setting.DisplayText);
        TestAssert.True(setting.IsAssigned);
        TestAssert.SequenceEqual(["Shortcut", "DisplayText", "IsAssigned"], changedProperties);
    }

    internal static void SettingTheSameShortcutDoesNotNotify()
    {
        var setting = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, new SpotlightShortcuts { Uuid = SampleShortcut })[SpotlightActionIds.Uuid];
        var notified = false;
        setting.PropertyChanged += (_, _) => notified = true;

        setting.Shortcut = SampleShortcut with { };

        TestAssert.False(notified);
    }

    internal static void PaletteSettingRoundTripsSeparatelyFromActionShortcuts()
    {
        var viewModel = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, new SpotlightShortcuts { Palette = SampleShortcut });

        TestAssert.Equal(SampleShortcut, viewModel.Palette.Shortcut);
        TestAssert.Equal(viewModel.Palette, viewModel[ShortcutSettingsViewModel.PaletteSettingId]);
        TestAssert.Equal("Open Spotlight", viewModel.Palette.Title);
        TestAssert.Equal("Reset", viewModel.Palette.ClearLabel);
        TestAssert.Equal(SampleShortcut, viewModel.ToSpotlightShortcuts().Palette);
        TestAssert.Empty(viewModel.ToSpotlightShortcuts().GetBindings());

        viewModel.Palette.Shortcut = null;

        TestAssert.Equal(SpotlightShortcuts.DefaultPalette, viewModel.ToSpotlightShortcuts().Palette);
    }
}
