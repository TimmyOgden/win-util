using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinUtil.Models;
using WinUtil.Views;
using Point = System.Windows.Point;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void ScreenshotCloseDialogOffersSaveDiscardAndCancel() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var owner = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));
        ScreenshotCloseDialog? dialog = null;

        try
        {
            owner.Show();
            dialog = new ScreenshotCloseDialog(owner);
            var panel = ((System.Windows.Controls.DockPanel)dialog.Content).Children.OfType<StackPanel>().Single();
            var buttons = panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            TestAssert.SequenceEqual(new[] { "Cancel", "Discard", "Save" }, buttons.Select(button => (string)button.Content));
            TestAssert.True(buttons.Single(button => Equals(button.Content, "Cancel")).IsCancel);
            TestAssert.True(buttons.Single(button => Equals(button.Content, "Save")).IsDefault);
            TestAssert.Equal(ScreenshotCloseChoice.Cancel, dialog.Choice);
        }
        finally
        {
            dialog?.Close();
            owner.CloseAfterConfirmation();
        }
    });

    internal static void EditorCloseProtectionTracksSaveCancelDiscardAndUndo() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var choice = ScreenshotCloseChoice.Cancel;
        string? path = null;
        var promptCount = 0;
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            promptClose: () => { promptCount++; return choice; }, selectSavePath: () => path);

        try
        {
            TestAssert.True(window.ConfirmClose());
            TestAssert.Equal(0, promptCount);
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(12, 12), new Point(80, 60));
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.False(window.ConfirmClose());
            choice = ScreenshotCloseChoice.Discard;
            TestAssert.True(window.ConfirmClose());
            TestAssert.True(window.HasUnsavedChanges);
            choice = ScreenshotCloseChoice.Save;
            TestAssert.False(window.ConfirmClose());
            TestAssert.True(window.HasUnsavedChanges);
            path = directory.PathFor("missing", "edited.png");
            TestAssert.False(window.ConfirmClose());
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.True(window.StatusText.Text.StartsWith("Could not save", StringComparison.Ordinal));
            path = directory.PathFor("edited.png");
            TestAssert.True(window.ConfirmClose());
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.True(File.Exists(path));
            window.CommitAnnotation(new Point(90, 20), new Point(120, 60));
            TestAssert.True(window.HasUnsavedChanges);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.HasUnsavedChanges);
            window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.True(window.HasUnsavedChanges);
            choice = ScreenshotCloseChoice.Cancel;
            window.Show();
            window.Close();
            TestAssert.True(window.IsVisible);
            TestAssert.True(window.HasUnsavedChanges);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorCloseProtectionIncludesPendingTextAndFailedExports() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var choice = ScreenshotCloseChoice.Cancel;
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            promptClose: () => choice, selectSavePath: () => directory.PathFor("pending.png"));

        try
        {
            window.BeginTextEntry(new Point(10, 10));
            TestAssert.False(window.HasUnsavedChanges);
            window.AnnotationTextBox.Text = "Pending text";
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.False(window.ConfirmClose());
            TestAssert.Equal("Pending text", window.AnnotationTextBox.Text);
            choice = ScreenshotCloseChoice.Save;
            TestAssert.True(window.ConfirmClose());
            TestAssert.False(window.HasUnsavedChanges);
            TestAssert.Equal("Pending text", TestAssert.Single(window.Annotations).Text);
            window.SelectAnnotationAt(new Point(12, 12));
            window.BeginSelectedTextEdit();
            TestAssert.False(window.HasUnsavedChanges);
            window.AnnotationTextBox.Text = "Changed text";
            TestAssert.True(window.HasUnsavedChanges);
            window.FinishTextEntry(commit: false);
            TestAssert.False(window.HasUnsavedChanges);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorCopyMarksOnlyTheExportedSnapshotClean() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        ScreenshotEditorWindow? window = null;
        var attempts = 0;
        BitmapSource? copied = null;
        var failCopy = false;
        window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), copyImage: image =>
        {
            attempts++;

            if (failCopy)
            {
                throw new ExternalException("Clipboard busy.");
            }

            if (attempts == 1)
            {
                window!.CommitAnnotation(new Point(90, 20), new Point(120, 60));
                throw new ExternalException("Retry the clipboard.");
            }

            copied = image;
        });

        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(12, 12), new Point(80, 60));
            var firstExport = Pixels(window.RenderedImage);
            CompleteTask(window.CopyImageAsync());
            TestAssert.Equal(2, attempts);
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.SequenceEqual(firstExport, Pixels(copied!));
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.False(window.HasUnsavedChanges);
            window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.True(window.HasUnsavedChanges);
            CompleteTask(window.CopyImageAsync());
            TestAssert.False(window.HasUnsavedChanges);
            window.CommitAnnotation(new Point(30, 70), new Point(60, 90));
            failCopy = true;
            CompleteTask(window.CopyImageAsync());
            TestAssert.True(window.HasUnsavedChanges);
            TestAssert.True(window.CopyButton.IsEnabled);
            TestAssert.True(window.StatusText.Text.StartsWith("Could not copy", StringComparison.Ordinal));
        }
        finally
        {
            window.CloseAfterConfirmation();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    });

    internal static void EditorSelectsTopmostAnnotationsAndMovesWithSingleUndo() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, HighDpi));

        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(10, 10), new Point(70, 60));
            window.CommitAnnotation(new Point(30, 20), new Point(100, 70));
            TestAssert.Equal<int?>(1, window.SelectAnnotationAt(new Point(50, 40)));
            var before = Pixels(window.RenderedImage);
            TestAssert.True(window.AnnotationCanvas.Children.Count > 0);
            TestAssert.True(window.BeginSelectionDrag(new Point(50, 40)));
            window.PreviewSelectionDrag(new Point(60, 45));
            window.PreviewSelectionDrag(new Point(70, 50));
            TestAssert.Equal(new Point(30, 20), window.Annotations[1].Start);
            window.CompleteSelectionDrag(new Point(70, 50));
            TestAssert.Equal(new Point(50, 30), window.Annotations[1].Start);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(before, Pixels(window.RenderedImage));
            window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(new Point(50, 30), window.Annotations[1].Start);
            window.SelectAnnotationAt(new Point(80, 50));
            window.BeginSelectionDrag(new Point(80, 50));
            window.PreviewSelectionDrag(new Point(90, 60));
            window.CancelSelectionDrag();
            TestAssert.Equal(new Point(50, 30), window.Annotations[1].Start);
            TestAssert.Equal<int?>(null, window.SelectAnnotationAt(new Point(150, 95)));
            TestAssert.False(window.DeleteAnnotationButton.IsEnabled);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorResizesShapesBlurAndArrowEndpoints() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));

        try
        {
            foreach (var tool in new[] { window.RectangleTool, window.EllipseTool, window.BlurTool })
            {
                tool.IsChecked = true;
                window.CommitAnnotation(new Point(10, 10), new Point(70, 60));
                window.SelectAnnotationAt(new Point(40, 30));
                window.BeginSelectionDrag(new Point(70, 60));
                window.CompleteSelectionDrag(new Point(100, 80));
                var annotation = window.Annotations[^1];
                TestAssert.Equal(new Rect(10, 10, 90, 70), new Rect(annotation.Start, annotation.End));
                window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.Equal(new Point(70, 60), window.Annotations[^1].End);
                window.SelectAnnotationAt(new Point(40, 30));
                window.DeleteSelectedAnnotation();
            }

            window.ArrowTool.IsChecked = true;
            window.CommitAnnotation(new Point(20, 20), new Point(100, 70));
            window.SelectAnnotationAt(new Point(60, 45));
            window.BeginSelectionDrag(new Point(100, 70));
            window.CompleteSelectionDrag(new Point(140, 90));
            TestAssert.Equal(new Point(140, 90), window.Annotations[^1].End);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(new Point(100, 70), window.Annotations[^1].End);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorResizeClicksPreserveSavedStateAndPointerOffset() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();

        foreach (var kind in new[] { ScreenshotAnnotationKind.Rectangle, ScreenshotAnnotationKind.Arrow })
        {
            var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));

            try
            {
                (kind == ScreenshotAnnotationKind.Rectangle ? window.RectangleTool : window.ArrowTool).IsChecked = true;
                window.CommitAnnotation(new Point(20, 20), new Point(100, 70));
                window.SavePng(directory.PathFor($"{kind}.png"));
                window.SelectAnnotationAt(new Point(60, 45));
                var original = TestAssert.Single(window.Annotations);

                foreach (var pointer in new[] { new Point(20, 20), new Point(22, 22), new Point(100, 70), new Point(102, 72) })
                {
                    TestAssert.True(window.BeginSelectionDrag(pointer));
                    window.CompleteSelectionDrag(pointer);
                    TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                    TestAssert.False(window.HasUnsavedChanges);
                    TestAssert.True(window.BeginSelectionDrag(pointer));
                    window.CompleteSelectionDrag(pointer + new Vector(0.25, 0.25));
                    TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                    TestAssert.False(window.HasUnsavedChanges);
                    TestAssert.True(window.BeginSelectionDrag(pointer));
                    window.PreviewSelectionDrag(pointer + new Vector(10, 10));
                    window.CompleteSelectionDrag(pointer);
                    TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                    TestAssert.False(window.HasUnsavedChanges);
                }

                window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.Empty(window.Annotations);
                TestAssert.False(window.UndoButton.IsEnabled);
                window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.False(window.HasUnsavedChanges);
                window.SelectAnnotationAt(new Point(60, 45));
                window.BeginSelectionDrag(new Point(102, 72));
                window.CompleteSelectionDrag(new Point(112, 82));
                var resized = TestAssert.Single(window.Annotations);
                TestAssert.Equal(new Point(20, 20), resized.Start);
                TestAssert.Equal(new Point(110, 80), resized.End);
                window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                TestAssert.False(window.HasUnsavedChanges);
            }
            finally
            {
                window.CloseAfterConfirmation();
            }
        }
    });

    internal static void EditorClearingExistingTextProtectsAndDeletesTheLabelWithUndo() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi),
            promptClose: () => ScreenshotCloseChoice.Cancel);

        try
        {
            window.BeginTextEntry(new Point(10, 10));
            window.AnnotationTextBox.Text = "Saved label";
            window.FinishTextEntry(commit: true);
            window.SavePng(directory.PathFor("label.png"));
            var original = TestAssert.Single(window.Annotations);

            foreach (var clearedText in new[] { string.Empty, " \n\t" })
            {
                window.SelectAnnotationAt(new Point(12, 12));
                window.BeginSelectedTextEdit();
                window.AnnotationTextBox.Text = clearedText;
                TestAssert.True(window.HasUnsavedChanges);
                TestAssert.False(window.ConfirmClose());
                TestAssert.Equal(clearedText, window.AnnotationTextBox.Text);
                TestAssert.Equal(Visibility.Visible, window.TextInputPanel.Visibility);
                TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                window.FinishTextEntry(commit: true);
                TestAssert.Empty(window.Annotations);
                TestAssert.True(window.HasUnsavedChanges);
                window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                TestAssert.False(window.HasUnsavedChanges);

                window.SelectAnnotationAt(new Point(12, 12));
                window.BeginSelectedTextEdit();
                window.AnnotationTextBox.Text = clearedText;
                window.FinishTextEntry(commit: false);
                TestAssert.Equal(original, TestAssert.Single(window.Annotations));
                TestAssert.False(window.HasUnsavedChanges);
            }

            window.BeginTextEntry(new Point(12, 12));
            window.AnnotationTextBox.Text = " \n\t";
            TestAssert.False(window.HasUnsavedChanges);
            window.FinishTextEntry(commit: true);
            TestAssert.Equal(original, TestAssert.Single(window.Annotations));
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorEditsSelectedTextStylesAndDeletionWithUndo() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));

        try
        {
            window.BeginTextEntry(new Point(10, 10));
            window.AnnotationTextBox.Text = "Original";
            window.FinishTextEntry(commit: true);
            TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(12, 12)));
            TestAssert.Equal(Visibility.Visible, window.EditTextButton.Visibility);
            window.BeginSelectedTextEdit();
            window.AnnotationTextBox.Text = "Changed";
            window.FinishTextEntry(commit: true);
            TestAssert.Equal("Changed", TestAssert.Single(window.Annotations).Text);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal("Original", TestAssert.Single(window.Annotations).Text);
            window.SelectAnnotationAt(new Point(12, 12));
            window.FontSizeComboBox.SelectedValue = 48d;
            TestAssert.Equal(48d, TestAssert.Single(window.Annotations).FontSize);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal(24d, TestAssert.Single(window.Annotations).FontSize);
            window.SelectAnnotationAt(new Point(12, 12));
            window.DeleteSelectedAnnotation();
            TestAssert.Empty(window.Annotations);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.Equal("Original", TestAssert.Single(window.Annotations).Text);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorSelectionOverlayStaysOutOfExportsAtBothZoomLevels() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new ScreenshotEditorWindow(CreateImage(PreviewImageWidth, PreviewImageHeight, HighDpi));

        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(100, 100), new Point(600, 400));
            var expected = Pixels(window.RenderedImage);
            window.Show();
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;

            foreach (var zoom in new[] { window.FitZoomButton, window.ActualSizeZoomButton })
            {
                zoom.IsChecked = true;
                window.UpdateLayout();
                TestAssert.Equal<int?>(0, window.SelectAnnotationAt(new Point(350, 250)));
                TestAssert.True(window.AnnotationCanvas.Children.Count > 0);
                TestAssert.SequenceEqual(expected, Pixels(window.RenderedImage));
                window.SavePng(directory.PathFor("selection.png"));
                using var stream = File.OpenRead(directory.PathFor("selection.png"));
                var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                TestAssert.Equal(PreviewImageWidth, decoded.Frames[0].PixelWidth);
                TestAssert.Equal(PreviewImageHeight, decoded.Frames[0].PixelHeight);
                TestAssert.SequenceEqual(expected, Pixels(new System.Windows.Media.Imaging.FormatConvertedBitmap(decoded.Frames[0], System.Windows.Media.PixelFormats.Pbgra32, null, 0)));
            }

            var previewDirectory = Environment.GetEnvironmentVariable("WINUTIL_NATIVE_PREVIEW_DIRECTORY");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                window.FitZoomButton.IsChecked = true;
                ExportEditorPreview(window, Path.Combine(previewDirectory, "screenshot-selection.png"));
            }
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    private static void CompleteTask(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            task.GetAwaiter().OnCompleted(() => dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }
}
