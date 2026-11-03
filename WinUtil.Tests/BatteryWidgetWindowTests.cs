using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class BatteryWidgetWindowTests
{
    private const int LowPercentage = 15;
    private const int RecoveredPercentage = 75;
    private const int LowerThreshold = 10;
    private const double CompactHeightLimit = 320;
    private const double ControlTestWidth = 100;
    private const double ControlTestHeight = 30;

    internal static void BatteryRowsShowKnownPowerStatusAndUnavailablePercentages() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeBatteryProvider(new BatterySnapshot(
        [
            new("Laptop", LowPercentage) { Id = BatteryDevice.SystemBatteryId, IsCharging = true, IsPluggedIn = true },
            new("Keyboard", null) { Id = "keyboard" }
        ], null));
        var window = CreateWindow(provider);
        try
        {
            window.Show();
            UpdateLayout(window);
            var labels = FindDescendants<TextBlock>(window.BatteryDevicesItemsControl).ToArray();
            TestAssert.True(labels.Any(label => label.Text == "Charging" && label.IsVisible));
            TestAssert.True(labels.Any(label => label.Text == $"{LowPercentage}%"));
            TestAssert.True(labels.Any(label => label.Text == "Unavailable"));
            TestAssert.True(labels.Any(label => label.Text == "Charging" && label.ToolTip is string tooltip && tooltip.Length > 0));
            TestAssert.Equal(1, labels.Count(label => label.Text == "Charging"));
            TestAssert.True(window.ActualHeight < CompactHeightLimit);
            provider.Snapshot = new BatterySnapshot([new("Laptop", RecoveredPercentage) { Id = BatteryDevice.SystemBatteryId, IsCharging = false, IsPluggedIn = true }], null);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            UpdateLayout(window);
            TestAssert.True(FindDescendants<TextBlock>(window.BatteryDevicesItemsControl).Any(label => label.Text == "Plugged in" && label.IsVisible));
        }
        finally { window.AllowClose(); }
    });

    internal static void BatteryRefreshAlertsOncePerLowEpisodeAndUsesCurrentSettings() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeBatteryProvider(Snapshot(LowPercentage));
        var notifications = new List<IReadOnlyList<BatteryDevice>>();
        var window = CreateWindow(provider, notifications.Add);
        try
        {
            TestAssert.Equal(1, notifications.Count);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            TestAssert.Equal(1, notifications.Count);
            provider.Snapshot = Snapshot(RecoveredPercentage);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            provider.Snapshot = Snapshot(LowPercentage);
            window.ApplySettings(window.CurrentSettings with { BatteryAlerts = new BatteryAlertSettings { Enabled = true, ThresholdPercent = LowerThreshold } });
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            TestAssert.Equal(1, notifications.Count);
            window.ApplySettings(window.CurrentSettings with { BatteryAlerts = new BatteryAlertSettings { Enabled = true } });
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            TestAssert.Equal(2, notifications.Count);
            TestAssert.Equal("keyboard", TestAssert.Single(notifications[0]).Id);
            window.ApplySettings(window.CurrentSettings with { BatteryAlerts = new BatteryAlertSettings { Enabled = false } });
            provider.Snapshot = Snapshot(RecoveredPercentage);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            provider.Snapshot = Snapshot(LowPercentage);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            TestAssert.Equal(2, notifications.Count);
        }
        finally { window.AllowClose(); }
    });

    internal static void BatteryRefreshSuppressesChargingAndReportsProviderErrors() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeBatteryProvider(new BatterySnapshot([new("Laptop", LowPercentage) { Id = BatteryDevice.SystemBatteryId, IsCharging = true, IsPluggedIn = true }], null));
        var notifications = new List<IReadOnlyList<BatteryDevice>>();
        var window = CreateWindow(provider, notifications.Add);
        try
        {
            window.Show();
            UpdateLayout(window);
            TestAssert.Empty(notifications);
            provider.Snapshot = new BatterySnapshot([], "Bluetooth access unavailable.", "System power status unavailable.");
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            UpdateLayout(window);
            TestAssert.Equal(Visibility.Visible, window.BatteryStatusText.Visibility);
            TestAssert.Equal(Visibility.Visible, window.BatteryEmptyText.Visibility);
            TestAssert.True(((string)window.BatteryStatusText.ToolTip).Contains("Bluetooth access unavailable.", StringComparison.Ordinal));
            TestAssert.True(((string)window.BatteryStatusText.ToolTip).Contains("System power status unavailable.", StringComparison.Ordinal));
            TestAssert.Empty(notifications);
            provider.Snapshot = Snapshot(LowPercentage);
            window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
            UpdateLayout(window);
            TestAssert.Equal(1, notifications.Count);
            TestAssert.Equal(Visibility.Collapsed, window.BatteryStatusText.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.BatteryEmptyText.Visibility);
            TestAssert.Equal<object?>(null, window.BatteryStatusText.ToolTip);
        }
        finally { window.AllowClose(); }
    });

    internal static void ClosingWidgetIgnoresLateBatteryResultsAndCancelsRefresh() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeBatteryProvider(Snapshot(RecoveredPercentage));
        var notifications = new List<IReadOnlyList<BatteryDevice>>();
        var window = CreateWindow(provider, notifications.Add);
        var completion = new TaskCompletionSource<BatterySnapshot>();
        provider.PendingSnapshot = completion.Task;
        var refresh = window.UpdateBatteryStatusAsync();
        var calls = provider.Calls;
        window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
        TestAssert.Equal(calls, provider.Calls);
        window.AllowClose();
        TestAssert.True(provider.LastCancellation.IsCancellationRequested);
        completion.SetResult(Snapshot(LowPercentage));
        UpdateLayout(window);
        TestAssert.True(refresh.IsCompletedSuccessfully);
        TestAssert.Empty(notifications);
        TestAssert.Equal(RecoveredPercentage, ((IReadOnlyList<BatteryDevice>)window.BatteryDevicesItemsControl.ItemsSource)[0].Percentage);
        window.UpdateBatteryStatusAsync().GetAwaiter().GetResult();
        TestAssert.Equal(calls, provider.Calls);
    });

    internal static void WidgetDraggingIgnoresInteractiveControls() => RunOnStaThread(() =>
    {
        TestAssert.False(WidgetWindow.CanDragFrom(null));
        TestAssert.False(WidgetWindow.CanDragFrom(new Button()));
        TestAssert.False(WidgetWindow.CanDragFrom(new TextBox()));
        TestAssert.False(WidgetWindow.CanDragFrom(new ComboBox()));
        TestAssert.False(WidgetWindow.CanDragFrom(new Slider()));
        TestAssert.True(WidgetWindow.CanDragFrom(new TextBlock { Text = "Background label" }));
        var button = new Button { Content = new TextBlock { Text = "Mute" } };
        button.Measure(new Size(ControlTestWidth, ControlTestHeight));
        button.Arrange(new Rect(0, 0, ControlTestWidth, ControlTestHeight));
        button.ApplyTemplate();
        TestAssert.False(WidgetWindow.CanDragFrom((TextBlock)button.Content));
    });

    private static WidgetWindow CreateWindow(FakeBatteryProvider provider, Action<IReadOnlyList<BatteryDevice>>? notify = null) =>
        new(WidgetSettings.Default with { KeepOnTop = false }, _ => { }, provider, new FakeAudioProvider(), notify);

    private static BatterySnapshot Snapshot(int percentage) => new([new("Keyboard", percentage) { Id = "keyboard" }], null);

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

    private sealed class FakeBatteryProvider(BatterySnapshot snapshot) : IBatteryStatusProvider
    {
        internal BatterySnapshot Snapshot { get; set; } = snapshot;
        internal Task<BatterySnapshot>? PendingSnapshot { get; set; }
        internal CancellationToken LastCancellation { get; private set; }
        internal int Calls { get; private set; }
        public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Calls++;
            LastCancellation = cancellationToken;
            return PendingSnapshot ?? Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeAudioProvider : IAudioDeviceProvider
    {
        public AudioDeviceSnapshot GetSnapshot() => new("Speakers", "Microphone", null);
        public AudioOperationResult SetDefaultOutput(string deviceId) => throw new InvalidOperationException("Read-only audio fake received an output command.");
        public AudioOperationResult SetDefaultInput(string deviceId) => throw new InvalidOperationException("Read-only audio fake received an input command.");
        public AudioOperationResult SetOutputVolume(double percentage) => throw new InvalidOperationException("Read-only audio fake received a volume command.");
        public AudioOperationResult SetMicrophoneMute(bool muted) => throw new InvalidOperationException("Read-only audio fake received a mute command.");
    }
}
