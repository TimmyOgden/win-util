using System.IO;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;

namespace WinUtil.Tests;

internal static class SpotlightDiscoveryTests
{
    internal static void FavoritesPersistAndRefreshAcrossPaletteInstances()
    {
        var saved = new SpotlightPreferences();
        var tools = SpotlightActionCatalog.Create(() => WidgetSettings.Default);
        var palette = new SpotlightViewModel(tools, getPreferences: () => saved, savePreferences: value => saved = value);
        palette.ToggleFavorite(SpotlightActionIds.Password);
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.Password }, saved.FavoriteActionIds);
        TestAssert.Equal(SpotlightActionIds.Password, TestAssert.Single(palette.MatchingActions).Id);
        TestAssert.True(palette.MatchingActions[0].IsFavorite);
        TestAssert.Equal("Favorites and recent commands", palette.ResultsLabel);
        var reopened = new SpotlightViewModel(SpotlightActionCatalog.Create(() => WidgetSettings.Default), getPreferences: () => saved);
        TestAssert.Equal(SpotlightActionIds.Password, TestAssert.Single(reopened.MatchingActions).Id);
        palette.ToggleFavorite(SpotlightActionIds.Password);
        TestAssert.Empty(saved.FavoriteActionIds);
        TestAssert.Equal(tools.Count, palette.MatchingActions.Count);
        TestAssert.False(tools.Single(action => action.Id == SpotlightActionIds.Password).IsFavorite);
    }

    internal static void FailedFavoriteSavePreservesTheSavedStateAndReportsTheError()
    {
        var saved = new SpotlightPreferences();
        var palette = new SpotlightViewModel(SpotlightActionCatalog.Create(() => WidgetSettings.Default), getPreferences: () => saved,
            savePreferences: _ => throw new IOException("Disk unavailable."));
        palette.ToggleFavorite(SpotlightActionIds.Password);
        TestAssert.Empty(saved.FavoriteActionIds);
        TestAssert.False(palette.MatchingActions.Single(action => action.Id == SpotlightActionIds.Password).IsFavorite);
        TestAssert.True(palette.ErrorMessage.Contains("Favorite could not be saved.", StringComparison.Ordinal));
        palette.Query = "uuid";
        TestAssert.True(palette.HasError);
    }

    internal static void WorkspaceRecordsOnlySuccessfulRunsAndNeverCopyOrInvalidInput()
    {
        var saved = new SpotlightPreferences();
        var saves = 0;
        var copies = 0;
        var palette = new SpotlightViewModel(SpotlightActionCatalog.Create(() => WidgetSettings.Default), _ => copies++, () => saved, value => { saved = value; saves++; });
        palette.Query = "format json";
        palette.OpenWorkspace();
        palette.Workspace.Run();
        palette.Workspace.InputText = "not valid json";
        palette.Workspace.Run();
        TestAssert.Empty(saved.RecentActionIds);
        palette.Workspace.InputText = "{\"secret input\":42}";
        palette.Workspace.Run();
        var output = palette.Workspace.OutputText;
        palette.Workspace.Copy();
        palette.Workspace.Copy();
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.FormatJson }, saved.RecentActionIds);
        TestAssert.Equal(1, saves);
        TestAssert.Equal(2, copies);
        TestAssert.Equal(output, palette.Workspace.OutputText);
        saved = saved with { RememberRecentCommands = false, RecentActionIds = [] };
        palette.Workspace.Run();
        TestAssert.Empty(saved.RecentActionIds);
        TestAssert.Equal(1, saves);
    }

    internal static void HistorySaveFailureKeepsTheProducedResultWithoutRerunning()
    {
        var generations = 0;
        var copies = new List<string>();
        var tool = SpotlightAction.CreateGenerator(SpotlightActionIds.Password, "Test", "Generate password", "", [], () => $"sensitive result {++generations}");
        var palette = new SpotlightViewModel([tool], copies.Add, savePreferences: _ => throw new UnauthorizedAccessException("Access denied."));
        palette.OpenWorkspace();
        palette.Workspace.Run();
        TestAssert.Equal(1, generations);
        TestAssert.Equal("sensitive result 1", palette.Workspace.OutputText);
        TestAssert.True(palette.Workspace.CanCopy);
        TestAssert.True(palette.Workspace.ErrorMessage.StartsWith("The command completed, but recent commands could not be saved.", StringComparison.Ordinal));
        palette.Workspace.Copy();
        TestAssert.SequenceEqual(new[] { "sensitive result 1" }, copies);
        TestAssert.Equal(1, generations);
        palette.Reset();
        TestAssert.True(palette.HasError);
    }

    internal static void FavoriteChangesNotifyBindingsOnlyWhenTheValueChanges()
    {
        var tool = SpotlightActionCatalog.Create(() => WidgetSettings.Default)[0];
        var changes = new List<string?>();
        tool.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        tool.SetFavorite(true);
        tool.SetFavorite(true);
        tool.SetFavorite(false);
        TestAssert.SequenceEqual(new[] { nameof(SpotlightAction.IsFavorite), nameof(SpotlightAction.IsFavorite) }, changes);
    }

    internal static void ResetReadsLatestSavedHistoryAndHandlesEmptyCatalogs()
    {
        var saved = new SpotlightPreferences { RecentActionIds = [SpotlightActionIds.Password] };
        var palette = new SpotlightViewModel(SpotlightActionCatalog.All, getPreferences: () => saved);
        saved = saved.RecordSuccess(SpotlightActionIds.Uuid);
        palette.Query = "json";
        palette.Reset();
        TestAssert.SequenceEqual(saved.RecentActionIds, palette.MatchingActions.Select(action => action.Id));
        TestAssert.Equal(SpotlightActionIds.Uuid, palette.SelectedAction?.Id);
        var empty = new SpotlightViewModel([]) { Query = null! };
        empty.MoveSelection(int.MaxValue);
        empty.Reset();
        TestAssert.Empty(empty.MatchingActions);
        TestAssert.Equal<SpotlightAction?>(null, empty.SelectedAction);
    }
}
