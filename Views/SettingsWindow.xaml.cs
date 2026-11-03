using System.IO;
using System.Windows;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace WinUtil.Views;

public partial class SettingsWindow : Window
{
    private const double PercentageMultiplier = 100;
    private const int JwtSecret256BitOptionIndex = 0;
    private const int JwtSecret384BitOptionIndex = 1;
    private const int JwtSecret512BitOptionIndex = 2;
    private const int SpotlightGeneralSettingsPageIndex = 0;
    private const int UuidSettingsPageIndex = 1;
    private const int PasswordSettingsPageIndex = 2;
    private const int JwtSecretSettingsPageIndex = 3;
    private const int TextToolsSettingsPageIndex = 4;
    private const int WidgetSettingsPageIndex = 0;
    private const int ScreenshotSettingsPageIndex = 1;
    private const int BatterySettingsPageIndex = 2;

    private readonly WidgetSettings initialSettings;
    private readonly SettingsStore settingsStore;
    private readonly ShortcutSettingsViewModel shortcutSettings;
    private readonly Action<Action<ShortcutCaptureResult>> beginShortcutCapture;
    private readonly Action endShortcutCapture;
    private readonly Func<SpotlightShortcuts, string?> tryApplySpotlightShortcuts;
    private readonly Func<bool, string?> tryApplyStartup;
    private readonly Func<ScreenshotEditorOptions> getScreenshotEditorOptions;
    private readonly Func<SpotlightPreferences> getSpotlightPreferences;
    private bool isChangingSettingsPage;
    private bool clearSpotlightHistory;

    internal SettingsWindow(
        SettingsStore settingsStore,
        WidgetSettings settings,
        Func<SpotlightShortcuts, string?> tryApplySpotlightShortcuts,
        Action<Action<ShortcutCaptureResult>> beginShortcutCapture,
        Action endShortcutCapture,
        Func<bool, string?> tryApplyStartup,
        Func<ScreenshotEditorOptions>? getScreenshotEditorOptions = null,
        Func<SpotlightPreferences>? getSpotlightPreferences = null)
    {
        initialSettings = settings;
        this.settingsStore = settingsStore;
        this.tryApplySpotlightShortcuts = tryApplySpotlightShortcuts;
        this.beginShortcutCapture = beginShortcutCapture;
        this.endShortcutCapture = endShortcutCapture;
        this.tryApplyStartup = tryApplyStartup;
        this.getScreenshotEditorOptions = getScreenshotEditorOptions ?? (() => settings.ScreenshotEditor);
        this.getSpotlightPreferences = getSpotlightPreferences ?? (() => settings.SpotlightPreferences);
        shortcutSettings = new ShortcutSettingsViewModel(SpotlightActionCatalog.All, settings.SpotlightShortcuts);
        DataContext = shortcutSettings;
        InitializeComponent();

        KeepOnTopCheckBox.IsChecked = settings.KeepOnTop;
        RunAtStartupCheckBox.IsChecked = settings.RunAtStartup;
        OpenScreenshotEditorCheckBox.IsChecked = settings.OpenScreenshotEditorAfterCapture;
        KeepScreenshotHistoryCheckBox.IsChecked = settings.KeepScreenshotHistory;
        BatteryAlertsEnabledCheckBox.IsChecked = settings.BatteryAlerts.Enabled;
        BatteryAlertThresholdSlider.Minimum = BatteryAlertSettings.MinimumThresholdPercent;
        BatteryAlertThresholdSlider.Maximum = BatteryAlertSettings.MaximumThresholdPercent;
        BatteryAlertThresholdSlider.Value = settings.BatteryAlerts.ThresholdPercent;
        RememberRecentCommandsCheckBox.IsChecked = settings.SpotlightPreferences.RememberRecentCommands;
        ShowSecondsCheckBox.IsChecked = settings.ShowSeconds;
        AutoHideDelayComboBox.SelectedIndex = settings.AutoHideDelaySeconds == WidgetSettings.ShortAutoHideDelaySeconds
            ? 0
            : 1;
        BackgroundOpacitySlider.Value = settings.BackgroundOpacity;
        FadedOpacitySlider.Value = settings.FadedOpacity;
        OpacitySlider.Value = settings.Opacity;
        PasswordIncludesDigitsCheckBox.IsChecked = settings.PasswordIncludesDigits;
        PasswordIncludesLowercaseCheckBox.IsChecked = settings.PasswordIncludesLowercase;
        PasswordIncludesSymbolsCheckBox.IsChecked = settings.PasswordIncludesSymbols;
        PasswordIncludesUppercaseCheckBox.IsChecked = settings.PasswordIncludesUppercase;
        PasswordLengthSlider.Value = settings.PasswordLength;
        UuidVersionComboBox.SelectedIndex = settings.UuidVersion == UuidVersion.Version7 ? 1 : 0;
        JwtSecretStrengthComboBox.SelectedIndex = settings.JwtSecretLengthBytes switch
        {
            WidgetSettings.JwtSecretBytesFor384Bits => JwtSecret384BitOptionIndex,
            WidgetSettings.JwtSecretBytesFor512Bits => JwtSecret512BitOptionIndex,
            _ => JwtSecret256BitOptionIndex
        };
        SettingsNavigationListBox.SelectedIndex = 0;
        Closed += (_, _) => endShortcutCapture();
    }

