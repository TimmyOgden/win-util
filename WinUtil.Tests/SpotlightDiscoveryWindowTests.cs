using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class SpotlightDiscoveryWindowTests
{
    internal static void FavoritesKeepNativeKeyboardFocusAndDoNotExecuteCommands() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var saved = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.Uuid],
            RecentActionIds = [SpotlightActionIds.Password]
        };
        var requests = 0;
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { }, () => saved, value => saved = value);
        window.ActionRequested += (_, _) => requests++;
        try
        {
            window.ShowPalette();
            UpdateLayout(window);
            var palette = (SpotlightViewModel)window.DataContext;
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uuid, SpotlightActionIds.Password }, palette.MatchingActions.Select(action => action.Id));
            TestAssert.True(window.CommandTextBox.IsKeyboardFocused);
            TestAssert.Equal(SpotlightSearch.MaximumQueryLength, window.CommandTextBox.MaxLength);
            RaiseKey(window, Key.Down);
            TestAssert.Equal(SpotlightActionIds.Password, palette.SelectedAction?.Id);
            var passwordItem = (ListBoxItem)window.ActionResultsListBox.ItemContainerGenerator.ContainerFromItem(palette.SelectedAction)!;
            var favorite = FindDescendants<Button>(passwordItem).Single();
            favorite.Focus();
            RaiseKey(window, Key.Enter);
            TestAssert.Equal(0, requests);
            favorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uuid, SpotlightActionIds.Password }, saved.FavoriteActionIds);
            var updatedItem = (ListBoxItem)window.ActionResultsListBox.ItemContainerGenerator.ContainerFromItem(palette.SelectedAction)!;
            TestAssert.Equal("Unfavorite", FindDescendants<Button>(updatedItem).Single().Content);
            TestAssert.True(window.CommandTextBox.IsKeyboardFocused);
            TestAssert.Equal(0, requests);
            palette.Query = "passwrod";
            UpdateLayout(window);
            TestAssert.Equal(SpotlightActionIds.Password, palette.SelectedAction?.Id);
            RaiseKey(window, Key.Enter);
            TestAssert.Equal(1, requests);
            RaiseKey(window, Key.Escape);
            TestAssert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    internal static void DirectCommandsRecordSuccessWithoutRecordingFailuresOrRetryingSaveErrors() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var saved = new SpotlightPreferences();
        var saves = 0;
        var failSave = false;
        var injector = new DiscoveryTextInjector();
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { }, () => saved, value =>
        {
            saves++;
            if (failSave) { throw new IOException("Disk unavailable."); }
            saved = value;
        });
        using var controller = new SpotlightController(window, injector, () => WidgetSettings.Default);
        try
        {
            controller.ExecuteAction(SpotlightActionIds.Uuid);
            controller.ExecuteAction(SpotlightActionIds.Uppercase);
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uppercase, SpotlightActionIds.Uuid }, saved.RecentActionIds);
            TestAssert.Equal(2, saves);
            injector.Failure = new SelectedTextUnavailableException();
            controller.ExecuteAction(SpotlightActionIds.Lowercase);
            TestAssert.Equal(2, saves);
            TestAssert.Equal(2, saved.RecentActionIds.Length);
            injector.Failure = null;
            failSave = true;
            controller.ExecuteAction(SpotlightActionIds.Password);
            var palette = (SpotlightViewModel)window.DataContext;
            TestAssert.Equal(2, injector.InsertCalls);
            TestAssert.Equal(3, saves);
            TestAssert.True(palette.ErrorMessage.StartsWith("The command completed, but recent commands could not be saved.", StringComparison.Ordinal));
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uppercase, SpotlightActionIds.Uuid }, saved.RecentActionIds);
            window.ShowPalette();
            TestAssert.True(palette.HasError);
        }
        finally { window.Close(); }
    });

    internal static void SelectedTextHistoryWaitsForSuccessfulReplacementCompletion() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var saved = new SpotlightPreferences();
        var pending = new TaskCompletionSource();
        var injector = new DiscoveryTextInjector { PendingReplacement = pending.Task };
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { }, () => saved, value => saved = value);
        using var controller = new SpotlightController(window, injector, () => WidgetSettings.Default);
        try
        {
            window.ShowPalette();
            UpdateLayout(window);
            controller.ExecuteAction(SpotlightActionIds.Uppercase);
            TestAssert.Empty(saved.RecentActionIds);
            pending.SetResult();
            UpdateLayout(window);
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uppercase }, saved.RecentActionIds);
        }
        finally { window.Close(); }
    });

    private static void RaiseKey(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    });

    private static void UpdateLayout(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void EnsureApplicationResources()
    {
        if (System.Windows.Application.Current is not null) { return; }
        RunOnStaThread(() =>
        {
            // A separate dispatcher prevents layout tests from starting the tray application.
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
        });
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) { yield return match; }
            foreach (var descendant in FindDescendants<T>(child)) { yield return descendant; }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) { throw new InvalidOperationException(failure.ToString(), failure); }
    }

    private sealed class DiscoveryTextInjector : IForegroundTextInjector
    {
        internal int InsertCalls { get; private set; }
        internal Exception? Failure { get; set; }
        internal Task? PendingReplacement { get; init; }
        public IntPtr CaptureForegroundWindow() => IntPtr.Zero;
        public void InsertText(IntPtr targetWindow, string text) => InsertCalls++;
        public Task ReplaceSelectedTextAsync(IntPtr targetWindow, Func<string, string> transformer)
        {
            if (Failure is not null) { return Task.FromException(Failure); }
            transformer("selected text");
            return PendingReplacement ?? Task.CompletedTask;
        }
    }
}
