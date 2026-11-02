using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class SpotlightWorkspaceWindowTests
{
    private const double StandardDpi = 96;
    private const string SampleJson = "{\"name\":\"Ada Lovelace\",\"languages\":[\"C#\",\"TypeScript\"]}";

    internal static void WorkspaceRunsAndCopiesWithoutForegroundInsertion() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var copied = new List<string>();
        var injector = new TrackingTextInjector();
        var window = new SpotlightWindow(() => WidgetSettings.Default, copied.Add);
        using var controller = new SpotlightController(window, injector, () => WidgetSettings.Default);

        try
        {
            window.ShowPalette();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.Query = "format json";
            window.WorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            TestAssert.True(palette.IsWorkspaceOpen);
            TestAssert.False(window.PalettePanel.IsVisible);
            TestAssert.True(window.WorkspacePanel.IsVisible);
            TestAssert.True(window.WorkspaceInputTextBox.IsKeyboardFocused);
            TestAssert.True(window.WorkspaceInputTextBox.AcceptsReturn);
            TestAssert.True(window.WorkspaceOutputTextBox.IsReadOnly);
            TestAssert.Equal(SpotlightWorkspaceViewModel.MaximumInputLength, window.WorkspaceInputTextBox.MaxLength);
            TestAssert.False(window.WorkspaceCopyButton.IsEnabled);

            window.WorkspaceInputTextBox.Text = SampleJson;
            window.WorkspaceRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            TestAssert.True(window.WorkspaceCopyButton.IsEnabled);
            TestAssert.Equal(JsonTextFormatter.Format(SampleJson), window.WorkspaceOutputTextBox.Text);
            window.WorkspaceCopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(new[] { window.WorkspaceOutputTextBox.Text }, copied);
            TestAssert.Equal(0, injector.CaptureCalls);
            TestAssert.Equal(0, injector.InsertCalls);
            TestAssert.Equal(0, injector.ReplaceCalls);
            ExportPreviewWhenRequested(window);

            window.WorkspaceToolComboBox.SelectedItem = palette.Workspace.Tools.Single(action => action.Id == SpotlightActionIds.Password);
            UpdateLayout(window);
            TestAssert.False(window.WorkspaceInputPanel.IsVisible);
            TestAssert.Equal("Generate", window.WorkspaceRunButton.Content);
            TestAssert.False(window.WorkspaceCopyButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void WorkspaceKeyboardRoutingPreservesEditingAndClearsOnClose() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { });

        try
        {
            window.ShowPalette();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.Query = "uppercase";
            window.WorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            window.WorkspaceInputTextBox.Text = "first\nsecond";

            TestAssert.False(window.HandleWorkspaceKey(Key.Enter, ModifierKeys.None));
            TestAssert.False(window.HandleWorkspaceKey(Key.Up, ModifierKeys.None));
            TestAssert.False(window.HandleWorkspaceKey(Key.Down, ModifierKeys.None));
            TestAssert.False(window.HandleWorkspaceKey(Key.Enter, ModifierKeys.Shift));
            TestAssert.Equal(string.Empty, palette.Workspace.OutputText);
            TestAssert.True(window.HandleWorkspaceKey(Key.Enter, ModifierKeys.Control));
            TestAssert.Equal("FIRST\nSECOND", palette.Workspace.OutputText);

            window.BackToPaletteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            TestAssert.True(palette.IsPaletteOpen);
            TestAssert.Equal(string.Empty, palette.Workspace.InputText);
            TestAssert.Equal(string.Empty, palette.Workspace.OutputText);
            TestAssert.False(window.WorkspaceInputTextBox.CanUndo);
            TestAssert.True(window.WorkspaceInputTextBox.IsUndoEnabled);

            window.WorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            window.WorkspaceInputTextBox.Text = "secret";
            TestAssert.True(window.HandleWorkspaceKey(Key.Enter, ModifierKeys.Control));
            TestAssert.True(window.HandleWorkspaceKey(Key.Escape, ModifierKeys.None));
            TestAssert.False(window.IsVisible);
            TestAssert.False(palette.IsWorkspaceOpen);
            TestAssert.Equal(string.Empty, palette.Workspace.InputText);
            TestAssert.Equal(string.Empty, palette.Workspace.OutputText);
            TestAssert.False(window.WorkspaceInputTextBox.CanUndo);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void NormalPaletteEnterStillInsertsAndTransforms() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var injector = new TrackingTextInjector();
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { });
        using var controller = new SpotlightController(window, injector, () => WidgetSettings.Default);

        try
        {
            controller.Toggle();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.Query = "uuid";
            UpdateLayout(window);
            RaiseEnter(window);
            TestAssert.Equal(1, injector.CaptureCalls);
            TestAssert.Equal(1, injector.InsertCalls);
            TestAssert.True(Guid.TryParse(injector.InsertedText, out _));

            palette.Query = "uppercase";
            UpdateLayout(window);
            RaiseEnter(window);
            TestAssert.Equal(1, injector.ReplaceCalls);
            TestAssert.Equal("SELECTED TEXT", injector.ReplacementText);
            TestAssert.False(palette.IsWorkspaceOpen);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void WorkspaceCloseAndToggleClearSensitiveContent() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { });
        using var controller = new SpotlightController(window, new TrackingTextInjector(), () => WidgetSettings.Default);

        try
        {
            controller.Toggle();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.OpenWorkspace();
            palette.Workspace.InputText = "secret input";
            palette.Workspace.Run();
            controller.Toggle();
            TestAssert.False(window.IsVisible);
            TestAssert.Equal(string.Empty, palette.Workspace.InputText);
            TestAssert.Equal(string.Empty, palette.Workspace.OutputText);

            controller.Toggle();
            palette.OpenWorkspace();
            palette.Workspace.InputText = "another secret";
            palette.Workspace.Run();
            window.CloseWorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.IsVisible);
            TestAssert.Equal(string.Empty, palette.Workspace.InputText);
            TestAssert.Equal(string.Empty, palette.Workspace.OutputText);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void WorkspaceToolPickerRendersTitlesInSelectionAndDropdown() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { });

        try
        {
            window.ShowPalette();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.Query = "format json";
            window.WorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            var picker = window.WorkspaceToolComboBox;

            foreach (var tool in palette.Workspace.Tools)
            {
                picker.SelectedItem = tool;
                UpdateLayout(window);
                var selectionLabels = FindDescendants<TextBlock>(picker).Select(label => label.Text).ToArray();
                TestAssert.True(selectionLabels.Contains(tool.Title));
                TestAssert.False(selectionLabels.Contains(typeof(SpotlightAction).FullName));
            }

            picker.IsDropDownOpen = true;
            UpdateLayout(window);
            var popup = (Popup)picker.Template.FindName("Popup", picker);
            var dropdownLabels = FindDescendants<TextBlock>(popup.Child).Select(label => label.Text).ToArray();
            TestAssert.True(dropdownLabels.Contains("Generate UUID"));
            TestAssert.True(dropdownLabels.Contains("Format JSON"));
            TestAssert.False(dropdownLabels.Contains(typeof(SpotlightAction).FullName));
            picker.IsDropDownOpen = false;
        }
        finally
        {
            window.Close();
        }
    });

    internal static void FailingDirectShortcutPreservesOpenWorkspaceContent() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var injector = new TrackingTextInjector(new SelectedTextUnavailableException());
        var window = new SpotlightWindow(() => WidgetSettings.Default, _ => { });
        using var controller = new SpotlightController(window, injector, () => WidgetSettings.Default);

        try
        {
            window.ShowPalette();
            var palette = (SpotlightViewModel)window.DataContext;
            palette.Query = "format json";
            window.WorkspaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            window.WorkspaceInputTextBox.Text = SampleJson;
            window.WorkspaceRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout(window);
            var displayedOutput = window.WorkspaceOutputTextBox.Text;
            var selectedTool = palette.Workspace.SelectedTool;

            controller.ExecuteAction(SpotlightActionIds.Uppercase);
            UpdateLayout(window);

            TestAssert.Equal(1, injector.ReplaceCalls);
            TestAssert.True(window.IsVisible);
            TestAssert.True(palette.IsWorkspaceOpen);
            TestAssert.True(window.WorkspaceInputTextBox.IsKeyboardFocused);
            TestAssert.Equal(SampleJson, palette.Workspace.InputText);
            TestAssert.Equal(displayedOutput, palette.Workspace.OutputText);
            TestAssert.Equal(selectedTool, palette.Workspace.SelectedTool);
            TestAssert.True(palette.Workspace.CanCopy);
            TestAssert.True(palette.Workspace.HasError);
            TestAssert.Equal("Direct shortcut failed. Select text in the original app before running this tool.", palette.Workspace.ErrorMessage);
            TestAssert.Equal(string.Empty, palette.Workspace.StatusMessage);
        }
        finally
        {
            window.Close();
        }
    });

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RaiseEnter(SpotlightWindow window)
    {
        var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        window.CommandTextBox.RaiseEvent(keyEvent);
        TestAssert.True(keyEvent.Handled);
    }

    private static void UpdateLayout(SpotlightWindow window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void ExportPreviewWhenRequested(SpotlightWindow window)
    {
        var directory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        UpdateLayout(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            StandardDpi * dpi.DpiScaleX,
            StandardDpi * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "spotlight-workspace.png"));
        encoder.Save(stream);
    }

    private static void EnsureApplicationResources()
    {
        if (System.Windows.Application.Current is not null)
        {
            return;
        }

        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }

    private sealed class TrackingTextInjector(Exception? replacementFailure = null) : IForegroundTextInjector
    {
        internal int CaptureCalls { get; private set; }
        internal int InsertCalls { get; private set; }
        internal int ReplaceCalls { get; private set; }
        internal string InsertedText { get; private set; } = string.Empty;
        internal string ReplacementText { get; private set; } = string.Empty;

        public IntPtr CaptureForegroundWindow()
        {
            CaptureCalls++;
            return IntPtr.Zero;
        }

        public void InsertText(IntPtr targetWindow, string text)
        {
            InsertCalls++;
            InsertedText = text;
        }

        public Task ReplaceSelectedTextAsync(IntPtr targetWindow, Func<string, string> transformer)
        {
            ReplaceCalls++;
            if (replacementFailure is not null)
            {
                return Task.FromException(replacementFailure);
            }

            ReplacementText = transformer("selected text");
            return Task.CompletedTask;
        }
    }
}
