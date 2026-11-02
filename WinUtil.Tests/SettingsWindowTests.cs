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

internal static partial class SettingsWindowTests
{
    private const int ExpectedTextToolGroupCount = 3;
    private const double StandardDpi = 96;
    private const string ClockWithSeconds = "10:24:36";
    private const string ClockWithoutSeconds = "10:24";
    private const int SpotlightGeneralPageIndex = 0;
    private const uint PaletteTestVirtualKey = 0x4B;
    private static readonly GlobalShortcut TestPalette = new()
    {
        Modifiers = GlobalShortcut.ControlModifier | GlobalShortcut.ShiftModifier,
        VirtualKey = PaletteTestVirtualKey
    };

    internal static void WindowRendersAShortcutEditorForEveryCatalogAction() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new SettingsWindow(
            new SettingsStore(directory.PathFor("settings.json")),
            WidgetSettings.Default,
            _ => null,
            _ => { },
            () => { },
            _ => null);

        try
        {
            window.Show();
            foreach (var panel in new[] { window.UuidSettingsPanel, window.PasswordSettingsPanel, window.JwtSecretSettingsPanel, window.TextToolsSettingsPanel })
            {
                panel.Visibility = Visibility.Visible;
            }

            window.UpdateLayout();

            var shortcutTextBoxes = FindDescendants<TextBox>(window)
                .Where(textBox => textBox.Tag is string actionId && actionId != ShortcutSettingsViewModel.PaletteSettingId)
                .ToArray();
            var actionIds = shortcutTextBoxes.Select(textBox => (string)textBox.Tag).Order().ToArray();
            var expectedIds = SpotlightActionCatalog.All.Select(action => action.Id).Order().ToArray();

            TestAssert.SequenceEqual(expectedIds, actionIds);
            TestAssert.True(shortcutTextBoxes.All(textBox => textBox.Text == ShortcutFormatter.UnassignedText));
            TestAssert.Equal(ExpectedTextToolGroupCount, FindDescendants<ItemsControl>(window).Count(items => items.ItemsSource is IReadOnlyList<WinUtil.ViewModels.ShortcutSettingViewModel>));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void StartupSettingAppliesAndPersistsWhenSaved() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var registrations = new List<bool>();
        var window = CreateStartupSettingsWindow(store, enabled =>
        {
            registrations.Add(enabled);
            return null;
        });

        try
        {
            window.Show();
            TestAssert.True(window.RunAtStartupCheckBox.IsChecked == true);
            window.RunAtStartupCheckBox.IsChecked = false;
            SaveSettings(window);

            TestAssert.SequenceEqual(new[] { false }, registrations);
            TestAssert.False(store.Load().Settings.RunAtStartup);
            TestAssert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void StartupSettingShowsAnErrorWhenRegistrationFails() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        var shortcutRegistrations = new List<SpotlightShortcuts>();
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), _ => "Test startup access denied.", shortcuts =>
        {
            shortcutRegistrations.Add(shortcuts);
            return null;
        });

