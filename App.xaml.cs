using System.IO;
using System.ComponentModel;
using System.Security;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil;

public partial class App : System.Windows.Application
{
    internal const string SpotlightShortcutsUnavailableMessage = "Spotlight shortcuts are unavailable until the desktop widget has started.";
    internal const string StartupConfigurationArgument = "--configure-startup";

    private const int StartupConfigurationSuccessExitCode = 0;
    private const int StartupConfigurationFailureExitCode = 1;

    private readonly CrashLog crashLog = new();
    private readonly StartupRegistration startupRegistration = new();
    private readonly HashSet<ScreenshotEditorWindow> screenshotEditors = [];
    private bool isExiting;
    private ScreenshotCaptureListener? screenshotCaptureListener;
    private SettingsStore? settingsStore;
    private SettingsWindow? settingsWindow;
    private ScreenshotHistoryStore? screenshotHistoryStore;
    private ScreenshotHistoryWindow? screenshotHistoryWindow;
    private SingleInstanceGate? singleInstanceGate;
    private ISpotlightShortcutManager? spotlightShortcutManager;
    private SpotlightController? spotlightController;
    private TrayIconController? trayIconController;
    private WidgetWindow? widgetWindow;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains(StartupConfigurationArgument, StringComparer.Ordinal))
        {
            var startupSettings = new SettingsStore().Load();
            var startupError = TryApplyStartup(startupSettings.Settings.RunAtStartup);

            if (startupError is not null)
            {
                Console.Error.WriteLine(startupError);
            }

            Shutdown(startupError is null ? StartupConfigurationSuccessExitCode : StartupConfigurationFailureExitCode);
            return;
        }

        singleInstanceGate = new SingleInstanceGate("Local\\WinUtil");
        if (!singleInstanceGate.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        RegisterUnhandledExceptionHandlers();

        settingsStore = new SettingsStore();
        screenshotHistoryStore = new ScreenshotHistoryStore();
        var loadResult = settingsStore.Load();
        trayIconController = new TrayIconController(ShowWidget, ShowSettings, ExitApplication, OpenImageFile, PasteClipboardImage, ShowScreenshotHistory);
        var widget = new WidgetWindow(
            loadResult.Settings,
            settingsStore.Save,
            new BatteryStatusProvider(),
            new AudioDeviceProvider(),
            ShowLowBatteryAlerts);
        widgetWindow = widget;
        var spotlightWindow = new SpotlightWindow(
            () => widget.CurrentSettings,
            getPreferences: () => widget.CurrentSettings.SpotlightPreferences,
            savePreferences: SaveSpotlightPreferences);
        spotlightController = new SpotlightController(spotlightWindow, new ForegroundTextInjector(), () => widget.CurrentSettings);
        widgetWindow.ShowWidget();
        RegisterSpotlightShortcuts();
        RegisterScreenshotCapture();

        var registrationError = TryApplyStartup(loadResult.Settings.RunAtStartup);

        if (registrationError is not null)
        {
            trayIconController.ShowWarning("Windows startup could not be updated", registrationError);
        }

        if (loadResult.Warning is not null)
        {
            trayIconController.ShowWarning("Settings reset to defaults", loadResult.Warning);
        }
    }

    private void ShowLowBatteryAlerts(IReadOnlyList<BatteryDevice> devices)
    {
        trayIconController?.ShowWarning("Low battery", string.Join(Environment.NewLine,
            devices.Select(device => $"{device.Name}: {device.Percentage}%")));
    }

    private void RegisterUnhandledExceptionHandlers()
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += HandleDomainUnhandledException;
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        crashLog.Write("Dispatcher", e.Exception);
        trayIconController?.ShowWarning(
            "WinUtil hit an unexpected error",
            $"{e.Exception.Message} Details were written to {crashLog.LogFilePath}.");
        e.Handled = true;
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        crashLog.Write("Task", e.Exception);
        e.SetObserved();
    }

    private void HandleDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            crashLog.Write("AppDomain", exception);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        screenshotCaptureListener?.Dispose();
        spotlightShortcutManager?.Dispose();
        spotlightController?.Dispose();
        trayIconController?.Dispose();
        singleInstanceGate?.Dispose();
        base.OnExit(e);
    }

    private void ExitApplication()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;

        try
        {
            if (!TryCloseScreenshotEditors(screenshotEditors))
            {
                return;
            }

            screenshotCaptureListener?.Dispose();
            screenshotCaptureListener = null;
            spotlightShortcutManager?.Dispose();
            spotlightShortcutManager = null;
            spotlightController?.Dispose();
            spotlightController = null;
            widgetWindow?.AllowClose();
            Shutdown();
        }
        finally
        {
            isExiting = false;
        }
    }

    internal static bool TryCloseScreenshotEditors(IReadOnlyCollection<ScreenshotEditorWindow> editors)
    {
        ArgumentNullException.ThrowIfNull(editors);
        var openEditors = editors.ToArray();

        if (openEditors.Any(editor => !editor.ConfirmClose()))
        {
            return false;
        }

        foreach (var editor in openEditors)
        {
            editor.CloseAfterConfirmation();
        }

        return true;
    }

    private void ShowSettings()
    {
        if (settingsStore is null || widgetWindow is null)
        {
            return;
        }

        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(
                settingsStore,
                widgetWindow.CurrentSettings,
                TryApplySpotlightShortcuts,
                BeginShortcutCapture,
                EndShortcutCapture,
                TryApplyStartup,
                () => widgetWindow.CurrentSettings.ScreenshotEditor,
                () => widgetWindow.CurrentSettings.SpotlightPreferences);
            settingsWindow.SettingsSaved += ApplyWidgetSettings;
            settingsWindow.Closed += HandleSettingsWindowClosed;
        }

        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void ShowWidget()
    {
        if (widgetWindow is null)
        {
            return;
        }

        widgetWindow.ShowWidget();
    }

    private void HandleSettingsWindowClosed(object? sender, EventArgs e)
    {
        settingsWindow = null;
    }

    private void ApplyWidgetSettings(WidgetSettings settings)
    {
        widgetWindow?.ApplySettings(settings);
        screenshotCaptureListener?.CancelPendingCapture();
    }

    private void SaveSpotlightPreferences(SpotlightPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (settingsStore is null || widgetWindow is null)
        {
            throw new InvalidOperationException("Spotlight preferences are unavailable until WinUtil has started.");
        }

        var updatedSettings = widgetWindow.CurrentSettings with { SpotlightPreferences = preferences.Normalize() };
        settingsStore.Save(updatedSettings);
        widgetWindow.ApplySettings(updatedSettings);
    }

    private void RegisterScreenshotCapture()
    {
        if (widgetWindow is null)
        {
            return;
        }

        try
        {
            screenshotCaptureListener = new ScreenshotCaptureListener(
                new WindowInteropHelper(widgetWindow).Handle,
                () => widgetWindow.CurrentSettings.OpenScreenshotEditorAfterCapture || widgetWindow.CurrentSettings.KeepScreenshotHistory,
                ShowScreenshotEditor,
                ReportScreenshotError);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException or AggregateException)
        {
            ReportScreenshotError(exception);
        }
    }

    private void ShowScreenshotEditor(BitmapSource screenshot)
    {
        if (isExiting || widgetWindow is null)
        {
            return;
        }

        RememberScreenshot(screenshot, ScreenshotHistorySource.Capture);
        if (widgetWindow.CurrentSettings.OpenScreenshotEditorAfterCapture) { OpenEditor(screenshot); }
    }

    private void OpenImageEditor(BitmapSource image)
    {
        if (isExiting || widgetWindow is null) { return; }
        RememberScreenshot(image, ScreenshotHistorySource.Import);
        OpenEditor(image);
    }

    private void OpenEditor(BitmapSource image)
    {
        if (isExiting || widgetWindow is null)
        {
            return;
        }

        var editor = new ScreenshotEditorWindow(image, widgetWindow.CurrentSettings.ScreenshotEditor,
            openImageEditor: OpenImageEditor, showHistory: ShowScreenshotHistory);
        editor.ImageExported += RememberExportedScreenshot;
        screenshotEditors.Add(editor);
        editor.Closed += HandleScreenshotEditorClosed;
        editor.Show();
        editor.Activate();
    }

    private void ShowScreenshotHistory()
    {
        if (isExiting || screenshotHistoryStore is null) { return; }
        if (screenshotHistoryWindow is null)
        {
            screenshotHistoryWindow = new ScreenshotHistoryWindow(screenshotHistoryStore, OpenEditor);
            screenshotHistoryWindow.Closed += (_, _) => screenshotHistoryWindow = null;
        }
        else { screenshotHistoryWindow.RefreshHistory(); }
        screenshotHistoryWindow.Show();
        if (screenshotHistoryWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            screenshotHistoryWindow.WindowState = System.Windows.WindowState.Normal;
        }
        screenshotHistoryWindow.Activate();
    }

    private void RememberExportedScreenshot(BitmapSource image) => RememberScreenshot(image, ScreenshotHistorySource.Export);

    private void RememberScreenshot(BitmapSource image, ScreenshotHistorySource source)
    {
        if (widgetWindow?.CurrentSettings.KeepScreenshotHistory != true || screenshotHistoryStore is null) { return; }
        try
        {
            screenshotHistoryStore.Add(image, source);
            screenshotHistoryWindow?.RefreshHistory();
        }
        catch (Exception exception) when (IsImageImportFailure(exception))
        {
            crashLog.Write("Screenshot history", exception);
            trayIconController?.ShowWarning("Screenshot history could not be saved", exception.Message);
        }
    }

    private void OpenImageFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() == true) { OpenImageEditor(ScreenshotImageLoader.LoadFile(dialog.FileName)); }
        }
        catch (Exception exception) when (IsImageImportFailure(exception)) { ReportScreenshotError(exception); }
    }

    private void PasteClipboardImage()
    {
        try { OpenImageEditor(ScreenshotImageLoader.PrepareClipboardImage(System.Windows.Clipboard.GetImage())); }
        catch (Exception exception) when (IsImageImportFailure(exception)) { ReportScreenshotError(exception); }
    }

    private static bool IsImageImportFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or System.Runtime.InteropServices.ExternalException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException;

    private void HandleScreenshotEditorClosed(object? sender, EventArgs e)
    {
        if (sender is not ScreenshotEditorWindow editor)
        {
            return;
        }

        editor.Closed -= HandleScreenshotEditorClosed;
        editor.ImageExported -= RememberExportedScreenshot;
        screenshotEditors.Remove(editor);

        if (widgetWindow is null || settingsStore is null)
        {
            return;
        }

        var currentOptions = widgetWindow.CurrentSettings.ScreenshotEditor;
        var options = editor.Options.MergeChanges(editor.InitialOptions, currentOptions);

        if (options == currentOptions)
        {
            return;
        }

        var settings = widgetWindow.CurrentSettings with { ScreenshotEditor = options };

        try
        {
            settingsStore.Save(settings);
            widgetWindow.ApplySettings(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportScreenshotError(exception);
        }
    }

    private void ReportScreenshotError(Exception exception)
    {
        crashLog.Write("Screenshot editor", exception);
        trayIconController?.ShowWarning("Screenshot editor", exception.Message);
    }

    private void RegisterSpotlightShortcuts()
    {
        if (widgetWindow is null || spotlightController is null)
        {
            return;
        }

        spotlightShortcutManager = new SpotlightShortcutManager(
            new WindowInteropHelper(widgetWindow).Handle,
            spotlightController.Toggle,
            spotlightController.ExecuteAction);
        var error = spotlightShortcutManager.TryApply(widgetWindow.CurrentSettings.SpotlightShortcuts);

        if (error is null)
        {
            return;
        }

        trayIconController?.ShowWarning(
            "Command palette shortcut unavailable",
            error);
    }

    private string? TryApplySpotlightShortcuts(SpotlightShortcuts shortcuts) => ApplySpotlightShortcuts(spotlightShortcutManager, shortcuts);

    private string? TryApplyStartup(bool enabled)
    {
        try
        {
            startupRegistration.Apply(enabled);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
        {
            crashLog.Write("Windows startup", exception);
            return $"Windows startup could not be updated. {exception.Message}";
        }
    }

    private void BeginShortcutCapture(Action<ShortcutCaptureResult> captureCallback)
    {
        if (spotlightShortcutManager is null)
        {
            captureCallback(ShortcutCaptureResult.Error(SpotlightShortcutsUnavailableMessage));
            return;
        }

        spotlightShortcutManager.BeginShortcutCapture(captureCallback);
    }

    private void EndShortcutCapture() => spotlightShortcutManager?.EndShortcutCapture();

    internal static string? ApplySpotlightShortcuts(ISpotlightShortcutManager? shortcutManager, SpotlightShortcuts shortcuts) => shortcutManager is null
        ? SpotlightShortcutsUnavailableMessage
        : shortcutManager.TryApply(shortcuts);
}
