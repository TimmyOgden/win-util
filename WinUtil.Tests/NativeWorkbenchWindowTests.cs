using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class NativeWorkbenchWindowTests
{
    private const double StandardDpi = 96;
    private const double MaximumCompactWidgetHeight = 320;
    private const double MaximumResultHeight = 288;
    private const double MaximumItemWidthDifference = 24;
    private const double TestOpacity = 0.8;
    private const double TestBackgroundOpacity = 0.7;
    private const double PreviewOutputVolume = 60;
    private const string PreviewClock = "09:41:00";
    private const string PreviewDate = "Friday, 16 October";

    internal static void WidgetKeepsCompactDeviceRowsAndSavedAppearance() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var settings = WidgetSettings.Default with
        {
            KeepOnTop = false,
            Opacity = TestOpacity,
            BackgroundOpacity = TestBackgroundOpacity,
            ShowSeconds = false
        };
        var window = new WidgetWindow(
            settings,
            _ => { },
            new SyntheticBatteryProvider(new BatterySnapshot([new("Laptop", 86), new("Wireless keyboard", 72)], null)),
            new SyntheticAudioProvider(new AudioDeviceSnapshot("Studio speakers", "USB microphone", null)
            {
                OutputDevices = [new("studio-speakers", "Studio speakers")],
                InputDevices = [new("usb-microphone", "USB microphone")],
                DefaultOutputDeviceId = "studio-speakers",
                DefaultInputDeviceId = "usb-microphone",
                OutputVolumePercent = PreviewOutputVolume,
                IsMicrophoneMuted = false
            }));

        try
        {
            window.Show();
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(2, window.BatteryDevicesItemsControl.Items.Count);
            TestAssert.Equal(Visibility.Visible, window.BatteryDevicesItemsControl.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.BatteryEmptyText.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.BatteryStatusText.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.AudioStatusText.Visibility);
            TestAssert.Equal("Studio speakers", window.OutputDeviceText.Text);
            TestAssert.Equal("USB microphone", window.InputDeviceText.Text);
            TestAssert.Equal(TestOpacity, window.Opacity);
            TestAssert.Equal(TestBackgroundOpacity, window.WidgetBackgroundBrush.Opacity);
            TestAssert.False(window.Topmost);
            TestAssert.Equal(1, window.TimeText.Text.Count(character => character == ':'));
            TestAssert.True(window.ActualHeight < MaximumCompactWidgetHeight);
            window.TimeText.Text = PreviewClock;
            window.DateText.Text = PreviewDate;
            ExportPreviewWhenRequested(window, "widget.png");
        }
        finally
        {
            window.AllowClose();
        }
    });

    internal static void WidgetShowsEmptyBatteryAndUnavailableProviders() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new WidgetWindow(
            WidgetSettings.Default with { KeepOnTop = false },
            _ => { },
            new SyntheticBatteryProvider(new BatterySnapshot([], "Battery access unavailable.")),
            new SyntheticAudioProvider(new AudioDeviceSnapshot(null, null, "Audio access unavailable.")));

        try
        {
            window.Show();
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(Visibility.Collapsed, window.BatteryDevicesItemsControl.Visibility);
            TestAssert.Equal(Visibility.Visible, window.BatteryEmptyText.Visibility);
            TestAssert.Equal(Visibility.Visible, window.BatteryStatusText.Visibility);
            TestAssert.Equal(Visibility.Visible, window.AudioStatusText.Visibility);
            TestAssert.Equal("No active output device", window.OutputDeviceText.Text);
            TestAssert.Equal("No active input device", window.InputDeviceText.Text);
            window.TimeText.Text = PreviewClock;
            window.DateText.Text = PreviewDate;
            ExportPreviewWhenRequested(window, "widget-unavailable.png");
        }
        finally
        {
            window.AllowClose();
        }
    });

    internal static void SpotlightStretchesSelectedResultsAndHandlesNoMatches() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default);

        try
        {
            window.Show();
            var viewModel = (SpotlightViewModel)window.DataContext;
            viewModel.Query = "json";
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(2, window.ActionResultsListBox.Items.Count);
            TestAssert.Equal("json", window.CommandTextBox.Text);
            TestAssert.Equal(Visibility.Visible, window.ActionResultsListBox.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.NoResultsPanel.Visibility);
            var selectedItem = (ListBoxItem)window.ActionResultsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedAction)!;
            TestAssert.True(selectedItem.IsSelected);
            TestAssert.Equal(HorizontalAlignment.Stretch, selectedItem.HorizontalContentAlignment);
            TestAssert.True(window.ActionResultsListBox.ActualWidth - selectedItem.ActualWidth <= MaximumItemWidthDifference);
            ExportPreviewWhenRequested(window, "spotlight.png");

            viewModel.Query = "no such tool";
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(0, window.ActionResultsListBox.Items.Count);
            TestAssert.Equal(Visibility.Collapsed, window.ActionResultsListBox.Visibility);
            TestAssert.Equal(Visibility.Visible, window.NoResultsPanel.Visibility);
            ExportPreviewWhenRequested(window, "spotlight-no-results.png");

            viewModel.Query = string.Empty;
            UpdateBindingsAndLayout(window);
            TestAssert.False(window.NoResultsPanel.IsVisible);
            TestAssert.Equal(string.Empty, window.CommandTextBox.Text);
            ExportPreviewWhenRequested(window, "spotlight-empty.png");
        }
        finally
        {
            window.Close();
        }
    });

    internal static void SpotlightKeepsLongResultListsAndKeyboardSelectionVisible() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default);

        try
        {
            window.Show();
            var viewModel = (SpotlightViewModel)window.DataContext;
            viewModel.Query = "e";
            UpdateBindingsAndLayout(window);
            TestAssert.True(window.ActionResultsListBox.Items.Count > 6);
            var scroller = FindDescendants<ScrollViewer>(window.ActionResultsListBox).Single();
            TestAssert.True(scroller.ScrollableHeight > 0);
            TestAssert.True(window.ActionResultsListBox.ActualHeight <= MaximumResultHeight);
            viewModel.SelectedAction = viewModel.MatchingActions.First();
            viewModel.MoveSelection(window.ActionResultsListBox.Items.Count - 1);
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(viewModel.MatchingActions.Last(), viewModel.SelectedAction);
            var selectedItem = (ListBoxItem)window.ActionResultsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedAction)!;
            TestAssert.True(selectedItem.IsSelected);
            var selectedPosition = selectedItem.TranslatePoint(new System.Windows.Point(), scroller);
            TestAssert.True(selectedPosition.Y >= 0);
            TestAssert.True(selectedPosition.Y + selectedItem.ActualHeight <= scroller.ActualHeight);
            ExportPreviewWhenRequested(window, "spotlight-scrolled.png");
        }
        finally
        {
            window.Close();
        }
    });

    internal static void SpotlightReportsMissingSelectionForEveryTextTool() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new SpotlightWindow(() => WidgetSettings.Default);
        using var controller = new SpotlightController(window, new MissingSelectionTextInjector(), () => WidgetSettings.Default);

        try
        {
            var viewModel = (SpotlightViewModel)window.DataContext;

            foreach (var action in SpotlightActionCatalog.All.Where(action => action.RequiresSelectedText))
            {
                viewModel.Reset();
                controller.ExecuteAction(action.Id);

                TestAssert.True(viewModel.HasError);
                TestAssert.Equal("Select text in the original app before running this tool.", viewModel.ErrorMessage);
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static void ExportPreviewWhenRequested(Window window, string fileName)
    {
        var previewDirectory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");

        if (string.IsNullOrWhiteSpace(previewDirectory))
        {
            return;
        }

        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var rendered = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            StandardDpi * dpi.DpiScaleX,
            StandardDpi * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        Directory.CreateDirectory(previewDirectory);
        using var stream = File.Create(System.IO.Path.Combine(previewDirectory, fileName));
        encoder.Save(stream);
    }

    private static void UpdateBindingsAndLayout(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
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

    private sealed class MissingSelectionTextInjector : IForegroundTextInjector
    {
        public IntPtr CaptureForegroundWindow() => IntPtr.Zero;

        public void InsertText(IntPtr targetWindow, string text) =>
            throw new InvalidOperationException("Text tools must use selected-text replacement.");

        public Task ReplaceSelectedTextAsync(IntPtr targetWindow, Func<string, string> transformer) =>
            Task.FromException(new SelectedTextUnavailableException());
    }

    private sealed class SyntheticBatteryProvider(BatterySnapshot snapshot) : IBatteryStatusProvider
    {
        public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class SyntheticAudioProvider(AudioDeviceSnapshot snapshot) : IAudioDeviceProvider
    {
        public AudioDeviceSnapshot GetSnapshot() => snapshot;
        public AudioOperationResult SetDefaultOutput(string deviceId) => throw new InvalidOperationException("Read-only audio fake received an output command.");
        public AudioOperationResult SetDefaultInput(string deviceId) => throw new InvalidOperationException("Read-only audio fake received an input command.");
        public AudioOperationResult SetOutputVolume(double percentage) => throw new InvalidOperationException("Read-only audio fake received a volume command.");
        public AudioOperationResult SetMicrophoneMute(bool muted) => throw new InvalidOperationException("Read-only audio fake received a mute command.");
    }
}
