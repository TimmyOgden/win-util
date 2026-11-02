using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;

namespace WinUtil.Tests;

internal static class SpotlightViewModelTests
{
    internal static void InitialStateShowsAllActionsWithoutSuggestions()
    {
        var viewModel = new SpotlightViewModel(SpotlightActionCatalog.All);

        TestAssert.SequenceEqual(SpotlightActionCatalog.All.Select(action => action.Id), viewModel.MatchingActions.Select(action => action.Id));
        TestAssert.Equal(SpotlightActionIds.Uuid, viewModel.SelectedAction?.Id);
        TestAssert.Equal("All commands", viewModel.ResultsLabel);
    }

    internal static void QueryRetainsTheSelectionWhenItStillMatches()
    {
        var viewModel = new SpotlightViewModel(SpotlightActionCatalog.All);
        viewModel.MoveSelection(1);

        viewModel.Query = "password";

        TestAssert.Equal("password", viewModel.SelectedAction?.Id);
    }

    internal static void ResetClearsTheQueryAndShowsAvailableActions()
    {
        var viewModel = new SpotlightViewModel(SpotlightActionCatalog.All)
        {
            Query = "password"
        };

        viewModel.Reset();

        TestAssert.Equal(string.Empty, viewModel.Query);
        TestAssert.SequenceEqual(SpotlightActionCatalog.All.Select(action => action.Id), viewModel.MatchingActions.Select(action => action.Id));
        TestAssert.Equal(SpotlightActionIds.Uuid, viewModel.SelectedAction?.Id);
    }

    internal static void ChangingTheQueryClearsAnInsertionError()
    {
        var viewModel = new SpotlightViewModel(SpotlightActionCatalog.All);
        viewModel.ShowInsertionError("Could not insert the result.");

        viewModel.Query = "uuid";

        TestAssert.False(viewModel.HasError);
    }

    internal static void MoveSelectionDoesNothingWhenThereAreNoMatches()
    {
        var viewModel = new SpotlightViewModel(SpotlightActionCatalog.All)
        {
            Query = "does-not-exist"
        };

        viewModel.MoveSelection(1);

        TestAssert.Equal<SpotlightAction?>(null, viewModel.SelectedAction);
    }
}
