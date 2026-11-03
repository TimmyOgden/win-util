using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinUtil.Models;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace WinUtil.Views;

public partial class WidgetWindow
{
    private const double MinimumVolumePercent = 0;
    private const double MaximumVolumePercent = 100;
    private AudioDeviceSnapshot audioSnapshot = new(null, null, null);
    private IReadOnlyList<AudioEndpoint> outputDevices = [];
    private IReadOnlyList<AudioEndpoint> inputDevices = [];
    private bool isRefreshingAudio = true;
    private bool isAdjustingOutputVolume;
    private bool isOutputVolumeChanged;
    private bool audioControlsClosed;
    private string? volumeAdjustmentDeviceId;
    private string? audioOperationError;

    internal void UpdateAudioStatus()
    {
        if (audioControlsClosed)
        {
            return;
        }

        var snapshot = audioDeviceProvider.GetSnapshot();
        isRefreshingAudio = true;
        try
        {
            if (!outputDevices.SequenceEqual(snapshot.OutputDevices))
            {
                outputDevices = snapshot.OutputDevices;
                OutputDeviceComboBox.ItemsSource = outputDevices;
            }

            if (!inputDevices.SequenceEqual(snapshot.InputDevices))
            {
                inputDevices = snapshot.InputDevices;
                InputDeviceComboBox.ItemsSource = inputDevices;
            }

            var output = outputDevices.FirstOrDefault(device => device.Id == snapshot.DefaultOutputDeviceId);
            var input = inputDevices.FirstOrDefault(device => device.Id == snapshot.DefaultInputDeviceId);
            OutputDeviceComboBox.SelectedItem = output;
            InputDeviceComboBox.SelectedItem = input;
            OutputDeviceComboBox.IsEnabled = outputDevices.Count > 0;
            InputDeviceComboBox.IsEnabled = inputDevices.Count > 0;
            OutputDeviceText.Text = snapshot.HasOutputDevice ? snapshot.OutputDeviceName : "No active output device";
            InputDeviceText.Text = snapshot.HasInputDevice ? snapshot.InputDeviceName : "No active input device";
            OutputDeviceText.Visibility = output is null ? Visibility.Visible : Visibility.Collapsed;
            InputDeviceText.Visibility = input is null ? Visibility.Visible : Visibility.Collapsed;

            var canAdjustVolume = output is not null && snapshot.OutputVolumePercent is >= MinimumVolumePercent and <= MaximumVolumePercent;
            if (isAdjustingOutputVolume && (volumeAdjustmentDeviceId != output?.Id || !canAdjustVolume))
            {
                CancelOutputVolumeAdjustment();
            }

            OutputVolumeSlider.IsEnabled = canAdjustVolume;
            if (!isAdjustingOutputVolume)
            {
                OutputVolumeSlider.Value = canAdjustVolume ? snapshot.OutputVolumePercent!.Value : MinimumVolumePercent;
                OutputVolumeValueText.Text = canAdjustVolume ? FormatVolume(OutputVolumeSlider.Value) : "Unavailable";
            }

            MicrophoneMuteButton.IsEnabled = input is not null && snapshot.IsMicrophoneMuted.HasValue;
            var muted = snapshot.IsMicrophoneMuted == true;
            MicrophoneMuteButton.Content = muted ? "Unmute" : "Mute";
            MicrophoneMuteButton.ToolTip = !MicrophoneMuteButton.IsEnabled
                ? "Microphone mute status is unavailable."
                : muted ? "Unmute the default microphone" : "Mute the default microphone";
            AutomationProperties.SetName(MicrophoneMuteButton, muted ? "Unmute microphone" : "Mute microphone");
            audioSnapshot = snapshot;
            var errors = new[] { audioOperationError, snapshot.ErrorMessage }.Where(error => !string.IsNullOrWhiteSpace(error)).Distinct().ToArray();
            var errorMessage = string.Join(Environment.NewLine, errors);
            AudioStatusText.Text = errorMessage.Length == 0 ? "LIVE" : errorMessage;
            AudioStatusText.ToolTip = errorMessage.Length == 0 ? null : errorMessage;
            AudioStatusText.Visibility = errorMessage.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            isRefreshingAudio = false;
        }
    }

