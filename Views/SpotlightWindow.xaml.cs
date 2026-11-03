using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace WinUtil.Views;

public partial class SpotlightWindow : Window
{
    private const double WindowEdgePadding = 12;
    private readonly SpotlightViewModel viewModel;

    internal SpotlightWindow(
        Func<WidgetSettings> settingsProvider,
        Action<string>? copyText = null,
        Func<SpotlightPreferences>? getPreferences = null,
        Action<SpotlightPreferences>? savePreferences = null)
    {
        InitializeComponent();
        viewModel = new SpotlightViewModel(SpotlightActionCatalog.Create(settingsProvider), copyText, getPreferences, savePreferences);
        DataContext = viewModel;
        WorkspaceInputTextBox.MaxLength = SpotlightWorkspaceViewModel.MaximumInputLength;
        CommandTextBox.MaxLength = SpotlightSearch.MaximumQueryLength;
        MaxHeight = Math.Min(MaxHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        WorkspacePanel.MaxHeight = Math.Min(WorkspacePanel.MaxHeight, MaxHeight - WindowEdgePadding * 2);
        Closed += (_, _) =>
        {
            viewModel.Reset();
            ClearWorkspaceUndoHistory();
        };
    }

    internal event EventHandler<SpotlightAction>? ActionRequested;

    internal void RecordSuccessfulAction(string actionId) => viewModel.RecordSuccessfulAction(actionId);

    internal void ShowPalette()
    {
        viewModel.Reset();
        ClearWorkspaceUndoHistory();

        if (!IsVisible)
        {
            Show();
        }

        FocusSearchBox();
    }

    internal void RestoreAfterInsertionFailure(string errorMessage)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (viewModel.IsWorkspaceOpen)
        {
            viewModel.Workspace.ShowDirectActionError(errorMessage);
            FocusWorkspace();
            return;
        }

        viewModel.ShowInsertionError(errorMessage);
        FocusSearchBox();
        CommandTextBox.SelectAll();
    }

    private void HandlePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (viewModel.IsWorkspaceOpen)
        {
            e.Handled = HandleWorkspaceKey(e.Key, Keyboard.Modifiers);
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClosePalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.FocusedElement is not ButtonBase)
        {
            e.Handled = true;
            RequestSelectedAction();
            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            viewModel.MoveSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
    }

    private void HandleActionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return;
            }

            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }

        RequestSelectedAction();
    }

    private void HandleToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId })
        {
            throw new InvalidOperationException("The favorite button must identify a command.");
        }

        viewModel.ToggleFavorite(actionId);
        e.Handled = true;
        FocusSearchBox();
    }

    private void HandleActionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ActionResultsListBox.SelectedItem is { } selectedAction)
        {
            ActionResultsListBox.ScrollIntoView(selectedAction);
        }
    }

    private void HandleDeactivated(object? sender, EventArgs e)
    {
        if (!viewModel.IsWorkspaceOpen)
        {
            Hide();
        }
    }

    internal void ClosePalette()
    {
        viewModel.Reset();
        ClearWorkspaceUndoHistory();
        Hide();
    }

    private void HandleWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || !double.IsFinite(Top))
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - ActualHeight));
    }

    private void ClearWorkspaceUndoHistory()
    {
        WorkspaceInputTextBox.IsUndoEnabled = false;
        WorkspaceInputTextBox.IsUndoEnabled = true;
    }

    internal bool HandleWorkspaceKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape)
        {
            ClosePalette();
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.Control)
        {
            viewModel.Workspace.Run();
            return true;
        }

        return false;
    }

    private void HandleOpenWorkspace(object sender, RoutedEventArgs e)
    {
        viewModel.OpenWorkspace();
        FocusWorkspace();
    }

    private void FocusWorkspace()
    {
        Activate();
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || !viewModel.IsWorkspaceOpen)
            {
                return;
            }

            if (viewModel.Workspace.RequiresInput)
            {
                WorkspaceInputTextBox.Focus();
            }
            else
            {
                WorkspaceToolComboBox.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void HandleCloseWorkspace(object sender, RoutedEventArgs e)
    {
        viewModel.CloseWorkspace();
        ClearWorkspaceUndoHistory();
        FocusSearchBox();
    }

    private void HandleClosePalette(object sender, RoutedEventArgs e) => ClosePalette();

    private void HandleRunWorkspace(object sender, RoutedEventArgs e) => viewModel.Workspace.Run();

    private void HandleCopyWorkspace(object sender, RoutedEventArgs e) => viewModel.Workspace.Copy();

    private void FocusSearchBox()
    {
        Activate();
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || viewModel.IsWorkspaceOpen)
            {
                return;
            }

            Activate();
            CommandTextBox.Focus();
            Keyboard.Focus(CommandTextBox);
        }, DispatcherPriority.Input);
    }

    private void RequestSelectedAction()
    {
        if (viewModel.SelectedAction is { } selectedAction)
        {
            ActionRequested?.Invoke(this, selectedAction);
        }
    }
}
