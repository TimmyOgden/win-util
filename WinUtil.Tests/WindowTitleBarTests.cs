using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;
using WinUtil.Views.Controls;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void SettingsTitleBarSupportsWindowControlsAndClosesWithoutSaving() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.PathFor("settings.json");
        var window = new SettingsWindow(new SettingsStore(settingsPath), WidgetSettings.Default,
            _ => null, _ => { }, () => { }, _ => null);
        try
        {
            window.Show();
            window.UpdateLayout();
            TestAssert.Equal(WindowStyle.None, window.WindowStyle);
            TestAssert.True(window.Icon is DrawingImage);
            var titleBar = (ScreenshotTitleBar)window.FindName("SettingsTitleBar");
            TestAssert.Equal(window.FindResource("TextPrimaryBrush"), titleBar.Foreground);
            TestAssert.Equal(titleBar.Height, WindowChrome.GetWindowChrome(window).CaptionHeight);
            var maximize = (Button)titleBar.FindName("MaximizeButton");
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Maximized, window.WindowState);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Normal, window.WindowState);
            ((Button)titleBar.FindName("MinimizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Minimized, window.WindowState);
            window.WindowState = WindowState.Normal;
            window.RunAtStartupCheckBox.IsChecked = false;
            ((Button)titleBar.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.IsVisible);
            TestAssert.False(File.Exists(settingsPath));
        }
        finally { window.Close(); }
    });

    internal static void SharedTitleBarRespectsEveryWindowResizeMode() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.Show();
            var titleBar = (ScreenshotTitleBar)window.FindName("EditorTitleBar");
            foreach (var mode in Enum.GetValues<ResizeMode>())
            {
                window.ResizeMode = mode;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                window.UpdateLayout();
                TestAssert.Equal(mode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible,
                    ((Button)titleBar.FindName("MinimizeButton")).Visibility);
                TestAssert.Equal(mode is ResizeMode.NoResize or ResizeMode.CanMinimize ? Visibility.Collapsed : Visibility.Visible,
                    ((Button)titleBar.FindName("MaximizeButton")).Visibility);
                TestAssert.Equal(Visibility.Visible, ((Button)titleBar.FindName("CloseButton")).Visibility);
            }
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void ScreenshotDialogTitleBarsCancelWithoutApplyingChanges() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var owner = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            owner.Show();
            foreach (var dialog in new Window[]
            {
                new ScreenshotResizeDialog(owner, TestImageWidth, TestImageHeight),
                new ScreenshotCloseDialog(owner)
            })
            {
                TestAssert.False(ShowDialogAndAct(dialog, () =>
                {
                    var titleBar = ((DockPanel)dialog.Content).Children.OfType<ScreenshotTitleBar>().Single();
                    TestAssert.Equal(WindowStyle.None, dialog.WindowStyle);
                    TestAssert.Equal(ResizeMode.NoResize, dialog.ResizeMode);
                    TestAssert.True(dialog.Icon is DrawingImage);
                    TestAssert.False(dialog.ShowInTaskbar);
                    TestAssert.Equal(owner, dialog.Owner);
                    TestAssert.Equal(titleBar.Height, WindowChrome.GetWindowChrome(dialog).CaptionHeight);
                    TestAssert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(dialog).ResizeBorderThickness);
                    TestAssert.Equal(Visibility.Collapsed, ((Button)titleBar.FindName("MinimizeButton")).Visibility);
                    TestAssert.Equal(Visibility.Collapsed, ((Button)titleBar.FindName("MaximizeButton")).Visibility);
                    RenderTitleBarPreviewWhenRequested(dialog, dialog is ScreenshotResizeDialog ? "resize-dialog.png" : "unsaved-dialog.png");
                    ((Button)titleBar.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }) == true);
                if (dialog is ScreenshotCloseDialog closeDialog) { TestAssert.Equal(ScreenshotCloseChoice.Cancel, closeDialog.Choice); }
                TestAssert.False(owner.HasUnsavedChanges);
            }
        }
        finally { owner.CloseAfterConfirmation(); }
    });

    internal static void CustomDialogChromePreservesSaveDiscardCancelAndResizeResults() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var owner = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            owner.Show();
            foreach (var choice in Enum.GetValues<ScreenshotCloseChoice>())
            {
                var dialog = new ScreenshotCloseDialog(owner);
                var result = ShowDialogAndAct(dialog, () =>
                {
                    var panel = ((DockPanel)dialog.Content).Children.OfType<StackPanel>().Single();
                    var buttons = panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>();
                    buttons.Single(button => Equals(button.Content, choice.ToString())).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                TestAssert.Equal(choice != ScreenshotCloseChoice.Cancel, result == true);
                TestAssert.Equal(choice, dialog.Choice);
            }
            var resize = new ScreenshotResizeDialog(owner, TestImageWidth, TestImageHeight);
            TestAssert.True(ShowDialogAndAct(resize, () =>
            {
                resize.WidthInput.Text = (TestImageWidth / 2).ToString();
                resize.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }) == true);
            TestAssert.Equal(TestImageWidth / 2, resize.PixelWidth);
            TestAssert.Equal(TestImageHeight / 2, resize.PixelHeight);
        }
        finally { owner.CloseAfterConfirmation(); }
    });

    private static bool? ShowDialogAndAct(Window dialog, Action action)
    {
        Exception? failure = null;
        dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { if (dialog.IsVisible) { dialog.Close(); } }
        }), DispatcherPriority.ApplicationIdle);
        var result = dialog.ShowDialog();
        if (failure is not null) { throw new InvalidOperationException(failure.Message, failure); }
        return result;
    }

    private static void RenderTitleBarPreviewWhenRequested(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var content = (FrameworkElement)window.Content;
        var rendered = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), StandardDpi * dpi.DpiScaleX, StandardDpi * dpi.DpiScaleY, PixelFormats.Pbgra32);
        rendered.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
}
