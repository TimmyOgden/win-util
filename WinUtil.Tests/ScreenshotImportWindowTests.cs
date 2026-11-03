using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using WinUtil.Services;
using WinUtil.Views;
using Point = System.Windows.Point;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void EditorImportsOpenPasteAndDropIntoNewDocumentsWithoutChangingCurrentEdits() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var filePath = directory.PathFor("source.png");
        var source = CreateImage(TestImageWidth, TestImageHeight, HighDpi);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(filePath)) { encoder.Save(stream); }
        var imported = new List<BitmapSource>();
        var window = new ScreenshotEditorWindow(source, openImageEditor: imported.Add,
            selectOpenPath: () => filePath, readClipboardImage: () => source);
        try
        {
            window.CommitAnnotation(new Point(12, 12), new Point(100, 80));
            var edited = Pixels(window.RenderedImage);
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.True(window.OpenImage());
            TestAssert.True(window.PasteImage());
            TestAssert.True(window.ImportImageFiles([filePath]));
            TestAssert.Equal(3, imported.Count);
            foreach (var image in imported)
            {
                TestAssert.True(image.IsFrozen);
                TestAssert.Equal(TestImageWidth, image.PixelWidth);
                TestAssert.Equal(StandardDpi, image.DpiX);
                TestAssert.SequenceEqual(Pixels(source), Pixels(image));
            }
            TestAssert.SequenceEqual(edited, Pixels(window.RenderedImage));
            TestAssert.True(window.HasUnsavedChanges);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void EditorImportCancellationAndInvalidFilesLeaveTheDocumentUntouched() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var imports = 0;
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            openImageEditor: _ => imports++, selectOpenPath: () => null);
        try
        {
            var initial = Pixels(window.RenderedImage);
            TestAssert.False(window.OpenImage());
            foreach (var paths in new IReadOnlyList<string>?[] { null, [], ["one.png", "two.png"], [""], [directory.PathFor("missing-image.png")] })
            {
                TestAssert.False(window.ImportImageFiles(paths));
                TestAssert.True(window.StatusText.Text.StartsWith("Could not open", StringComparison.Ordinal));
            }
            TestAssert.Equal(0, imports);
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.False(window.UndoButton.IsEnabled);
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void EditorPasteReportsMissingLockedClipboardAndOpenFailures() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        foreach (var read in new Func<BitmapSource?>[] { () => null, () => throw new ExternalException("Clipboard is busy.") })
        {
            var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
                readClipboardImage: read, openImageEditor: _ => throw new InvalidOperationException("Must not open."));
            try
            {
                TestAssert.False(window.PasteImage());
                TestAssert.True(window.StatusText.Text.StartsWith("Could not paste", StringComparison.Ordinal));
                TestAssert.False(window.HasUnsavedChanges);
            }
            finally { window.CloseAfterConfirmation(); }
        }
        var failed = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            readClipboardImage: () => CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            openImageEditor: _ => throw new InvalidOperationException("Could not create editor."),
            selectOpenPath: () => throw new IOException("Dialog unavailable."));
        try
        {
            TestAssert.False(failed.PasteImage());
            TestAssert.True(failed.StatusText.Text.Contains("Could not create editor", StringComparison.Ordinal));
            TestAssert.False(failed.OpenImage());
            TestAssert.True(failed.StatusText.Text.Contains("Dialog unavailable", StringComparison.Ordinal));
        }
        finally { failed.CloseAfterConfirmation(); }
    });

    internal static void ResizeDialogLinksDimensionsAndBlocksInvalidInput() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var owner = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        owner.Show();
        var dialog = new ScreenshotResizeDialog(owner, TestImageWidth, TestImageHeight);
        try
        {
            dialog.WidthInput.Text = "80";
            TestAssert.Equal(80, dialog.PixelWidth);
            TestAssert.Equal(50, dialog.PixelHeight);
            TestAssert.Equal("50", dialog.HeightInput.Text);
            dialog.HeightInput.Text = "100";
            TestAssert.Equal(TestImageWidth, dialog.PixelWidth);
            TestAssert.Equal("160", dialog.WidthInput.Text);
            foreach (var text in new[] { "", "bad", "0", "-1", "9999999999999999", "32769" })
            {
                dialog.WidthInput.Text = text;
                TestAssert.False(dialog.ApplyButton.IsEnabled);
                TestAssert.Equal(Visibility.Visible, dialog.ErrorText.Visibility);
            }
            dialog.WidthInput.Text = "160";
            TestAssert.True(dialog.ApplyButton.IsEnabled);
            TestAssert.Equal(Visibility.Collapsed, dialog.ErrorText.Visibility);
        }
        finally { dialog.Close(); owner.CloseAfterConfirmation(); }
    });
}