    internal event Action<WidgetSettings>? SettingsSaved;

    private void HandleCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HandleOpacityValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = $"{OpacitySlider.Value * PercentageMultiplier:0}%";
        }
    }

    private void HandleBackgroundOpacityValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundOpacityValueText is not null)
        {
            BackgroundOpacityValueText.Text = $"{BackgroundOpacitySlider.Value * PercentageMultiplier:0}%";
        }
    }

    private void HandleFadedOpacityValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadedOpacityValueText is not null)
        {
            FadedOpacityValueText.Text = $"{FadedOpacitySlider.Value * PercentageMultiplier:0}%";
        }
    }

    private void HandlePasswordLengthValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PasswordLengthValueText is not null)
        {
            PasswordLengthValueText.Text = $"{PasswordLengthSlider.Value:0} characters";
        }
    }

    private void HandlePasswordCharacterSetChanged(object sender, RoutedEventArgs e) =>
        PasswordCharacterSetErrorText.Visibility = Visibility.Collapsed;

    private void HandleSpotlightNavigationSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (isChangingSettingsPage || SpotlightNavigationListBox.SelectedIndex < 0)
        {
            return;
        }

        ShowSettingsPage(SpotlightNavigationListBox.SelectedIndex switch
        {
            SpotlightGeneralSettingsPageIndex => SettingsPage.SpotlightGeneral,
            UuidSettingsPageIndex => SettingsPage.Uuid,
            PasswordSettingsPageIndex => SettingsPage.Password,
            JwtSecretSettingsPageIndex => SettingsPage.JwtSecret,
            TextToolsSettingsPageIndex => SettingsPage.TextTools,
            _ => throw new ArgumentOutOfRangeException(nameof(SpotlightNavigationListBox.SelectedIndex))
        });
    }

    private void HandleWidgetNavigationSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (isChangingSettingsPage || SettingsNavigationListBox.SelectedIndex < 0)
        {
            return;
        }

        ShowSettingsPage(SettingsNavigationListBox.SelectedIndex switch
        {
            WidgetSettingsPageIndex => SettingsPage.Widget,
            ScreenshotSettingsPageIndex => SettingsPage.Screenshots,
            BatterySettingsPageIndex => SettingsPage.Battery,
            _ => throw new ArgumentOutOfRangeException(nameof(SettingsNavigationListBox.SelectedIndex))
        });
    }

    private void HandleSave(object sender, RoutedEventArgs e)
    {
        if (!HasPasswordCharacterSet())
        {
            PasswordCharacterSetErrorText.Visibility = Visibility.Visible;
            return;
        }

        var spotlightPreferences = getSpotlightPreferences() with
        {
            RememberRecentCommands = RememberRecentCommandsCheckBox.IsChecked == true
        };

        if (clearSpotlightHistory)
        {
            spotlightPreferences = spotlightPreferences.ClearHistory();
        }

        var settings = new WidgetSettings
        {
            KeepOnTop = KeepOnTopCheckBox.IsChecked ?? WidgetSettings.Default.KeepOnTop,
            AutoHideDelaySeconds = AutoHideDelayComboBox.SelectedIndex == 0
                ? WidgetSettings.ShortAutoHideDelaySeconds
                : WidgetSettings.DefaultAutoHideDelaySeconds,
            BackgroundOpacity = BackgroundOpacitySlider.Value,
            FadedOpacity = FadedOpacitySlider.Value,
            Left = initialSettings.Left,
            Opacity = OpacitySlider.Value,
            JwtSecretLengthBytes = JwtSecretStrengthComboBox.SelectedIndex switch
            {
                JwtSecret384BitOptionIndex => WidgetSettings.JwtSecretBytesFor384Bits,
                JwtSecret512BitOptionIndex => WidgetSettings.JwtSecretBytesFor512Bits,
                _ => WidgetSettings.JwtSecretBytesFor256Bits
            },
            PasswordIncludesDigits = PasswordIncludesDigitsCheckBox.IsChecked ?? WidgetSettings.Default.PasswordIncludesDigits,
            PasswordIncludesLowercase = PasswordIncludesLowercaseCheckBox.IsChecked ?? WidgetSettings.Default.PasswordIncludesLowercase,
            PasswordIncludesSymbols = PasswordIncludesSymbolsCheckBox.IsChecked ?? WidgetSettings.Default.PasswordIncludesSymbols,
            PasswordIncludesUppercase = PasswordIncludesUppercaseCheckBox.IsChecked ?? WidgetSettings.Default.PasswordIncludesUppercase,
            PasswordLength = (int)PasswordLengthSlider.Value,
            RunAtStartup = RunAtStartupCheckBox.IsChecked ?? WidgetSettings.Default.RunAtStartup,
            OpenScreenshotEditorAfterCapture = OpenScreenshotEditorCheckBox.IsChecked ?? WidgetSettings.Default.OpenScreenshotEditorAfterCapture,
            KeepScreenshotHistory = KeepScreenshotHistoryCheckBox.IsChecked ?? WidgetSettings.Default.KeepScreenshotHistory,
            ScreenshotEditor = getScreenshotEditorOptions(),
            BatteryAlerts = new BatteryAlertSettings
            {
                Enabled = BatteryAlertsEnabledCheckBox.IsChecked == true,
                ThresholdPercent = (int)BatteryAlertThresholdSlider.Value
            }.Normalize(),
            ShowSeconds = ShowSecondsCheckBox.IsChecked ?? WidgetSettings.Default.ShowSeconds,
            SpotlightShortcuts = shortcutSettings.ToSpotlightShortcuts(),
            SpotlightPreferences = spotlightPreferences.Normalize(),
            Top = initialSettings.Top,
            UuidVersion = UuidVersionComboBox.SelectedIndex == 1 ? UuidVersion.Version7 : UuidVersion.Version4
        };

        var shortcutError = settings.SpotlightShortcuts.Validate()
            ?? tryApplySpotlightShortcuts(settings.SpotlightShortcuts);

        if (shortcutError is not null)
        {
            ShowShortcutError(shortcutError);
            return;
        }

        var startupError = tryApplyStartup(settings.RunAtStartup);

        if (startupError is not null)
        {
            StartupErrorText.Text = RestoreSpotlightShortcuts(startupError);
            StartupErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var rollbackError = tryApplyStartup(initialSettings.RunAtStartup);
            StartupErrorText.Text = RestoreSpotlightShortcuts($"Settings could not be saved. {exception.Message} {rollbackError}".TrimEnd());
            StartupErrorText.Visibility = Visibility.Visible;
            return;
        }

        SettingsSaved?.Invoke(settings);
        Close();
    }

    private string RestoreSpotlightShortcuts(string errorMessage)
    {
        var rollbackError = tryApplySpotlightShortcuts(initialSettings.SpotlightShortcuts);

        return rollbackError is null
            ? errorMessage
            : $"{errorMessage} Spotlight shortcuts could not be restored. {rollbackError}";
    }

    private void HandleClearSpotlightHistory(object sender, RoutedEventArgs e)
    {
        clearSpotlightHistory = true;
        ClearSpotlightHistoryButton.Content = "Clears when saved";
        ClearSpotlightHistoryButton.IsEnabled = false;
    }

    private bool HasPasswordCharacterSet() => PasswordIncludesDigitsCheckBox.IsChecked == true
        || PasswordIncludesLowercaseCheckBox.IsChecked == true
        || PasswordIncludesSymbolsCheckBox.IsChecked == true
        || PasswordIncludesUppercaseCheckBox.IsChecked == true;

    private void HandleClearShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId })
        {
            throw new InvalidOperationException("The shortcut clear button must identify a Spotlight action.");
        }

        shortcutSettings[actionId].Shortcut = actionId == ShortcutSettingsViewModel.PaletteSettingId
            ? SpotlightShortcuts.DefaultPalette
            : null;
        ShortcutErrorText.Visibility = Visibility.Collapsed;
    }

    private void HandleShortcutCaptureGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string actionId })
        {
            throw new InvalidOperationException("The shortcut input must identify a Spotlight action.");
        }

        beginShortcutCapture(result => HandleShortcutCaptureResult(actionId, result));
    }

    private void HandleShortcutCaptureLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        endShortcutCapture();

    private void HandleShortcutCaptureResult(string actionId, ShortcutCaptureResult result)
    {
        if (result.ErrorMessage is { } errorMessage)
        {
            ShowShortcutError(errorMessage);
            return;
        }

        shortcutSettings[actionId].Shortcut = result.Shortcut
            ?? throw new InvalidOperationException("A shortcut capture result must include a shortcut or an error.");
        ShortcutErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowShortcutError(string message)
    {
        ShortcutErrorText.Text = message;
        ShortcutErrorText.Visibility = Visibility.Visible;
    }

    private void ShowSettingsPage(SettingsPage page)
    {
        isChangingSettingsPage = true;

        try
        {
            WidgetSettingsPanel.Visibility = page == SettingsPage.Widget ? Visibility.Visible : Visibility.Collapsed;
            ScreenshotSettingsPanel.Visibility = page == SettingsPage.Screenshots ? Visibility.Visible : Visibility.Collapsed;
            BatterySettingsPanel.Visibility = page == SettingsPage.Battery ? Visibility.Visible : Visibility.Collapsed;
            SpotlightGeneralSettingsPanel.Visibility = page == SettingsPage.SpotlightGeneral ? Visibility.Visible : Visibility.Collapsed;
            UuidSettingsPanel.Visibility = page == SettingsPage.Uuid ? Visibility.Visible : Visibility.Collapsed;
            PasswordSettingsPanel.Visibility = page == SettingsPage.Password ? Visibility.Visible : Visibility.Collapsed;
            JwtSecretSettingsPanel.Visibility = page == SettingsPage.JwtSecret ? Visibility.Visible : Visibility.Collapsed;
            TextToolsSettingsPanel.Visibility = page == SettingsPage.TextTools ? Visibility.Visible : Visibility.Collapsed;
            SettingsNavigationListBox.SelectedIndex = page switch
            {
                SettingsPage.Widget => WidgetSettingsPageIndex,
                SettingsPage.Screenshots => ScreenshotSettingsPageIndex,
                SettingsPage.Battery => BatterySettingsPageIndex,
                _ => -1
            };
            SpotlightNavigationListBox.SelectedIndex = page switch
            {
                SettingsPage.SpotlightGeneral => SpotlightGeneralSettingsPageIndex,
                SettingsPage.Uuid => UuidSettingsPageIndex,
                SettingsPage.Password => PasswordSettingsPageIndex,
                SettingsPage.JwtSecret => JwtSecretSettingsPageIndex,
                SettingsPage.TextTools => TextToolsSettingsPageIndex,
                _ => -1
            };
            SettingsContentScrollViewer.ScrollToTop();
        }
        finally
        {
            isChangingSettingsPage = false;
        }
    }

    private enum SettingsPage
    {
        Widget,
        Screenshots,
        Battery,
        SpotlightGeneral,
        Uuid,
        Password,
        JwtSecret,
        TextTools
    }
}
