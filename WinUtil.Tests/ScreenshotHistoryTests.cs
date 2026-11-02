using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;
using WinUtil.Views.Controls;
using Point = System.Windows.Point;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void HistoryPersistsAllSourcesAndOriginalPixelsAcrossStoreInstances() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("history");
        var store = new ScreenshotHistoryStore(path);
        TestAssert.Empty(store.List());
        var image = CreateImage(TestImageWidth, TestImageHeight, HighDpi);
        var entries = Enum.GetValues<ScreenshotHistorySource>().Select(source => store.Add(image, source)).ToArray();
        var restored = new ScreenshotHistoryStore(path);
        var history = restored.List();
        TestAssert.Equal(entries.Length, history.Count);
        TestAssert.SequenceEqual(history.OrderByDescending(entry => entry.CreatedAt).Select(entry => entry.FileName), history.Select(entry => entry.FileName));
        foreach (var entry in history)
        {
            var loaded = restored.Load(entry);
            TestAssert.SequenceEqual(Pixels(image), Pixels(loaded));
            TestAssert.Equal(image.PixelWidth, loaded.PixelWidth);
            TestAssert.True(loaded.IsFrozen);
            TestAssert.True(entry.SizeBytes > 0);
            TestAssert.True(entry.DisplayName.Length > 0 && entry.CreatedText.Length > 0 && entry.SizeText.Length > 0);
        }
        TestAssert.Equal(entries.Length, Directory.GetFiles(path).Length);
        File.WriteAllText(directory.PathFor("history", "unrelated.png"), "not history");
        File.WriteAllText(directory.PathFor("history", "incomplete.pending"), "not complete");
        TestAssert.Equal(entries.Length, restored.List().Count);
    });

    internal static void HistoryRejectsInvalidImagesForeignPathsAndUnavailableStorage() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var store = new ScreenshotHistoryStore(directory.PathFor("history"));
        var image = CreateImage(TestImageWidth, TestImageHeight, StandardDpi);
        ExpectHistoryFailure<ArgumentNullException>(() => store.Add(null!, ScreenshotHistorySource.Capture));
        ExpectHistoryFailure<ArgumentOutOfRangeException>(() => store.Add(image, (ScreenshotHistorySource)int.MaxValue));
        ExpectHistoryFailure<ArgumentNullException>(() => store.Load(null!));
        ExpectHistoryFailure<ArgumentNullException>(() => store.LoadPreview(null!));
        var entry = store.Add(image, ScreenshotHistorySource.Capture);
        foreach (var invalidName in new[] { "", " ", "../" + entry.FileName, "..\\" + entry.FileName, directory.PathFor(entry.FileName) })
        {
            var invalid = entry with { FileName = invalidName };
            ExpectHistoryFailure<ArgumentException>(() => store.Load(invalid));
            ExpectHistoryFailure<ArgumentException>(() => store.LoadPreview(invalid));
        }
        File.WriteAllText(directory.PathFor("blocked"), "file instead of directory");
        ExpectHistoryFailure<IOException>(() => new ScreenshotHistoryStore(directory.PathFor("blocked")).Add(image, ScreenshotHistorySource.Capture));
        File.WriteAllText(directory.PathFor("history", entry.FileName), "corrupt PNG");
        ExpectHistoryFailure<InvalidDataException>(() => store.Load(entry));
    });

    internal static void HistoryPreviewsBoundBothOrientationsAndSupportMaximumDimensions() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var store = new ScreenshotHistoryStore(directory.PathFor("history"));
        foreach (var (width, height) in new[] { (TestImageWidth, TestImageHeight), (ScreenshotDocument.MaximumSourceDimension, 1), (1, ScreenshotDocument.MaximumSourceDimension) })
        {
            var entry = store.Add(CreateImage(width, height, StandardDpi), ScreenshotHistorySource.Capture);
            var preview = store.LoadPreview(entry);
            TestAssert.True(preview.PixelWidth <= ScreenshotHistoryStore.MaximumPreviewDimensionPixels);
            TestAssert.True(preview.PixelHeight <= ScreenshotHistoryStore.MaximumPreviewDimensionPixels);
            TestAssert.True(preview.IsFrozen);
            var loaded = store.Load(entry);
            TestAssert.Equal(width, loaded.PixelWidth);
            TestAssert.Equal(height, loaded.PixelHeight);
        }
    });

    internal static void HistoryWindowListsPreviewsReopensAndReportsMissingImages() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new ScreenshotHistoryStore(directory.PathFor("history"));
        BitmapSource? opened = null;
        var window = new ScreenshotHistoryWindow(store, image => opened = image);
        try
        {
            window.Show();
            TestAssert.Equal(Visibility.Visible, window.EmptyText.Visibility);
            TestAssert.False(window.OpenSelectedImage());
            TestAssert.False(window.OpenEditorButton.IsEnabled);
            var entry = store.Add(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), ScreenshotHistorySource.Capture);
            window.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(1, window.HistoryList.Items.Count);
            TestAssert.Equal(Visibility.Collapsed, window.EmptyText.Visibility);
            TestAssert.True(window.PreviewImage.Source is BitmapSource);
            TestAssert.True(window.OpenSelectedImage());
            TestAssert.Equal(TestImageWidth, opened!.PixelWidth);
            TestAssert.Equal(1, store.List().Count);
            File.Move(directory.PathFor("history", entry.FileName), directory.PathFor("removed.png"));
            TestAssert.False(window.OpenSelectedImage());
            TestAssert.True(window.StatusText.Text.Contains("Could not open", StringComparison.Ordinal));
            window.RefreshHistory();
            TestAssert.Equal(Visibility.Visible, window.EmptyText.Visibility);
            TestAssert.Equal(null, window.PreviewImage.Source);
            TestAssert.False(window.OpenEditorButton.IsEnabled);
            File.Move(directory.PathFor("removed.png"), directory.PathFor("history", entry.FileName));
            File.WriteAllText(directory.PathFor("history", entry.FileName), "corrupt");
            window.RefreshHistory();
            TestAssert.False(window.OpenEditorButton.IsEnabled);
            TestAssert.True(window.StatusText.Text.Contains("Could not preview", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    internal static void ScreenshotTitleBarControlsPreserveCloseProtectionAndHistoryAction() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var historyRequests = 0;
        var choice = ScreenshotCloseChoice.Cancel;
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            promptClose: () => choice, showHistory: () => historyRequests++);
        try
        {
            window.Show();
            TestAssert.Equal(WindowStyle.None, window.WindowStyle);
            TestAssert.True(window.Icon is DrawingImage);
            TestAssert.Equal(40d, WindowChrome.GetWindowChrome(window).CaptionHeight);
            var titleBar = (ScreenshotTitleBar)window.FindName("EditorTitleBar");
            var maximize = (Button)titleBar.FindName("MaximizeButton");
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Maximized, window.WindowState);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Normal, window.WindowState);
            ((Button)titleBar.FindName("MinimizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(WindowState.Minimized, window.WindowState);
            window.WindowState = WindowState.Normal;
            window.HistoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(1, historyRequests);
            window.CommitAnnotation(new Point(8, 8), new Point(80, 60));
            TestAssert.True(window.Title.Contains("Unsaved", StringComparison.Ordinal));
            ((Button)titleBar.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.True(window.IsVisible);
            choice = ScreenshotCloseChoice.Discard;
            ((Button)titleBar.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.IsVisible);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void AppRecordsCapturesImportsAndExportsAndHonorsDisabledHistory() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var app = (App)System.Windows.Application.Current;
        var widgetField = typeof(App).GetField("widgetWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var historyField = typeof(App).GetField("screenshotHistoryStore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var editorsField = typeof(App).GetField("screenshotEditors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousWidget = widgetField.GetValue(app);
        var previousHistory = historyField.GetValue(app);
        var editors = (HashSet<ScreenshotEditorWindow>)editorsField.GetValue(app)!;
        var existing = editors.ToArray();
        var settings = WidgetSettings.Default with { OpenScreenshotEditorAfterCapture = false };
        var widget = new WidgetWindow(settings, _ => { }, new ImportBatteryProvider(), new ImportAudioProvider());
        var store = new ScreenshotHistoryStore(directory.PathFor("history"));
        widgetField.SetValue(app, widget);
        historyField.SetValue(app, store);
        try
        {
            var image = CreateImage(TestImageWidth, TestImageHeight, StandardDpi);
            InvokeImageMethod(app, "ShowScreenshotEditor", image);
            TestAssert.Equal(existing.Length, editors.Count);
            TestAssert.Equal(1, store.List().Count);
            InvokeImageMethod(app, "OpenImageEditor", image);
            var editor = TestAssert.Single(editors.Except(existing).ToArray());
            TestAssert.Equal(2, store.List().Count);
            editor.SavePng(directory.PathFor("export.png"));
            TestAssert.Equal(3, store.List().Count);
            InvokeImageMethod(app, "OpenEditor", image);
            TestAssert.Equal(3, store.List().Count);
            widget.ApplySettings(settings with { KeepScreenshotHistory = false });
            InvokeImageMethod(app, "ShowScreenshotEditor", image);
            InvokeImageMethod(app, "OpenImageEditor", image);
            editor.SavePng(directory.PathFor("disabled-export.png"));
            TestAssert.Equal(3, store.List().Count);
        }
        finally
        {
            foreach (var editor in editors.Except(existing).ToArray()) { editor.CloseAfterConfirmation(); }
            widgetField.SetValue(app, previousWidget);
            historyField.SetValue(app, previousHistory);
            widget.AllowClose();
        }
    });

    internal static void EditorPublishesTheExactSavedAndCopiedPixelsToHistory() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var exports = new List<BitmapSource>();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), copyImage: _ => { });
        window.ImageExported += exports.Add;
        try
        {
            window.CommitAnnotation(new Point(8, 8), new Point(80, 60));
            window.SavePng(directory.PathFor("export.png"));
            window.CopyImageAsync().GetAwaiter().GetResult();
            TestAssert.Equal(2, exports.Count);
            foreach (var image in exports) { TestAssert.SequenceEqual(Pixels(window.RenderedImage), Pixels(image)); }
            TestAssert.False(window.HistoryButton.IsEnabled);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void HistoryRendersSyntheticPreviewWhenRequested() => RunOnStaThread(() =>
    {
        var editorPreviewPath = Environment.GetEnvironmentVariable("WINUTIL_SCREENSHOT_EDITOR_PREVIEW_PATH");
        if (string.IsNullOrWhiteSpace(editorPreviewPath)) { return; }
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var store = new ScreenshotHistoryStore(directory.PathFor("history"));
        store.Add(CreateSyntheticScreenshot(), ScreenshotHistorySource.Capture);
        store.Add(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), ScreenshotHistorySource.Import);
        var window = new ScreenshotHistoryWindow(store, _ => { });
        try
        {
            window.Show();
            window.HistoryList.SelectedIndex = 1;
            window.UpdateLayout();
            var rendered = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, StandardDpi, StandardDpi, PixelFormats.Pbgra32);
            rendered.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(editorPreviewPath))!;
            Directory.CreateDirectory(outputDirectory);
            using var stream = File.Create(Path.Combine(outputDirectory, "screenshot-history.png"));
            encoder.Save(stream);
        }
        finally { window.Close(); }
    });

    private static void ExpectHistoryFailure<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