    private void HandleOutputDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isRefreshingAudio && !audioControlsClosed && OutputDeviceComboBox.SelectedItem is AudioEndpoint endpoint
            && endpoint.Id != audioSnapshot.DefaultOutputDeviceId)
        {
            CancelOutputVolumeAdjustment();
            ApplyAudioOperation(audioDeviceProvider.SetDefaultOutput(endpoint.Id));
        }
    }

    private void HandleInputDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isRefreshingAudio && !audioControlsClosed && InputDeviceComboBox.SelectedItem is AudioEndpoint endpoint
            && endpoint.Id != audioSnapshot.DefaultInputDeviceId)
        {
            ApplyAudioOperation(audioDeviceProvider.SetDefaultInput(endpoint.Id));
        }
    }

    private void HandleMicrophoneMute(object sender, RoutedEventArgs e)
    {
        if (!isRefreshingAudio && !audioControlsClosed && MicrophoneMuteButton.IsEnabled && audioSnapshot.IsMicrophoneMuted is bool muted)
        {
            ApplyAudioOperation(audioDeviceProvider.SetMicrophoneMute(!muted));
        }
    }

    internal void BeginOutputVolumeAdjustment()
    {
        if (isRefreshingAudio || audioControlsClosed || !OutputVolumeSlider.IsEnabled || isAdjustingOutputVolume)
        {
            return;
        }

        isAdjustingOutputVolume = true;
        isOutputVolumeChanged = false;
        volumeAdjustmentDeviceId = audioSnapshot.DefaultOutputDeviceId;
    }

    internal void CommitOutputVolumeAdjustment()
    {
        if (!isAdjustingOutputVolume)
        {
            return;
        }

        UpdateAudioStatus();
        if (!isAdjustingOutputVolume)
        {
            return;
        }

        var shouldCommit = !audioControlsClosed && isOutputVolumeChanged && OutputVolumeSlider.IsEnabled
            && volumeAdjustmentDeviceId == audioSnapshot.DefaultOutputDeviceId;
        var percentage = OutputVolumeSlider.Value;
        CancelOutputVolumeAdjustment();
        if (shouldCommit)
        {
            ApplyAudioOperation(audioDeviceProvider.SetOutputVolume(percentage));
        }
    }

    private void HandleOutputVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isRefreshingAudio || !isAdjustingOutputVolume)
        {
            return;
        }

        isOutputVolumeChanged = true;
        OutputVolumeValueText.Text = FormatVolume(e.NewValue);
    }

    private void HandleOutputVolumeMouseDown(object sender, MouseButtonEventArgs e) => BeginOutputVolumeAdjustment();

    private void HandleOutputVolumeMouseUp(object sender, MouseButtonEventArgs e) => CommitOutputVolumeAdjustment();

    private void HandleOutputVolumeLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => CommitOutputVolumeAdjustment();

    private void HandleOutputVolumeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsVolumeKey(e.Key))
        {
            BeginOutputVolumeAdjustment();
        }
    }

    private void HandleOutputVolumeKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsVolumeKey(e.Key))
        {
            CommitOutputVolumeAdjustment();
        }
    }

    private void HandleOutputVolumeLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitOutputVolumeAdjustment();

    private static bool IsVolumeKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private static string FormatVolume(double value) => Math.Round(value).ToString(CultureInfo.CurrentCulture) + "%";

    private void ApplyAudioOperation(AudioOperationResult result)
    {
        audioOperationError = result.ErrorMessage;
        UpdateAudioStatus();
    }

    private void CancelOutputVolumeAdjustment()
    {
        isAdjustingOutputVolume = false;
        isOutputVolumeChanged = false;
        volumeAdjustmentDeviceId = null;
    }

    private void CloseAudioControls()
    {
        audioControlsClosed = true;
        CancelOutputVolumeAdjustment();
    }
}
