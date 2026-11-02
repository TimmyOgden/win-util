using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Views;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    private const int TestImageWidth = 160;
    private const int TestImageHeight = 100;
    private const int BytesPerPixel = 4;
    private const double StandardDpi = 96;
    private const double HighDpi = 192;
    private const int PreviewImageWidth = 960;
    private const int PreviewImageHeight = 540;

    internal static void EditorPreservesPixelResolutionAndSupportsUndoRedo() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, HighDpi));

        try
        {
            TestAssert.Equal((double)TestImageWidth, window.ImageSurface.Width);
            TestAssert.Equal((double)TestImageHeight, window.ImageSurface.Height);
            TestAssert.Equal(TestImageWidth, window.RenderedImage.PixelWidth);
            TestAssert.Equal(TestImageHeight, window.RenderedImage.PixelHeight);
            TestAssert.Equal(StandardDpi, window.RenderedImage.DpiX);
            TestAssert.False(window.UndoButton.IsEnabled);
            TestAssert.False(window.RedoButton.IsEnabled);
            var initial = Pixels(window.RenderedImage);
            window.CommitAnnotation(new Point(12, 12), new Point(120, 72));
            var edited = Pixels(window.RenderedImage);
            TestAssert.False(initial.SequenceEqual(edited));
            TestAssert.True(window.UndoButton.IsEnabled);
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));
            TestAssert.True(window.RedoButton.IsEnabled);
            window.RedoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(edited, Pixels(window.RenderedImage));
            TestAssert.True(window.RenderedImage.IsFrozen);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorSupportsShapesTextAndBlurWithoutEmptyAnnotations() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));

        try
        {
            window.CommitAnnotation(new Point(8, 8), new Point(8, 8));
            TestAssert.False(window.UndoButton.IsEnabled);
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(8, 8), new Point(40, 8));
            TestAssert.False(window.UndoButton.IsEnabled);

            foreach (var tool in new[] { window.RectangleTool, window.EllipseTool, window.BlurTool })
            {
                tool.IsChecked = true;
                var before = Pixels(window.RenderedImage);
                window.CommitAnnotation(new Point(12, 12), new Point(100, 80));
                TestAssert.True(window.UndoButton.IsEnabled);
                TestAssert.False(before.SequenceEqual(Pixels(window.RenderedImage)));
            }

            window.TextTool.IsChecked = true;
            TestAssert.Equal(Visibility.Collapsed, window.TextInputPanel.Visibility);
            TestAssert.Equal(Visibility.Visible, window.FontSizeOptionPanel.Visibility);
            var beforeText = Pixels(window.RenderedImage);
            window.AnnotationTextBox.Text = "  ";
            window.CommitAnnotation(new Point(16, 16), new Point(16, 16));
            TestAssert.SequenceEqual(beforeText, Pixels(window.RenderedImage));
            TestAssert.True(window.StatusText.Text.Contains("Enter text", StringComparison.Ordinal));
            window.AnnotationTextBox.Text = "Hello\nWorld";
            window.CommitAnnotation(new Point(16, 16), new Point(16, 16));
            TestAssert.False(beforeText.SequenceEqual(Pixels(window.RenderedImage)));
            window.ArrowTool.IsChecked = true;
            TestAssert.Equal(Visibility.Collapsed, window.TextInputPanel.Visibility);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorPlacesTextOnCanvasAndCommitsBeforeExport() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi));

        try
        {
            window.TextTool.IsChecked = true;
            var initial = Pixels(window.RenderedImage);
            window.BeginTextEntry(new Point(16, 16));
            TestAssert.Equal(Visibility.Visible, window.TextInputPanel.Visibility);
            TestAssert.Equal(12d, Canvas.GetLeft(window.TextInputPanel));
            TestAssert.Equal(window.Options.FontSize, window.AnnotationTextBox.FontSize);
            window.AnnotationTextBox.Text = "Draft";
            window.FinishTextEntry(commit: false);
            TestAssert.Equal(Visibility.Collapsed, window.TextInputPanel.Visibility);
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));
            TestAssert.False(window.UndoButton.IsEnabled);

            window.BeginTextEntry(new Point(16, 16));
            window.AnnotationTextBox.Text = " ";
            window.FinishTextEntry(commit: true);
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));

            window.BeginTextEntry(new Point(16, 16));
            window.AnnotationTextBox.Text = "Saved text that stays on one explicit line until a newline\nSecond line";
            window.SavePng(directory.PathFor("pending-text.png"));
            TestAssert.Equal(Visibility.Collapsed, window.TextInputPanel.Visibility);
            TestAssert.False(initial.SequenceEqual(Pixels(window.RenderedImage)));
            var exported = Pixels(window.RenderedImage);
            window.FinishTextEntry(commit: true);
            TestAssert.SequenceEqual(exported, Pixels(window.RenderedImage));
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));

            window.BeginTextEntry(new Point(16, 16));
            window.AnnotationTextBox.Text = "Tool switch";
            window.ArrowTool.IsChecked = true;
            TestAssert.Equal(Visibility.Collapsed, window.TextInputPanel.Visibility);
            TestAssert.False(initial.SequenceEqual(Pixels(window.RenderedImage)));
            window.UndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));

            window.TextTool.IsChecked = true;
            window.BeginTextEntry(new Point(double.MaxValue, double.MaxValue));
            TestAssert.Equal(TestImageWidth - 5d, Canvas.GetLeft(window.TextInputPanel));
            TestAssert.Equal(TestImageHeight - 5d, Canvas.GetTop(window.TextInputPanel));
            window.FinishTextEntry(commit: false);

            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var rejected = false;

                try
                {
                    window.BeginTextEntry(new Point(invalid, 0));
                }
                catch (ArgumentOutOfRangeException)
                {
                    rejected = true;
                }

                TestAssert.True(rejected);
            }

            window.ColorComboBox.SelectedValue = "#FFFFFFFF";
            window.BeginTextEntry(new Point(16, 16));
            TestAssert.Equal((System.Windows.Media.Brush)window.FindResource("SurfaceBrush"), window.TextInputPanel.Background);
            TestAssert.Equal(TextWrapping.NoWrap, window.AnnotationTextBox.TextWrapping);
            window.FinishTextEntry(commit: false);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorShowsContextualOptionsAndChangesZoomWithoutChangingPixels() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, HighDpi));

        try
        {
            var initial = Pixels(window.RenderedImage);
            TestAssert.Equal(Visibility.Visible, window.ColorOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Visible, window.StrokeOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.FontSizeOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.BlurOptionPanel.Visibility);
            window.TextTool.IsChecked = true;
            TestAssert.Equal(Visibility.Visible, window.ColorOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.StrokeOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Visible, window.FontSizeOptionPanel.Visibility);
            window.BlurTool.IsChecked = true;
            TestAssert.Equal(Visibility.Collapsed, window.ColorOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.StrokeOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Collapsed, window.FontSizeOptionPanel.Visibility);
            TestAssert.Equal(Visibility.Visible, window.BlurOptionPanel.Visibility);
            window.ActualSizeZoomButton.IsChecked = true;
            TestAssert.Equal(Stretch.None, window.ImageViewbox.Stretch);
            TestAssert.Equal(ScrollBarVisibility.Auto, window.ImageScrollViewer.HorizontalScrollBarVisibility);
            window.FitZoomButton.IsChecked = true;
            TestAssert.Equal(Stretch.Uniform, window.ImageViewbox.Stretch);
            TestAssert.Equal(ScrollBarVisibility.Disabled, window.ImageScrollViewer.HorizontalScrollBarVisibility);
            TestAssert.SequenceEqual(initial, Pixels(window.RenderedImage));
            TestAssert.Equal(TestImageWidth, window.RenderedImage.PixelWidth);
            TestAssert.Equal(TestImageHeight, window.RenderedImage.PixelHeight);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorUsesAndUpdatesSavedOptions() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var options = new ScreenshotEditorOptions
        {
            Color = "#FF123ABC",
            StrokeWidth = 17,
            FontSize = 37,
            BlurRadius = 19
        };
        var window = new ScreenshotEditorWindow(CreateImage(TestImageWidth, TestImageHeight, StandardDpi), options);

        try
        {
            TestAssert.Equal(options, window.Options);
            TestAssert.Equal(options, window.InitialOptions);
            TestAssert.Equal(options.Color, (string)window.ColorComboBox.SelectedValue);
            TestAssert.Equal(options.StrokeWidth, (double)window.StrokeComboBox.SelectedValue);
            TestAssert.Equal(options.FontSize, (double)window.FontSizeComboBox.SelectedValue);
            TestAssert.Equal(options.BlurRadius, (double)window.BlurComboBox.SelectedValue);
            window.StrokeComboBox.SelectedValue = 8d;
            TestAssert.Equal(8d, window.Options.StrokeWidth);
            window.FontSizeComboBox.SelectedValue = 48d;
            TestAssert.Equal(48d, window.Options.FontSize);
            window.BlurComboBox.SelectedValue = 32d;
            TestAssert.Equal(32d, window.Options.BlurRadius);
            window.ColorComboBox.SelectedValue = ScreenshotEditorOptions.DefaultColor;
            TestAssert.Equal(ScreenshotEditorOptions.DefaultColor, window.Options.Color);
            TestAssert.Equal(options, window.InitialOptions);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    internal static void EditorOptionsNormalizeInvalidValuesAndClampLimits()
    {
        foreach (var color in new string?[] { null, "", "Red", "#123", "#FF12345G", " #FFFFFF" })
        {
            TestAssert.Equal(ScreenshotEditorOptions.DefaultColor, new ScreenshotEditorOptions { Color = color! }.Normalize().Color);
        }

        TestAssert.Equal("#ABCDEF", new ScreenshotEditorOptions { Color = "#abcdef" }.Normalize().Color);

        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var normalized = new ScreenshotEditorOptions { StrokeWidth = value, FontSize = value, BlurRadius = value }.Normalize();
            TestAssert.Equal(new ScreenshotEditorOptions(), normalized);
        }

        var minimum = new ScreenshotEditorOptions { StrokeWidth = 0, FontSize = 0, BlurRadius = 0 }.Normalize();
        TestAssert.Equal(ScreenshotEditorOptions.MinimumStrokeWidth, minimum.StrokeWidth);
        TestAssert.Equal(ScreenshotEditorOptions.MinimumFontSize, minimum.FontSize);
        TestAssert.Equal(ScreenshotEditorOptions.MinimumBlurRadius, minimum.BlurRadius);
        var maximum = new ScreenshotEditorOptions { StrokeWidth = double.MaxValue, FontSize = double.MaxValue, BlurRadius = double.MaxValue }.Normalize();
        TestAssert.Equal(ScreenshotEditorOptions.MaximumStrokeWidth, maximum.StrokeWidth);
        TestAssert.Equal(ScreenshotEditorOptions.MaximumFontSize, maximum.FontSize);
        TestAssert.Equal(ScreenshotEditorOptions.MaximumBlurRadius, maximum.BlurRadius);
        TestAssert.Equal(minimum, minimum.Normalize());
        TestAssert.Equal(maximum, maximum.Normalize());
    }

    internal static void EditorOptionsMergeOnlyFieldsChangedByThisEditor()
    {
        var initial = new ScreenshotEditorOptions();
        var savedByFirstEditor = initial with { StrokeWidth = 8, FontSize = 48, BlurRadius = 32 };
        TestAssert.Equal(savedByFirstEditor, initial.MergeChanges(initial, savedByFirstEditor));

        var colorChangedBySecondEditor = initial with { Color = "#FF52A8FF" };
        TestAssert.Equal(savedByFirstEditor with { Color = colorChangedBySecondEditor.Color },
            colorChangedBySecondEditor.MergeChanges(initial, savedByFirstEditor));

        var strokeChangedBySecondEditor = initial with { StrokeWidth = 12 };
        TestAssert.Equal(savedByFirstEditor with { StrokeWidth = strokeChangedBySecondEditor.StrokeWidth },
            strokeChangedBySecondEditor.MergeChanges(initial, savedByFirstEditor));

        var fontAndBlurChangedBySecondEditor = initial with { FontSize = 32, BlurRadius = 20 };
        TestAssert.Equal(savedByFirstEditor with { FontSize = 32, BlurRadius = 20 },
            fontAndBlurChangedBySecondEditor.MergeChanges(initial, savedByFirstEditor));

        var invalidChanges = initial with { Color = "invalid", StrokeWidth = double.MaxValue, BlurRadius = double.NaN };
        var normalized = invalidChanges.MergeChanges(initial, savedByFirstEditor);
        TestAssert.Equal(ScreenshotEditorOptions.DefaultColor, normalized.Color);
        TestAssert.Equal(ScreenshotEditorOptions.MaximumStrokeWidth, normalized.StrokeWidth);
        TestAssert.Equal(ScreenshotEditorOptions.DefaultBlurRadius, normalized.BlurRadius);
        TestAssert.Equal(savedByFirstEditor.FontSize, normalized.FontSize);
    }

    internal static void EditorOptionsMergeRejectsNullInputs()
    {
        var options = new ScreenshotEditorOptions();

        try
        {
            options.MergeChanges(null!, options);
            throw new InvalidOperationException("Expected the initial options to be required.");
        }
        catch (ArgumentNullException exception)
        {
            TestAssert.Equal("initial", exception.ParamName);
        }

        try
        {
            options.MergeChanges(options, null!);
            throw new InvalidOperationException("Expected the current options to be required.");
        }
        catch (ArgumentNullException exception)
        {
            TestAssert.Equal("current", exception.ParamName);
        }
    }

    internal static void EditorRendersSyntheticPreviewWhenRequested() => RunOnStaThread(() =>
    {
        var previewPath = Environment.GetEnvironmentVariable("WINUTIL_SCREENSHOT_EDITOR_PREVIEW_PATH");

        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return;
        }

        EnsureApplicationResources();
        var window = new ScreenshotEditorWindow(CreateSyntheticScreenshot());

        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(42, 82), new Point(610, 158));
            window.ArrowTool.IsChecked = true;
            window.CommitAnnotation(new Point(770, 220), new Point(615, 128));
            window.TextTool.IsChecked = true;
            window.AnnotationTextBox.Text = "Review this section";
            window.CommitAnnotation(new Point(660, 224), new Point(660, 224));
            window.BlurTool.IsChecked = true;
            window.BlurComboBox.SelectedValue = 32d;
            window.CommitAnnotation(new Point(190, 346), new Point(544, 378));
            window.Show();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(previewPath))!);
            ExportEditorPreview(window, previewPath);
            var previewDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(previewPath))!;
            window.TextTool.IsChecked = true;
            window.ColorComboBox.SelectedValue = "#FFFFFFFF";
            window.BeginTextEntry(new Point(260, 200));
            window.AnnotationTextBox.Text = "Type directly on the screenshot";
            ExportEditorPreview(window, System.IO.Path.Combine(previewDirectory, "screenshot-editor-text.png"));
            window.FinishTextEntry(commit: false);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.ArrowTool.IsChecked = true;
            ExportEditorPreview(window, System.IO.Path.Combine(previewDirectory, "screenshot-editor-compact.png"));
            window.ActualSizeZoomButton.IsChecked = true;
            ExportEditorPreview(window, System.IO.Path.Combine(previewDirectory, "screenshot-editor-actual-size.png"));
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    private static void ExportEditorPreview(ScreenshotEditorWindow window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var content = (FrameworkElement)window.Content;
        var rendered = new RenderTargetBitmap(
            (int)Math.Ceiling((content.ActualWidth + content.Margin.Left + content.Margin.Right) * dpi.DpiScaleX),
            (int)Math.Ceiling((content.ActualHeight + content.Margin.Top + content.Margin.Bottom) * dpi.DpiScaleY),
            StandardDpi * dpi.DpiScaleX,
            StandardDpi * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        rendered.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal static void EditorExportsFlattenedPngAtOriginalResolution() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        using var directory = new TemporaryDirectory();
        var image = CreateImage(TestImageWidth, TestImageHeight, HighDpi);
        var window = new ScreenshotEditorWindow(image);

        try
        {
            window.RectangleTool.IsChecked = true;
            window.CommitAnnotation(new Point(12, 12), new Point(120, 80));
            window.TextTool.IsChecked = true;
            window.AnnotationTextBox.Text = "Review";
            window.CommitAnnotation(new Point(18, 20), new Point(18, 20));
            window.BlurTool.IsChecked = true;
            window.CommitAnnotation(new Point(40, 40), new Point(100, 72));
            var path = directory.PathFor("edited.png");
            window.SavePng(path);

            using var stream = File.OpenRead(path);
            var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            TestAssert.Equal(1, decoded.Frames.Count);
            TestAssert.Equal(TestImageWidth, decoded.Frames[0].PixelWidth);
            TestAssert.Equal(TestImageHeight, decoded.Frames[0].PixelHeight);
            var normalized = new FormatConvertedBitmap(decoded.Frames[0], PixelFormats.Pbgra32, null, 0);
            var expectedPixels = Pixels(window.RenderedImage);
            var actualPixels = Pixels(normalized);
            if (!expectedPixels.SequenceEqual(actualPixels))
            {
                const int alphaChannelOffset = BytesPerPixel - 1;
                var mismatch = Enumerable.Range(0, expectedPixels.Length).First(index => expectedPixels[index] != actualPixels[index]);
                var pixelIndex = mismatch / BytesPerPixel;
                var pixelOffset = pixelIndex * BytesPerPixel;
                var nonopaquePixels = Enumerable.Range(0, expectedPixels.Length / BytesPerPixel)
                    .Count(index => expectedPixels[index * BytesPerPixel + alphaChannelOffset] != byte.MaxValue);
                var expectedBgra = string.Join(", ", expectedPixels.Skip(pixelOffset).Take(BytesPerPixel));
                var actualBgra = string.Join(", ", actualPixels.Skip(pixelOffset).Take(BytesPerPixel));
                throw new InvalidOperationException($"PNG differs at ({pixelIndex % window.RenderedImage.PixelWidth}, {pixelIndex / window.RenderedImage.PixelWidth}): expected BGRA [{expectedBgra}], actual BGRA [{actualBgra}]; rendered pixels with alpha != {byte.MaxValue}: {nonopaquePixels}.");
            }
            TestAssert.False(Pixels(image).SequenceEqual(Pixels(normalized)));

            foreach (var invalidPath in new string?[] { null, "", " " })
            {
                var rejected = false;

                try
                {
                    window.SavePng(invalidPath!);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                TestAssert.True(rejected);
            }
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    private static BitmapSource CreateImage(int width, int height, double dpi)
    {
        var pixels = new byte[width * height * BytesPerPixel];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * BytesPerPixel;
                var value = (byte)((x + y) % 2 == 0 ? 32 : 224);
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        var image = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * BytesPerPixel);
        image.Freeze();
        return image;
    }

    private static BitmapSource CreateSyntheticScreenshot()
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 247, 251)), null, new Rect(0, 0, PreviewImageWidth, PreviewImageHeight));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 36, 55)), null, new Rect(0, 0, PreviewImageWidth, 56));
            DrawText(context, "PROJECT ATLAS  /  Release review", 24, 17, 18, Colors.White);
            DrawText(context, "Ready for the next release", 56, 98, 28, Color.FromRgb(30, 43, 62));
            DrawText(context, "Final checks before publishing the desktop update.", 56, 172, 18, Color.FromRgb(80, 94, 115));
            context.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(215, 222, 233)), 1), new Rect(56, 254, 848, 224), 8, 8);
            DrawText(context, "Release owner", 80, 280, 15, Color.FromRgb(80, 94, 115));
            DrawText(context, "Alex Morgan", 192, 280, 18, Color.FromRgb(30, 43, 62));
            DrawText(context, "Email", 80, 352, 15, Color.FromRgb(80, 94, 115));
            DrawText(context, "alex.morgan@example.test", 192, 352, 18, Color.FromRgb(30, 43, 62));
            DrawText(context, "Status", 80, 424, 15, Color.FromRgb(80, 94, 115));
            DrawText(context, "Ready for review", 192, 424, 18, Color.FromRgb(30, 112, 77));
        }

        var image = new RenderTargetBitmap(PreviewImageWidth, PreviewImageHeight, StandardDpi, StandardDpi, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double size, Color color) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1), new Point(x, y));

    private static byte[] Pixels(BitmapSource source)
    {
        var pixels = new byte[source.PixelWidth * source.PixelHeight * BytesPerPixel];
        source.CopyPixels(pixels, source.PixelWidth * BytesPerPixel, 0);
        return pixels;
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
