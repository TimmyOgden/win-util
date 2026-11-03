using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.ViewModels;

internal sealed class SpotlightViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<SpotlightAction> actions;
    private readonly ObservableCollection<SpotlightAction> matchingActions = [];
    private readonly Func<SpotlightPreferences>? getPreferences;
    private readonly Action<SpotlightPreferences>? savePreferences;
    private SpotlightPreferences localPreferences = new();
    private string preferenceErrorMessage = string.Empty;
    private string errorMessage = string.Empty;
    private string query = string.Empty;
    private SpotlightAction? selectedAction;
    private bool isWorkspaceOpen;

    internal SpotlightViewModel(
        IReadOnlyList<SpotlightAction> actions,
        Action<string>? copyText = null,
        Func<SpotlightPreferences>? getPreferences = null,
        Action<SpotlightPreferences>? savePreferences = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        this.actions = actions;
        this.getPreferences = getPreferences;
        this.savePreferences = savePreferences;
        Workspace = new SpotlightWorkspaceViewModel(actions, copyText ?? System.Windows.Clipboard.SetText, RecordSuccessfulAction);
        MatchingActions = new ReadOnlyObservableCollection<SpotlightAction>(matchingActions);
        RefreshMatches();
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value)
            {
                return;
            }

            errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasMatches => MatchingActions.Count > 0;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public string ResultsLabel => HasQuery ? "Matching commands" : HasSuggestions ? "Favorites and recent commands" : "All commands";

    private bool HasSuggestions => GetPreferences().FavoriteActionIds.Length > 0 || GetPreferences().RecentActionIds.Length > 0;

    public SpotlightWorkspaceViewModel Workspace { get; }

    public bool IsWorkspaceOpen => isWorkspaceOpen;

    public bool IsPaletteOpen => !isWorkspaceOpen;

    public ReadOnlyObservableCollection<SpotlightAction> MatchingActions { get; }

    public string Query
    {
        get => query;
        set
        {
            var nextQuery = value ?? string.Empty;

            if (query == nextQuery)
            {
                return;
            }

            query = nextQuery;
            ErrorMessage = preferenceErrorMessage;
            RefreshMatches();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQuery));
        }
    }

    public SpotlightAction? SelectedAction
    {
        get => selectedAction;
        set
        {
            if (selectedAction == value)
            {
                return;
            }

            selectedAction = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void MoveSelection(int offset)
    {
        if (MatchingActions.Count == 0)
        {
            return;
        }

        var selectedIndex = SelectedAction is null ? 0 : MatchingActions.IndexOf(SelectedAction);
        selectedIndex = Math.Max(selectedIndex, 0);
        var nextIndex = (selectedIndex + offset) % MatchingActions.Count;

        SelectedAction = MatchingActions[nextIndex < 0 ? nextIndex + MatchingActions.Count : nextIndex];
    }

    internal void Reset()
    {
        CloseWorkspace();
        query = string.Empty;
        ErrorMessage = preferenceErrorMessage;
        SelectedAction = null;
        RefreshMatches();
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(HasQuery));
    }

    internal void ShowInsertionError(string message) => ErrorMessage = message;

    internal void ToggleFavorite(string actionId)
    {
        var preferences = GetPreferences().ToggleFavorite(actionId);
        SavePreferences(preferences, "Favorite could not be saved.");
    }

    internal void RecordSuccessfulAction(string actionId)
    {
        var preferences = GetPreferences();
        if (!preferences.RememberRecentCommands)
        {
            return;
        }

        SavePreferences(preferences.RecordSuccess(actionId), "The command completed, but recent commands could not be saved.");
    }

    private SpotlightPreferences GetPreferences() => (getPreferences?.Invoke() ?? localPreferences).Normalize();

    private void SavePreferences(SpotlightPreferences preferences, string failureMessage)
    {
        try
        {
            savePreferences?.Invoke(preferences);
            localPreferences = preferences;
            preferenceErrorMessage = string.Empty;
            ErrorMessage = string.Empty;
            RefreshMatches();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            preferenceErrorMessage = $"{failureMessage} {exception.Message}";
            ErrorMessage = preferenceErrorMessage;
            if (IsWorkspaceOpen)
            {
                Workspace.ShowPreferenceError(preferenceErrorMessage);
            }
        }
    }

    internal void OpenWorkspace()
    {
        Workspace.Reset(SelectedAction);
        isWorkspaceOpen = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsWorkspaceOpen));
        OnPropertyChanged(nameof(IsPaletteOpen));
    }

    internal void CloseWorkspace()
    {
        Workspace.Reset();
        isWorkspaceOpen = false;
        OnPropertyChanged(nameof(IsWorkspaceOpen));
        OnPropertyChanged(nameof(IsPaletteOpen));
    }

    private void RefreshMatches()
    {
        var previousSelection = SelectedAction;
        matchingActions.Clear();

        var preferences = GetPreferences();
        var filteredActions = SpotlightSearch.Rank(actions, Query, preferences);

        foreach (var action in actions)
        {
            action.SetFavorite(preferences.FavoriteActionIds.Contains(action.Id, StringComparer.Ordinal));
        }

        foreach (var action in filteredActions)
        {
            matchingActions.Add(action);
        }

        SelectedAction = previousSelection is not null && MatchingActions.Contains(previousSelection)
            ? previousSelection
            : MatchingActions.FirstOrDefault();
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ResultsLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
