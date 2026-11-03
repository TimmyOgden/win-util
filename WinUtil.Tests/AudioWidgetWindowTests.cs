using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class AudioWidgetWindowTests
{
    private const double InitialVolume = 50;
    private const double ChangedVolume = 30;
    private const double ExternalVolume = 75;
    private const double MaximumVolume = 100;
    private static readonly AudioEndpoint[] Outputs = [new("speakers", "Studio speakers"), new("headphones", "USB headphones")];
    private static readonly AudioEndpoint[] Inputs = [new("microphone", "USB microphone"), new("headset", "Headset microphone")];

    internal static void InitialAudioBindingsAndExternalRefreshNeverIssueCommands() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.Show();
            UpdateLayout(window);
            TestAssert.Empty(provider.Commands);
            TestAssert.Equal(Outputs[0], window.OutputDeviceComboBox.SelectedItem);
            TestAssert.Equal(Inputs[0], window.InputDeviceComboBox.SelectedItem);
            TestAssert.True(FindDescendants<TextBlock>(window.OutputDeviceComboBox).Any(label => label.Text == Outputs[0].Name));
            TestAssert.True(window.OutputVolumeSlider.IsEnabled);
            TestAssert.True(window.MicrophoneMuteButton.IsEnabled);
            provider.Snapshot = provider.Snapshot with
            {
                DefaultOutputDeviceId = Outputs[1].Id,
                OutputDeviceName = Outputs[1].Name,
                OutputVolumePercent = ExternalVolume,
                IsMicrophoneMuted = true
            };
            window.UpdateAudioStatus();
            UpdateLayout(window);
            TestAssert.Equal(Outputs[1], window.OutputDeviceComboBox.SelectedItem);
            TestAssert.Equal(ExternalVolume, window.OutputVolumeSlider.Value);
            TestAssert.Equal("Unmute", window.MicrophoneMuteButton.Content);
            TestAssert.Empty(provider.Commands);
            TestAssert.False(WidgetWindow.CanDragFrom(window.OutputDeviceComboBox));
            TestAssert.False(WidgetWindow.CanDragFrom(window.OutputVolumeSlider));
            TestAssert.False(WidgetWindow.CanDragFrom(window.MicrophoneMuteButton));
        }
        finally { window.AllowClose(); }
    });

    internal static void DeviceSelectionAndMicrophoneCommandsRefreshActualState() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.OutputDeviceComboBox.SelectedItem = Outputs[1];
            window.InputDeviceComboBox.SelectedItem = Inputs[1];
            window.MicrophoneMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(new[] { "output:headphones", "input:headset", "mute:True" }, provider.Commands);
            TestAssert.Equal(Outputs[1].Name, window.OutputDeviceText.Text);
            TestAssert.Equal(Inputs[1].Name, window.InputDeviceText.Text);
            TestAssert.Equal("Unmute", window.MicrophoneMuteButton.Content);
            TestAssert.Equal(Visibility.Collapsed, window.AudioStatusText.Visibility);
            window.MicrophoneMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal("mute:False", provider.Commands.Last());
            TestAssert.Equal("Mute", window.MicrophoneMuteButton.Content);
        }
        finally { window.AllowClose(); }
    });

    internal static void AudioCommandFailuresRestoreReportedStateAndRemainVisible() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider { FailureMessage = "The device was removed. Select another device." };
        var window = CreateWindow(provider);
        try
        {
            window.OutputDeviceComboBox.SelectedItem = Outputs[1];
            TestAssert.Equal(Outputs[0], window.OutputDeviceComboBox.SelectedItem);
            TestAssert.Equal(Visibility.Visible, window.AudioStatusText.Visibility);
            TestAssert.Equal(provider.FailureMessage, window.AudioStatusText.Text);
            window.UpdateAudioStatus();
            TestAssert.Equal(provider.FailureMessage, window.AudioStatusText.Text);
            window.MicrophoneMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal("Mute", window.MicrophoneMuteButton.Content);
            provider.FailureMessage = null;
            window.MicrophoneMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal("Unmute", window.MicrophoneMuteButton.Content);
            TestAssert.Equal(Visibility.Collapsed, window.AudioStatusText.Visibility);
        }
        finally { window.AllowClose(); }
    });

    internal static void OutputVolumeCommitsOnceAndPollingPreservesAnActiveAdjustment() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.BeginOutputVolumeAdjustment();
            window.OutputVolumeSlider.Value = ChangedVolume;
            TestAssert.Empty(provider.Commands);
            provider.Snapshot = provider.Snapshot with { OutputVolumePercent = ExternalVolume };
            window.UpdateAudioStatus();
            TestAssert.Equal(ChangedVolume, window.OutputVolumeSlider.Value);
            window.CommitOutputVolumeAdjustment();
            window.CommitOutputVolumeAdjustment();
            TestAssert.SequenceEqual(new[] { "volume:30" }, provider.Commands);
            TestAssert.Equal(ChangedVolume, window.OutputVolumeSlider.Value);
            window.BeginOutputVolumeAdjustment();
            window.CommitOutputVolumeAdjustment();
            TestAssert.Equal(1, provider.Commands.Count);
            window.OutputVolumeSlider.Value = ExternalVolume;
            TestAssert.Equal(1, provider.Commands.Count);
        }
        finally { window.AllowClose(); }
    });

    internal static void RemovingAudioEndpointsCancelsAdjustmentsAndDisablesControls() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.BeginOutputVolumeAdjustment();
            window.OutputVolumeSlider.Value = ChangedVolume;
            provider.Snapshot = new(null, null, "Reconnect an audio device and try again.");
            window.UpdateAudioStatus();
            window.CommitOutputVolumeAdjustment();
            window.MicrophoneMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Empty(provider.Commands);
            TestAssert.False(window.OutputDeviceComboBox.IsEnabled);
            TestAssert.False(window.InputDeviceComboBox.IsEnabled);
            TestAssert.False(window.OutputVolumeSlider.IsEnabled);
            TestAssert.False(window.MicrophoneMuteButton.IsEnabled);
            TestAssert.Equal("No active output device", window.OutputDeviceText.Text);
            TestAssert.Equal("No active input device", window.InputDeviceText.Text);
            TestAssert.Equal("Unavailable", window.OutputVolumeValueText.Text);
            TestAssert.Equal(Visibility.Visible, window.AudioStatusText.Visibility);
        }
        finally { window.AllowClose(); }
    });

    internal static void VolumeKeyboardAndMouseEventsCommitOnlyOnCompletion() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.Show();
            UpdateLayout(window);
            var slider = window.OutputVolumeSlider;
            slider.Focus();
            RaiseKey(slider, Key.Down, Keyboard.PreviewKeyDownEvent);
            slider.Value = ChangedVolume;
            TestAssert.Empty(provider.Commands);
            RaiseKey(slider, Key.Down, Keyboard.PreviewKeyUpEvent);
            TestAssert.SequenceEqual(new[] { "volume:30" }, provider.Commands);
            RaiseMouse(slider, UIElement.PreviewMouseLeftButtonDownEvent);
            slider.Value = ExternalVolume;
            TestAssert.Equal(1, provider.Commands.Count);
            RaiseMouse(slider, UIElement.PreviewMouseLeftButtonUpEvent);
            TestAssert.SequenceEqual(new[] { "volume:30", "volume:75" }, provider.Commands);
        }
        finally { window.AllowClose(); }
    });

    internal static void UnknownAndInvalidAudioValuesDisableOnlyTheirControls() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            foreach (var volume in new double?[] { null, double.NaN, double.PositiveInfinity, -1, MaximumVolume + 1 })
            {
                provider.Snapshot = provider.Snapshot with { OutputVolumePercent = volume, IsMicrophoneMuted = null };
                window.UpdateAudioStatus();
                TestAssert.True(window.OutputDeviceComboBox.IsEnabled);
                TestAssert.True(window.InputDeviceComboBox.IsEnabled);
                TestAssert.False(window.OutputVolumeSlider.IsEnabled);
                TestAssert.False(window.MicrophoneMuteButton.IsEnabled);
                window.BeginOutputVolumeAdjustment();
                window.CommitOutputVolumeAdjustment();
            }

            foreach (var volume in new[] { 0, MaximumVolume })
            {
                provider.Snapshot = provider.Snapshot with { OutputVolumePercent = volume };
                window.UpdateAudioStatus();
                TestAssert.True(window.OutputVolumeSlider.IsEnabled);
                TestAssert.Equal(volume, window.OutputVolumeSlider.Value);
            }
            TestAssert.Empty(provider.Commands);
        }
        finally { window.AllowClose(); }
    });

    internal static void VolumeCommitRechecksTheDefaultEndpointBeforeWriting() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        try
        {
            window.BeginOutputVolumeAdjustment();
            window.OutputVolumeSlider.Value = ChangedVolume;
            provider.Snapshot = provider.Snapshot with { DefaultOutputDeviceId = Outputs[1].Id, OutputDeviceName = Outputs[1].Name, OutputVolumePercent = ExternalVolume };
            window.CommitOutputVolumeAdjustment();
            TestAssert.Empty(provider.Commands);
            TestAssert.Equal(Outputs[1], window.OutputDeviceComboBox.SelectedItem);
            TestAssert.Equal(ExternalVolume, window.OutputVolumeSlider.Value);
        }
        finally { window.AllowClose(); }
    });

    internal static void ClosingWidgetCancelsPendingAudioCommandsAndRefreshes() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var provider = new FakeAudioProvider();
        var window = CreateWindow(provider);
        window.BeginOutputVolumeAdjustment();
        window.OutputVolumeSlider.Value = ChangedVolume;
        window.AllowClose();
        var reads = provider.SnapshotReads;
        window.CommitOutputVolumeAdjustment();
        window.UpdateAudioStatus();
        window.OutputDeviceComboBox.SelectedItem = Outputs[1];
        TestAssert.Empty(provider.Commands);
        TestAssert.Equal(reads, provider.SnapshotReads);
    });

    private static WidgetWindow CreateWindow(FakeAudioProvider provider) =>
        new(WidgetSettings.Default with { KeepOnTop = false }, _ => { }, new FakeBatteryProvider(), provider);

    private static void RaiseKey(UIElement element, Key key, RoutedEvent routedEvent) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), Environment.TickCount, key) { RoutedEvent = routedEvent });

    private static void RaiseMouse(UIElement element, RoutedEvent routedEvent) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent });

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

    private sealed class FakeBatteryProvider : IBatteryStatusProvider
    {
        public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new BatterySnapshot([], null));
    }

    private sealed class FakeAudioProvider : IAudioDeviceProvider
    {
        internal AudioDeviceSnapshot Snapshot { get; set; } = new(Outputs[0].Name, Inputs[0].Name, null)
        {
            OutputDevices = Outputs,
            InputDevices = Inputs,
            DefaultOutputDeviceId = Outputs[0].Id,
            DefaultInputDeviceId = Inputs[0].Id,
            OutputVolumePercent = InitialVolume,
            IsMicrophoneMuted = false
        };
        internal List<string> Commands { get; } = [];
        internal string? FailureMessage { get; set; }
        internal int SnapshotReads { get; private set; }
        public AudioDeviceSnapshot GetSnapshot() { SnapshotReads++; return Snapshot; }
        public AudioOperationResult SetDefaultOutput(string deviceId)
        {
            Commands.Add("output:" + deviceId);
            if (FailureMessage is not null) { return new(FailureMessage); }
            var endpoint = Snapshot.OutputDevices.Single(device => device.Id == deviceId);
            Snapshot = Snapshot with { DefaultOutputDeviceId = deviceId, OutputDeviceName = endpoint.Name };
            return new(null);
        }
        public AudioOperationResult SetDefaultInput(string deviceId)
        {
            Commands.Add("input:" + deviceId);
            if (FailureMessage is not null) { return new(FailureMessage); }
            var endpoint = Snapshot.InputDevices.Single(device => device.Id == deviceId);
            Snapshot = Snapshot with { DefaultInputDeviceId = deviceId, InputDeviceName = endpoint.Name };
            return new(null);
        }
        public AudioOperationResult SetOutputVolume(double percentage)
        {
            Commands.Add("volume:" + percentage.ToString(CultureInfo.InvariantCulture));
            if (FailureMessage is not null) { return new(FailureMessage); }
            Snapshot = Snapshot with { OutputVolumePercent = percentage };
            return new(null);
        }
        public AudioOperationResult SetMicrophoneMute(bool muted)
        {
            Commands.Add("mute:" + muted);
            if (FailureMessage is not null) { return new(FailureMessage); }
            Snapshot = Snapshot with { IsMicrophoneMuted = muted };
            return new(null);
        }
    }
}
