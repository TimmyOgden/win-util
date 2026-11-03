using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;
using Point = System.Windows.Point;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void CropSelectionPreviewsCancelsAndAppliesAsOneUndoableChange() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, HighDpi));
        try
        {
            var original = Pixels(window.RenderedImage);
            window.PreviewCropSelection(new Point(20, 20));
            TestAssert.False(window.ApplyCropSelection());
            window.BeginCropSelection(new Point(12.3, 10.2));
            window.PreviewCropSelection(new Point(70.8, 60.9));
            window.CompleteCropSelection(new Point(70.8, 60.9));
            TestAssert.Equal<Int32Rect?>(new Int32Rect(12, 10, 59, 51), window.CropSelectionBounds);
            TestAssert.True(window.ApplyCropButton.IsEnabled);
            TestAssert.True(window.AnnotationCanvas.Children.Count > 0);
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.SequenceEqual(original, Pixels(window.RenderedImage));
            window.CancelCropMode();
            TestAssert.Equal<Int32Rect?>(null, window.CropSelectionBounds);
            TestAssert.False(window.UndoButton.IsEnabled);
            window.BeginCropSelection(new Point(70, 60));
            window.CompleteCropSelection(new Point(10, 10));
            window.ApplyCropButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(60, window.RenderedImage.PixelWidth);
            TestAssert.Equal(50, window.RenderedImage.PixelHeight);
            TestAssert.Equal(60d, window.AnnotationCanvas.Width);
            TestAssert.True(window.HasUnsavedChanges);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.False(window.UndoButton.IsEnabled);
            TestAssert.SequenceEqual(original, Pixels(window.RenderedImage));
            window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(60, window.RenderedImage.PixelWidth);
            ExportTransformedEditorWhenRequested();
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void ImageTransformsRefreshDimensionsAndExportCompleteDirtyState() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            TestAssert.True(window.ResizeImage(TestImageWidth / 2, TestImageHeight / 2));
            window.RotateRightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(TestImageHeight / 2, window.RenderedImage.PixelWidth);
            TestAssert.Equal(TestImageWidth / 2, window.RenderedImage.PixelHeight);
            TestAssert.Equal((double)window.RenderedImage.PixelWidth, window.ImageSurface.Width);
            TestAssert.Equal((double)window.RenderedImage.PixelHeight, window.AnnotationCanvas.Height);
            TestAssert.True(window.ImageDetailsText.Text.Contains("50 × 80", StringComparison.Ordinal));
            var path = directory.PathFor("transformed.png");
            window.SavePng(path);
            TestAssert.False(window.HasUnsavedChanges);
            using var stream = File.OpenRead(path);
            var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            TestAssert.Equal(window.RenderedImage.PixelWidth, decoded.Frames[0].PixelWidth);
            TestAssert.Equal(window.RenderedImage.PixelHeight, decoded.Frames[0].PixelHeight);
            window.RotateLeftButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.True(window.HasUnsavedChanges);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.HasUnsavedChanges);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.Equal(TestImageWidth / 2, window.RenderedImage.PixelWidth);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void TransformedAnnotationsUseProjectedSelectionHandlesAndLocalMovement() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(20, 10), new Point(100, 60));
            window.RotateImageClockwise();
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(65, 60)));
            TestAssert.True(window.BeginSelectionDrag(new Point(40, 100)));
            window.CompleteSelectionDrag(new Point(30, 120));
            TestAssert.Equal(new Point(120, 70), window.Annotations[0].End);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(new Point(100, 60), window.Annotations[0].End);
            TestAssert.Equal(TestImageHeight, window.RenderedImage.PixelWidth);
            window.SelectAnnotationAt(new Point(65, 60));
            window.BeginSelectionDrag(new Point(65, 60));
            window.CompleteSelectionDrag(new Point(70, 70));
            TestAssert.Equal(new Point(30, 5), window.Annotations[0].Start);
            TestAssert.Equal(new Point(110, 55), window.Annotations[0].End);
            TestAssert.Equal(Matrix.Identity, window.Annotations[0].LocalToSource);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void NewAnnotationsAfterRotationKeepTheirCreationCoordinatesWhenEdited() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.RotateImageClockwise();
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(10, 20), new Point(50, 60));
            var creationMatrix = window.Annotations[0].LocalToSource;
            TestAssert.False(creationMatrix.IsIdentity);
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(30, 40)));
            window.RotateImageCounterclockwise();
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(40, 70)));
            window.BeginSelectionDrag(new Point(40, 70));
            window.CompleteSelectionDrag(new Point(50, 70));
            TestAssert.Equal(new Point(10, 30), window.Annotations[0].Start);
            TestAssert.Equal(new Point(50, 70), window.Annotations[0].End);
            TestAssert.Equal(creationMatrix, window.Annotations[0].LocalToSource);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void CroppedAnnotationsRetainHiddenGeometryDuringMovement() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(10, 10), new Point(100, 80));
            window.CropImage(new Int32Rect(50, 20, 80, 60));
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(20, 30)));
            window.BeginSelectionDrag(new Point(20, 30));
            window.CompleteSelectionDrag(new Point(25, 30));
            var moved = window.Annotations[0];
            TestAssert.Equal(new Point(15, 10), moved.Start);
            TestAssert.Equal(new Point(105, 80), moved.End);
            TestAssert.Equal(70d, Math.Abs(moved.End.Y - moved.Start.Y));
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(new Point(10, 10), window.Annotations[0].Start);
            TestAssert.Equal(80, window.RenderedImage.PixelWidth);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(TestImageWidth, window.RenderedImage.PixelWidth);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void TextEditingPreservesClippedAnchorsAndCreationMatrices() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.BeginTextEntry(new Point(10, 10));
            window.AnnotationTextBox.Text = "Original text";
            window.FinishTextEntry(commit: true);
            window.CropImage(new Int32Rect(30, 0, 100, 80));
            window.SavePng(directory.PathFor("clipped.png"));
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(5, 15)));
            var original = window.Annotations[0];
            window.BeginSelectedTextEdit();
            TestAssert.Equal(0d, Canvas.GetLeft(window.TextInputPanel) + 4);
            TestAssert.False(window.HasUnsavedChanges);
            window.AnnotationTextBox.Text = "Updated text";
            window.FinishTextEntry(commit: true);
            TestAssert.Equal(original.Start, window.Annotations[0].Start);
            TestAssert.Equal(original.LocalToSource, window.Annotations[0].LocalToSource);
            window.RotateImageClockwise();
            window.BeginTextEntry(new Point(10, 10));
            window.AnnotationTextBox.Text = "Upright";
            window.FinishTextEntry(commit: true);
            var rotated = window.Annotations[1];
            TestAssert.False(rotated.LocalToSource.IsIdentity);
            TestAssert.Equal<int?>(1, window.SelectAnnotationAt(new Point(12, 12)));
            window.BeginSelectedTextEdit();
            window.AnnotationTextBox.Text = "Still upright";
            window.FinishTextEntry(commit: true);
            TestAssert.Equal(rotated.LocalToSource, window.Annotations[1].LocalToSource);
            TestAssert.Equal(rotated.Start, window.Annotations[1].Start);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void ResizeKeepsAnnotationPointersInDisplayedPixelCoordinates() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.ResizeImage(TestImageWidth / 2, TestImageHeight / 2);
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(10, 10), new Point(40, 30));
            TestAssert.Equal(new Matrix(2, 0, 0, 2, 0, 0), window.Annotations[0].LocalToSource);
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(25, 20)));
            window.BeginSelectionDrag(new Point(25, 20));
            window.CompleteSelectionDrag(new Point(35, 20));
            TestAssert.Equal(new Point(20, 10), window.Annotations[0].Start);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ResizeImage(TestImageWidth, TestImageHeight);
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(50, 40)));
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void TransformDuringClipboardRetryDoesNotMarkTheNewStateClean() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        ScreenshotEditorWindow? window = null;
        var attempts = 0;
        BitmapSource? exported = null;
        window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), copyImage: image =>
        {
            if (++attempts == 1)
            {
                window!.RotateImageClockwise();
                throw new ExternalException("Clipboard busy.");
            }
            exported = image;
        });
        try
        {
            CompleteTask(window.CopyImageAsync());
            TestAssert.Equal(TestImageWidth, exported!.PixelWidth);
            TestAssert.Equal(TestImageHeight, window.RenderedImage.PixelWidth);
            TestAssert.True(window.HasUnsavedChanges);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.HasUnsavedChanges);
        }
        finally
        {
            window.CloseAfterConfirmation();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    });

    internal static void ImageImportShortcutsPreserveNativeTextPaste() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var image = CreateImage(TestImageWidth, TestImageHeight, StandardDpi);
        var reads = 0;
        var opens = 0;
        var imports = 0;
        var window = new ScreenshotEditorWindow(image, openImageEditor: _ => imports++, selectOpenPath: () => { opens++; return null; },
            readClipboardImage: () => { reads++; return image; });
        try
        {
            window.Show();
            window.BeginTextEntry(new Point(10, 10));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            TestAssert.True(window.AnnotationTextBox.IsKeyboardFocused);
            TestAssert.False(window.HandleImageImportKey(Key.V, ModifierKeys.Control));
            TestAssert.Equal(0, reads);
            TestAssert.True(window.HandleImageImportKey(Key.O, ModifierKeys.Control));
            TestAssert.Equal(1, opens);
            window.FinishTextEntry(commit: false);
            window.AnnotationCanvas.Focus();
            TestAssert.True(window.HandleImageImportKey(Key.V, ModifierKeys.Control));
            TestAssert.Equal(1, reads);
            TestAssert.Equal(1, imports);
            TestAssert.False(window.HandleImageImportKey(Key.V, ModifierKeys.Alt));
            TestAssert.False(window.HandleImageImportKey(Key.A, ModifierKeys.Control));
        }
        finally { window.CloseAfterConfirmation(); }
    });

    internal static void EmptyWholeImageAndInvalidTransformRequestsPreserveHistory() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        try
        {
            window.BeginCropSelection(new Point());
            window.CompleteCropSelection(new Point());
            TestAssert.False(window.ApplyCropSelection());
            TestAssert.False(window.CropImage(new Int32Rect(0, 0, TestImageWidth, TestImageHeight)));
            TestAssert.False(window.ResizeImage(TestImageWidth, TestImageHeight));
            ExpectEditorArgument(() => window.BeginCropSelection(new Point(double.NaN, 0)));
            ExpectEditorArgument(() => window.CropImage(new Int32Rect(0, 0, TestImageWidth + 1, TestImageHeight)));
            ExpectEditorArgument(() => window.ResizeImage(0, 0));
            TestAssert.False(window.UndoButton.IsEnabled);
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.True(window.CropImage(new Int32Rect(0, 0, 1, 1)));
            TestAssert.Equal(1, window.RenderedImage.PixelWidth);
            TestAssert.Equal(1, window.RenderedImage.PixelHeight);
        }
        finally { window.CloseAfterConfirmation(); }
    });

    private static void ExpectEditorArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid editor input to be rejected.");
    }

    private static void ExportTransformedEditorWhenRequested()
    {
        var directory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var window = new ScreenshotEditorWindow(CreateSyntheticScreenshot());
        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(56, 254), new Point(904, 478));
            window.BeginTextEntry(new Point(70, 115));
            window.AnnotationTextBox.Text = "Release approved";
            window.FinishTextEntry(commit: true);
            window.CropImage(new Int32Rect(40, 80, 880, 420));
            window.RotateImageClockwise();
            window.ResizeImage(210, 440);
            window.BeginTextEntry(new Point(140, 60));
            window.AnnotationTextBox.Text = "OK";
            window.FinishTextEntry(commit: true);
            window.Show();
            window.SelectAnnotationAt(new Point(68, 220));
            window.UpdateLayout();
            ExportEditorPreview(window, Path.Combine(directory, "screenshot-transformed.png"));
        }
        finally { window.CloseAfterConfirmation(); }
    }
}