        try
        {
            window.Show();
            window.RunAtStartupCheckBox.IsChecked = false;
            var editedShortcuts = EditUuidShortcut(window);
            SaveSettings(window);

            TestAssert.SequenceEqual(new[] { editedShortcuts, WidgetSettings.Default.SpotlightShortcuts }, shortcutRegistrations);
            TestAssert.Equal(Visibility.Visible, window.StartupErrorText.Visibility);
            TestAssert.Equal("Test startup access denied.", window.StartupErrorText.Text);
            TestAssert.True(window.IsVisible);
            TestAssert.False(File.Exists(settingsPath));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void StartupSettingRollsBackWhenSettingsCannotBeSaved() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        Directory.CreateDirectory(settingsPath);
        var registrations = new List<bool>();
        var shortcutRegistrations = new List<SpotlightShortcuts>();
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), enabled =>
        {
            registrations.Add(enabled);
            return null;
        }, shortcuts =>
        {
            shortcutRegistrations.Add(shortcuts);
            return null;
        });

        try
        {
            window.Show();
            window.RunAtStartupCheckBox.IsChecked = false;
            var editedShortcuts = EditUuidShortcut(window);
            SaveSettings(window);

            TestAssert.SequenceEqual(new[] { false, true }, registrations);
            TestAssert.SequenceEqual(new[] { editedShortcuts, WidgetSettings.Default.SpotlightShortcuts }, shortcutRegistrations);
            TestAssert.Equal(Visibility.Visible, window.StartupErrorText.Visibility);
            TestAssert.True(window.StartupErrorText.Text.StartsWith("Settings could not be saved.", StringComparison.Ordinal));
            TestAssert.True(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void FailedSaveReportsShortcutAndStartupRollbackErrors() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();

        foreach (var settingsSaveFails in new[] { false, true })
        {
            using var directory = new TemporaryDirectory();
            var settingsPath = directory.PathFor("settings.json");

            if (settingsSaveFails)
            {
                Directory.CreateDirectory(settingsPath);
            }

            var shortcutRegistrations = new List<SpotlightShortcuts>();
            var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), enabled => settingsSaveFails
                ? enabled ? "Test startup rollback failed." : null
                : "Test startup access denied.", shortcuts =>
            {
                shortcutRegistrations.Add(shortcuts);
                return shortcutRegistrations.Count > 1 ? "Test shortcut rollback failed." : null;
            });

            try
            {
                window.Show();
                window.RunAtStartupCheckBox.IsChecked = false;
                var editedShortcuts = EditUuidShortcut(window);
                SaveSettings(window);

                TestAssert.SequenceEqual(new[] { editedShortcuts, WidgetSettings.Default.SpotlightShortcuts }, shortcutRegistrations);
                TestAssert.True(window.StartupErrorText.Text.Contains("Test shortcut rollback failed.", StringComparison.Ordinal));
                TestAssert.True(window.StartupErrorText.Text.Contains(settingsSaveFails ? "Settings could not be saved." : "Test startup access denied.", StringComparison.Ordinal));

                if (settingsSaveFails)
                {
                    TestAssert.True(window.StartupErrorText.Text.Contains("Test startup rollback failed.", StringComparison.Ordinal));
                }
            }
            finally
            {
                window.Close();
            }
        }
    });

    private static SpotlightShortcuts EditUuidShortcut(SettingsWindow window)
    {
        var editedShortcuts = WidgetSettings.Default.SpotlightShortcuts with
        {
            Uuid = new GlobalShortcut
            {
                Modifiers = GlobalShortcut.ControlModifier,
                VirtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.U)
            }
        };
        ((ShortcutSettingsViewModel)window.DataContext)[SpotlightActionIds.Uuid].Shortcut = editedShortcuts.Uuid;
        return editedShortcuts;
    }

    internal static void ScreenshotSettingsNavigateAndPersistTheToggle() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var window = new SettingsWindow(store, WidgetSettings.Default with { OpenScreenshotEditorAfterCapture = false }, _ => null, _ => { }, () => { }, _ => null);

        try
        {
            window.Show();
            window.SettingsNavigationListBox.SelectedIndex = 1;
            TestAssert.Equal(Visibility.Visible, window.ScreenshotSettingsPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.WidgetSettingsPanel.Visibility);
            TestAssert.Equal(-1, window.SpotlightNavigationListBox.SelectedIndex);
            TestAssert.False(window.OpenScreenshotEditorCheckBox.IsChecked == true);
            TestAssert.True(window.KeepScreenshotHistoryCheckBox.IsChecked == true);
            window.KeepScreenshotHistoryCheckBox.IsChecked = false;
            window.OpenScreenshotEditorCheckBox.IsChecked = true;
            SaveSettings(window);
            TestAssert.True(store.Load().Settings.OpenScreenshotEditorAfterCapture);
            TestAssert.False(store.Load().Settings.KeepScreenshotHistory);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void ScreenshotSettingsSavePreservesNewerEditorOptions() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var currentOptions = new ScreenshotEditorOptions();
        var window = new SettingsWindow(store, WidgetSettings.Default, _ => null, _ => { }, () => { }, _ => null, () => currentOptions);

        try
        {
            window.Show();
            currentOptions = currentOptions with { StrokeWidth = 8, BlurRadius = 24 };
            SaveSettings(window);
            TestAssert.Equal(currentOptions, store.Load().Settings.ScreenshotEditor);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void WidgetAppearancePreviewTracksUnsavedSettingsAndIdleState() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), _ => null);

        try
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Show();
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(ClockWithSeconds, window.WidgetPreviewTimeText.Text);

            foreach (var opacity in new[] { window.OpacitySlider.Minimum, window.OpacitySlider.Maximum })
            {
                window.OpacitySlider.Value = opacity;
                UpdateBindingsAndLayout(window);
                TestAssert.Equal(opacity, window.WidgetPreview.Opacity);
            }

            foreach (var opacity in new[] { window.BackgroundOpacitySlider.Minimum, window.BackgroundOpacitySlider.Maximum })
            {
                window.BackgroundOpacitySlider.Value = opacity;
                UpdateBindingsAndLayout(window);
                TestAssert.Equal(opacity, window.WidgetPreview.Background.Opacity);
            }

            window.PreviewIdleCheckBox.IsChecked = true;

            foreach (var opacity in new[] { window.FadedOpacitySlider.Minimum, window.FadedOpacitySlider.Maximum })
            {
                window.FadedOpacitySlider.Value = opacity;
                UpdateBindingsAndLayout(window);
                TestAssert.Equal(opacity, window.WidgetPreview.Opacity);
            }

            window.PreviewIdleCheckBox.IsChecked = false;
            window.ShowSecondsCheckBox.IsChecked = false;
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(window.OpacitySlider.Value, window.WidgetPreview.Opacity);
            TestAssert.Equal(ClockWithoutSeconds, window.WidgetPreviewTimeText.Text);
            var previewPosition = window.WidgetPreview.TranslatePoint(new Point(), window.SettingsContentScrollViewer);
            TestAssert.True(previewPosition.X >= 0);
            TestAssert.True(previewPosition.X + window.WidgetPreview.ActualWidth <= window.SettingsContentScrollViewer.ActualWidth);
            window.SettingsNavigationListBox.SelectedIndex = 1;
            window.SettingsNavigationListBox.SelectedIndex = 0;
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(ClockWithoutSeconds, window.WidgetPreviewTimeText.Text);
            TestAssert.Equal(window.OpacitySlider.Value, window.WidgetPreview.Opacity);
            TestAssert.False(File.Exists(settingsPath));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void SettingsRenderNativeWorkbenchPreviewsWhenRequested() => RunOnStaThread(() =>
    {
        var previewDirectory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");

        if (string.IsNullOrWhiteSpace(previewDirectory))
        {
            return;
        }

        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = CreateStartupSettingsWindow(new SettingsStore(directory.PathFor("settings.json")), _ => null);

        try
        {
            window.Show();
            Directory.CreateDirectory(previewDirectory);
            ExportPreview(window, System.IO.Path.Combine(previewDirectory, "settings-widget.png"));
            window.SettingsNavigationListBox.SelectedIndex = 1;
            ExportPreview(window, System.IO.Path.Combine(previewDirectory, "settings-screenshots.png"));
            window.SettingsNavigationListBox.SelectedIndex = 2;
            ExportPreview(window, System.IO.Path.Combine(previewDirectory, "settings-battery.png"));

            foreach (var (pageIndex, pageName) in new[] { (0, "spotlight"), (1, "uuid"), (2, "password"), (3, "jwt-secret"), (4, "text-tools") })
            {
                window.SpotlightNavigationListBox.SelectedIndex = pageIndex;
                ExportPreview(window, System.IO.Path.Combine(previewDirectory, $"settings-{pageName}.png"));
            }

            window.SettingsNavigationListBox.SelectedIndex = 0;
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            ExportPreview(window, System.IO.Path.Combine(previewDirectory, "settings-widget-compact.png"));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void PaletteShortcutCanBeCapturedAndSavedFromGeneralSettings() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.PathFor("settings.json"));
        var registrations = new List<SpotlightShortcuts>();
        Action<ShortcutCaptureResult>? capture = null;
        var captureEnded = false;
        var window = new SettingsWindow(store, WidgetSettings.Default, shortcuts =>
        {
            registrations.Add(shortcuts);
            return null;
        }, callback => capture = callback, () => captureEnded = true, _ => null);

        try
        {
            window.Show();
            window.SpotlightNavigationListBox.SelectedIndex = SpotlightGeneralPageIndex;
            UpdateBindingsAndLayout(window);
            TestAssert.Equal(Visibility.Visible, window.SpotlightGeneralSettingsPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.WidgetSettingsPanel.Visibility);
            var input = FindDescendants<TextBox>(window).Single(textBox => Equals(textBox.Tag, ShortcutSettingsViewModel.PaletteSettingId));
            TestAssert.Equal("Alt+2", input.Text);
            input.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, null, input)
            {
                RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent
            });
            TestAssert.True(capture is not null);
            capture!(ShortcutCaptureResult.Capture(TestPalette));
            UpdateBindingsAndLayout(window);
            TestAssert.Equal("Ctrl+Shift+K", input.Text);

            SaveSettings(window);

            TestAssert.Equal(TestPalette, registrations.Single().Palette);
            TestAssert.Equal(TestPalette, store.Load().Settings.SpotlightShortcuts.Palette);
            TestAssert.True(captureEnded);
        }
        finally
        {
            window.Close();
        }
    });

    internal static void PaletteResetRestoresDefaultAndConflictsStayVisible() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        var registrations = new List<SpotlightShortcuts>();
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), _ => null, shortcuts =>
        {
            registrations.Add(shortcuts);
            return null;
        });

        try
        {
            window.Show();
            window.SpotlightNavigationListBox.SelectedIndex = SpotlightGeneralPageIndex;
            var viewModel = (ShortcutSettingsViewModel)window.DataContext;
            viewModel.Palette.Shortcut = TestPalette;
            UpdateBindingsAndLayout(window);
            var reset = FindDescendants<Button>(window).Single(button => Equals(button.Tag, ShortcutSettingsViewModel.PaletteSettingId));
            TestAssert.Equal("Reset", reset.Content);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(SpotlightShortcuts.DefaultPalette, viewModel.Palette.Shortcut);

            viewModel.Palette.Shortcut = TestPalette;
            viewModel[SpotlightActionIds.Uuid].Shortcut = TestPalette;
            SaveSettings(window);

            TestAssert.Equal("Ctrl+Shift+K is reserved for opening Spotlight.", window.ShortcutErrorText.Text);
            TestAssert.Equal(Visibility.Visible, window.ShortcutErrorText.Visibility);
            TestAssert.Empty(registrations);
            TestAssert.True(window.IsVisible);
            TestAssert.False(File.Exists(settingsPath));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void PaletteRegistrationFailureDoesNotPersistSettings() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        const string conflictMessage = "Ctrl+Shift+K is already in use by another app.";
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), _ => null, _ => conflictMessage);

        try
        {
            window.Show();
            ((ShortcutSettingsViewModel)window.DataContext).Palette.Shortcut = TestPalette;
            SaveSettings(window);

            TestAssert.Equal(conflictMessage, window.ShortcutErrorText.Text);
            TestAssert.Equal(Visibility.Visible, window.ShortcutErrorText.Visibility);
            TestAssert.True(window.IsVisible);
            TestAssert.False(File.Exists(settingsPath));
        }
        finally
        {
            window.Close();
        }
    });

    internal static void FailedSettingsSaveRestoresPreviousPaletteShortcut() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        Directory.CreateDirectory(settingsPath);
        var registrations = new List<SpotlightShortcuts>();
        var window = CreateStartupSettingsWindow(new SettingsStore(settingsPath), _ => null, shortcuts =>
        {
            registrations.Add(shortcuts);
            return null;
        });

        try
        {
            window.Show();
            ((ShortcutSettingsViewModel)window.DataContext).Palette.Shortcut = TestPalette;
            SaveSettings(window);

            TestAssert.SequenceEqual(new[] { TestPalette, SpotlightShortcuts.DefaultPalette }, registrations.Select(shortcuts => shortcuts.Palette));
            TestAssert.Equal(Visibility.Visible, window.StartupErrorText.Visibility);
            TestAssert.True(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    private static void ExportPreview(SettingsWindow window, string path)
    {
        UpdateBindingsAndLayout(window);
        window.SettingsContentScrollViewer.ScrollToTop();
        UpdateBindingsAndLayout(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var content = (FrameworkElement)window.Content;
        var rendered = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            StandardDpi * dpi.DpiScaleX,
            StandardDpi * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void UpdateBindingsAndLayout(SettingsWindow window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        window.UpdateLayout();
    }

    private static SettingsWindow CreateStartupSettingsWindow(SettingsStore store, Func<bool, string?> tryApplyStartup, Func<SpotlightShortcuts, string?>? tryApplySpotlightShortcuts = null) => new(
        store,
        WidgetSettings.Default,
        tryApplySpotlightShortcuts ?? (_ => null),
        _ => { },
        () => { },
        tryApplyStartup);

    private static void SaveSettings(SettingsWindow window)
    {
        window.UpdateLayout();
        FindDescendants<Button>(window).Single(button => Equals(button.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
}
