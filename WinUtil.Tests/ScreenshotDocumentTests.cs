using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static partial class ScreenshotDocumentTests
{
    private const int FixtureWidth = 64;
    private const int FixtureHeight = 48;
    private const int BytesPerPixel = 4;
    private const double SourceDpi = 192;
    private const double ExportDpi = 96;

    internal static void ConstructorDetachesTheSourceAndPreservesPixelDimensions() => RunOnStaThread(() =>
    {
        var original = new WriteableBitmap(CreateBitmap(FixtureWidth, FixtureHeight));
        var document = new ScreenshotDocument(original);
        var expectedPixels = ReadPixels(document.Render());
        var replacementPixels = ReadPixels(CreateBitmap(FixtureWidth, FixtureHeight, (_, _) => Colors.Blue));
        original.WritePixels(new Int32Rect(0, 0, FixtureWidth, FixtureHeight), replacementPixels, FixtureWidth * BytesPerPixel, 0);
        var rendered = document.Render();

        TestAssert.Equal(FixtureWidth, document.Width);
        TestAssert.Equal(FixtureHeight, document.Height);
        TestAssert.Equal(FixtureWidth, rendered.PixelWidth);
        TestAssert.Equal(FixtureHeight, rendered.PixelHeight);
        TestAssert.Equal(ExportDpi, rendered.DpiX);
        TestAssert.Equal(ExportDpi, rendered.DpiY);
        TestAssert.True(rendered.IsFrozen);
        TestAssert.SequenceEqual(expectedPixels, ReadPixels(rendered));
        TestAssert.False(document.CanUndo);
        TestAssert.False(document.CanRedo);
        TestAssert.False(document.HasAnnotations);
    });

    internal static void ConstructorRejectsNullUninitializedAndOversizedSources() => RunOnStaThread(() =>
    {
        ExpectException<ArgumentNullException>(() => new ScreenshotDocument(null!));
        ExpectException<InvalidOperationException>(() => new ScreenshotDocument(new BitmapImage()));
        ExpectException<ArgumentOutOfRangeException>(() => new ScreenshotDocument(CreateBitmap(ScreenshotDocument.MaximumSourceDimension + 1, 1)));
        ExpectException<ArgumentOutOfRangeException>(() => new ScreenshotDocument(CreateBitmap(1, ScreenshotDocument.MaximumSourceDimension + 1)));
    });

    internal static void ConstructorAcceptsTheMaximumDimensionWithABoundedPixelCount() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(ScreenshotDocument.MaximumSourceDimension, 1));

        TestAssert.Equal(ScreenshotDocument.MaximumSourceDimension, document.Width);
        TestAssert.Equal(1, document.Height);
        TestAssert.True(document.Render().IsFrozen);
    });

    internal static void AddRejectsInvalidAnnotationsWithoutChangingHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var valid = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        var invalidAnnotations = new[]
        {
            valid with { Kind = (ScreenshotAnnotationKind)int.MaxValue },
            valid with { Start = new Point(double.NaN, 0) },
            valid with { End = new Point(0, double.PositiveInfinity) },
            valid with { Start = new Point(double.NegativeInfinity, 0) },
            valid with { StrokeWidth = double.NaN },
            valid with { StrokeWidth = ScreenshotDocument.MinimumStrokeWidth - 1 },
            valid with { StrokeWidth = ScreenshotDocument.MaximumStrokeWidth + 1 },
            valid with { FontSize = double.PositiveInfinity },
            valid with { FontSize = ScreenshotDocument.MinimumFontSize - 1 },
            valid with { FontSize = ScreenshotDocument.MaximumFontSize + 1 },
            valid with { BlurRadius = double.NaN },
            valid with { BlurRadius = ScreenshotDocument.MinimumBlurRadius - 1 },
            valid with { BlurRadius = ScreenshotDocument.MaximumBlurRadius + 1 },
            valid with { Text = null! },
            valid with { Text = new string('a', ScreenshotDocument.MaximumTextLength + 1) },
            valid with { Kind = ScreenshotAnnotationKind.Text, Text = "" },
            valid with { Kind = ScreenshotAnnotationKind.Text, Text = " \n\t" },
            valid with { Kind = ScreenshotAnnotationKind.Text, Text = "text", Start = new Point(FixtureWidth, 0) },
            valid with { Kind = ScreenshotAnnotationKind.Text, Text = "text", Start = new Point(0, FixtureHeight) },
            valid with { Kind = ScreenshotAnnotationKind.Arrow, End = valid.Start },
            valid with { End = new Point(valid.Start.X, valid.End.Y) },
            valid with { Kind = ScreenshotAnnotationKind.Ellipse, End = new Point(valid.End.X, valid.Start.Y) },
            valid with { Kind = ScreenshotAnnotationKind.Blur, End = valid.Start },
            valid with { Start = new Point(-20, -20), End = new Point(-1, -1) },
            valid with { Start = new Point(FixtureWidth + 1, FixtureHeight + 1), End = new Point(FixtureWidth + 5, FixtureHeight + 5) }
        };

        ExpectException<ArgumentNullException>(() => document.Add(null!));

        foreach (var invalid in invalidAnnotations)
        {
            ExpectException<ArgumentException>(() => document.Add(invalid));
        }

        TestAssert.False(document.HasAnnotations);
        TestAssert.False(document.CanUndo);
        TestAssert.False(document.CanRedo);
    });

    internal static void AddAcceptsMaximumStylesAndTextLength() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with
        {
            StrokeWidth = ScreenshotDocument.MaximumStrokeWidth,
            FontSize = ScreenshotDocument.MaximumFontSize,
            BlurRadius = ScreenshotDocument.MaximumBlurRadius
        });
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Text) with
        {
            Text = new string('a', ScreenshotDocument.MaximumTextLength),
            FontSize = ScreenshotDocument.MaximumFontSize
        });
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with { BlurRadius = ScreenshotDocument.MaximumBlurRadius });

        TestAssert.True(document.HasAnnotations);
        TestAssert.Equal(FixtureWidth, document.Render().PixelWidth);
    });

    internal static void UndoAndRedoRestoreRenderedStatesAndHandleEmptyHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var original = ReadPixels(document.Render());
        document.Undo();
        document.Redo();
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle));
        var first = ReadPixels(document.Render());
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Arrow) with { Color = Colors.Blue });
        var second = ReadPixels(document.Render());

        document.Undo();
        TestAssert.SequenceEqual(first, ReadPixels(document.Render()));
        TestAssert.True(document.CanUndo);
        TestAssert.True(document.CanRedo);
        document.Undo();
        document.Undo();
        TestAssert.SequenceEqual(original, ReadPixels(document.Render()));
        TestAssert.False(document.HasAnnotations);
        TestAssert.False(document.CanUndo);
        TestAssert.True(document.CanRedo);
        document.Redo();
        TestAssert.SequenceEqual(first, ReadPixels(document.Render()));
        document.Redo();
        document.Redo();
        TestAssert.SequenceEqual(second, ReadPixels(document.Render()));
        TestAssert.True(document.HasAnnotations);
        TestAssert.True(document.CanUndo);
        TestAssert.False(document.CanRedo);
    });

    internal static void AddingAfterUndoDiscardsRedoHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle));
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Ellipse));
        document.Undo();
        TestAssert.True(document.CanRedo);
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Arrow));
        var expected = ReadPixels(document.Render());
        document.Redo();

        TestAssert.False(document.CanRedo);
        TestAssert.SequenceEqual(expected, ReadPixels(document.Render()));
    });

    internal static void RenderDrawsArrowsShapeOutlinesAndText() => RunOnStaThread(() =>
    {
        var rectangle = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        rectangle.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle));
        TestAssert.Equal(Colors.Red, PixelAt(rectangle.Render(), 20, 8));
        TestAssert.Equal(Colors.White, PixelAt(rectangle.Render(), 28, 24));

        var ellipse = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        ellipse.Add(CreateAnnotation(ScreenshotAnnotationKind.Ellipse));
        TestAssert.Equal(Colors.Red, PixelAt(ellipse.Render(), 28, 8));
        TestAssert.Equal(Colors.White, PixelAt(ellipse.Render(), 28, 24));

        var arrow = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        arrow.Add(CreateAnnotation(ScreenshotAnnotationKind.Arrow) with { Start = new Point(8, 24), End = new Point(48, 24) });
        TestAssert.Equal(Colors.Red, PixelAt(arrow.Render(), 20, 24));
        TestAssert.Equal(Colors.Red, PixelAt(arrow.Render(), 40, 24));
        TestAssert.Equal(Colors.White, PixelAt(arrow.Render(), 40, 4));

        var text = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var original = ReadPixels(text.Render());
        text.Add(CreateAnnotation(ScreenshotAnnotationKind.Text) with { Text = "Text", Start = new Point(8, 8) });
        var rendered = text.Render();
        TestAssert.False(original.SequenceEqual(ReadPixels(rendered)));
        TestAssert.Equal(Colors.White, PixelAt(rendered, 0, 0));
        TestAssert.True(rendered.IsFrozen);
    });

    internal static void RenderClipsReversedAndOutOfBoundsSelections() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, Checkerboard));
        var original = ReadPixels(document.Render());
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with { Start = new Point(12, 12), End = new Point(-100, -100) });
        var rendered = ReadPixels(document.Render());

        AssertOutsideRegionUnchanged(original, rendered, FixtureWidth, FixtureHeight, new Int32Rect(0, 0, 12, 12));
        TestAssert.False(original.SequenceEqual(rendered));
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with { Start = new Point(FixtureWidth + 10, FixtureHeight + 10), End = new Point(10, 10) });
        TestAssert.Equal(FixtureWidth, document.Render().PixelWidth);
    });

    internal static void BlurChangesEverySelectedPixelAndPreservesOutsidePixels() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, Checkerboard));
        var original = ReadPixels(document.Render());
        var selection = new Int32Rect(8, 6, 24, 18);
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with
        {
            Start = new Point(selection.X, selection.Y),
            End = new Point(selection.X + selection.Width, selection.Y + selection.Height),
            BlurRadius = 3
        });
        var rendered = ReadPixels(document.Render());

        AssertOutsideRegionUnchanged(original, rendered, FixtureWidth, FixtureHeight, selection);

        for (var y = selection.Y; y < selection.Y + selection.Height; y++)
        {
            for (var x = selection.X; x < selection.X + selection.Width; x++)
            {
                var offset = (y * FixtureWidth + x) * BytesPerPixel;
                TestAssert.True(rendered[offset] > 0 && rendered[offset] < byte.MaxValue);
                TestAssert.Equal(byte.MaxValue, rendered[offset + BytesPerPixel - 1]);
            }
        }
    });

    internal static void BlurFlattensEarlierAnnotationsAndKeepsLaterAnnotationsSharp() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var rectangle = CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with { Start = new Point(12, 8), End = new Point(32, 24), StrokeWidth = 6 };
        var blur = CreateAnnotation(ScreenshotAnnotationKind.Blur) with { Start = new Point(8, 4), End = new Point(40, 28), BlurRadius = 3 };
        document.Add(rectangle);
        TestAssert.Equal(Colors.Red, PixelAt(document.Render(), 20, 8));
        document.Add(blur);
        var blurredPixel = PixelAt(document.Render(), 20, 8);
        TestAssert.True(blurredPixel.G > 0);
        TestAssert.True(blurredPixel.G < byte.MaxValue);
        document.Add(rectangle with { Color = Colors.Blue, Start = new Point(20, 10), End = new Point(50, 26), StrokeWidth = 2 });
        TestAssert.Equal(Colors.Blue, PixelAt(document.Render(), 30, 10));

        var reversed = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        reversed.Add(blur);
        reversed.Add(rectangle);
        TestAssert.Equal(Colors.Red, PixelAt(reversed.Render(), 20, 8));
    });

    internal static void BlurPreservesUniformRegionsSinglePixelsAndOutsideAlpha() => RunOnStaThread(() =>
    {
        var uniform = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, (_, _) => Colors.CornflowerBlue));
        var uniformPixels = ReadPixels(uniform.Render());
        uniform.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur));
        TestAssert.SequenceEqual(uniformPixels, ReadPixels(uniform.Render()));

        var singlePixel = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, Checkerboard));
        var singlePixelOriginal = ReadPixels(singlePixel.Render());
        singlePixel.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with { Start = new Point(4, 4), End = new Point(5, 5), BlurRadius = ScreenshotDocument.MaximumBlurRadius });
        TestAssert.SequenceEqual(singlePixelOriginal, ReadPixels(singlePixel.Render()));

        var transparent = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, (x, y) => Color.FromArgb((byte)((x + y) % 2 == 0 ? 64 : 192), byte.MaxValue, 0, 0)));
        var alphaOriginal = ReadPixels(transparent.Render());
        var selection = new Int32Rect(8, 8, 16, 16);
        transparent.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with { Start = new Point(8, 8), End = new Point(24, 24) });
        AssertOutsideRegionUnchanged(alphaOriginal, ReadPixels(transparent.Render()), FixtureWidth, FixtureHeight, selection);
    });

    internal static void ReplaceAndRemoveAnnotationsPreserveUndoAndImmutableSnapshots() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var original = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        document.Add(original);
        var snapshot = document.Annotations;
        var edited = original with { Start = new Point(12, 12), Color = Colors.Blue };
        document.Replace(0, edited);
        TestAssert.Equal(original, snapshot[0]);
        TestAssert.Equal(edited, document.Annotations[0]);
        document.Undo();
        TestAssert.Equal(original, document.Annotations[0]);
        document.Replace(0, original);
        TestAssert.True(document.CanRedo);
        document.Redo();
        TestAssert.Equal(edited, document.Annotations[0]);
        document.Remove(0);
        TestAssert.False(document.HasAnnotations);
        document.Undo();
        TestAssert.Equal(edited, document.Annotations[0]);
        document.Redo();
        TestAssert.False(document.HasAnnotations);
        document.Undo();
        document.Replace(0, original);
        TestAssert.False(document.CanRedo);
    });

    internal static void AnnotationEditsRejectInvalidInputWithoutChangingHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var original = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        ExpectException<ArgumentOutOfRangeException>(() => document.Remove(0));
        ExpectException<ArgumentOutOfRangeException>(() => document.Replace(0, original));
        document.Add(original);

        foreach (var index in new[] { -1, 1, int.MaxValue })
        {
            ExpectException<ArgumentOutOfRangeException>(() => document.Remove(index));
            ExpectException<ArgumentOutOfRangeException>(() => document.Replace(index, original));
            ExpectException<ArgumentOutOfRangeException>(() => document.RenderReplacing(index, original));
        }

        ExpectException<ArgumentNullException>(() => document.Replace(0, null!));
        ExpectException<ArgumentNullException>(() => document.RenderReplacing(0, null!));

        foreach (var invalid in new[]
        {
            original with { End = original.Start },
            original with { Start = new Point(double.NaN, 0) },
            original with { StrokeWidth = ScreenshotDocument.MaximumStrokeWidth + 1 },
            original with { Text = null! },
            original with { Text = new string('a', ScreenshotDocument.MaximumTextLength + 1) }
        })
        {
            ExpectException<ArgumentException>(() => document.Replace(0, invalid));
            ExpectException<ArgumentException>(() => document.RenderReplacing(0, invalid));
        }

        TestAssert.Equal(original, document.Annotations[0]);
        document.Undo();
        TestAssert.False(document.HasAnnotations);
        TestAssert.False(document.CanUndo);
    });

    internal static void AnnotationPreviewDoesNotChangeDocumentOrHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var original = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        document.Add(original);
        var initialPixels = ReadPixels(document.Render());
        var preview = document.RenderReplacing(0, original with { Color = Colors.Blue });
        TestAssert.False(initialPixels.SequenceEqual(ReadPixels(preview)));
        TestAssert.SequenceEqual(initialPixels, ReadPixels(document.Render()));
        TestAssert.Equal(original, document.Annotations[0]);
        document.Undo();
        TestAssert.False(document.HasAnnotations);
        TestAssert.False(document.CanUndo);
    });

    internal static void AnnotationGeometrySupportsTextArrowsMovementAndResizing() => RunOnStaThread(() =>
    {
        var rectangle = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        TestAssert.True(ScreenshotAnnotationGeometry.HitTest(rectangle, new Point(20, 20), 0));
        TestAssert.False(ScreenshotAnnotationGeometry.HitTest(rectangle, new Point(0, 0), 0));
        var moved = ScreenshotAnnotationGeometry.Move(rectangle, new Vector(double.MaxValue, double.MaxValue), FixtureWidth, FixtureHeight);
        TestAssert.Equal(new Point(FixtureWidth, FixtureHeight), moved.End);
        TestAssert.Equal(new Rect(rectangle.Start, rectangle.End).Size, new Rect(moved.Start, moved.End).Size);
        var resized = ScreenshotAnnotationGeometry.Resize(rectangle, ScreenshotResizeHandle.TopLeft, new Point(0, 0), FixtureWidth, FixtureHeight);
        TestAssert.Equal(new Rect(0, 0, 48, 40), ScreenshotAnnotationGeometry.GetBounds(resized));
        TestAssert.Equal(rectangle, ScreenshotAnnotationGeometry.Resize(rectangle, ScreenshotResizeHandle.TopLeft, rectangle.Start, FixtureWidth, FixtureHeight));
        TestAssert.Equal(rectangle, ScreenshotAnnotationGeometry.Resize(rectangle, ScreenshotResizeHandle.TopLeft, rectangle.End, FixtureWidth, FixtureHeight));

        var arrow = rectangle with { Kind = ScreenshotAnnotationKind.Arrow, Start = new Point(8, 8), End = new Point(32, 8) };
        TestAssert.True(ScreenshotAnnotationGeometry.HitTest(arrow, new Point(20, 8), 0));
        TestAssert.False(ScreenshotAnnotationGeometry.HitTest(arrow, new Point(20, 24), 0));
        TestAssert.Equal(new Point(40, 20), ScreenshotAnnotationGeometry.Resize(arrow, ScreenshotResizeHandle.End, new Point(40, 20), FixtureWidth, FixtureHeight).End);
        TestAssert.Equal(arrow, ScreenshotAnnotationGeometry.Resize(arrow, ScreenshotResizeHandle.Start, arrow.End, FixtureWidth, FixtureHeight));

        var text = rectangle with { Kind = ScreenshotAnnotationKind.Text, End = rectangle.Start, Text = "Hello\nWorld" };
        var textBounds = ScreenshotAnnotationGeometry.GetBounds(text);
        TestAssert.True(textBounds.Height > text.FontSize);
        TestAssert.True(ScreenshotAnnotationGeometry.HitTest(text, textBounds.TopLeft, 0));
        TestAssert.Equal(text, ScreenshotAnnotationGeometry.Move(text, new Vector(), FixtureWidth, FixtureHeight));
        var movedText = ScreenshotAnnotationGeometry.Move(text, new Vector(-double.MaxValue, -double.MaxValue), FixtureWidth, FixtureHeight);
        TestAssert.Equal(new Point(), movedText.Start);
        TestAssert.Equal(movedText.Start, movedText.End);
        ExpectException<ArgumentNullException>(() => ScreenshotAnnotationGeometry.GetBounds(null!));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotAnnotationGeometry.HitTest(text, new Point(double.NaN, 0), 0));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotAnnotationGeometry.HitTest(text, new Point(), double.PositiveInfinity));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotAnnotationGeometry.Move(text, new Vector(double.NaN, 0), FixtureWidth, FixtureHeight));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotAnnotationGeometry.Move(text, new Vector(), 0, FixtureHeight));
        ExpectException<ArgumentException>(() => ScreenshotAnnotationGeometry.Resize(text, ScreenshotResizeHandle.BottomRight, new Point(), FixtureWidth, FixtureHeight));
    });

    private static ScreenshotAnnotation CreateAnnotation(ScreenshotAnnotationKind kind) => new(kind, new Point(8, 8), new Point(48, 40), Colors.Red);

    private static Color Checkerboard(int x, int y) => (x + y) % 2 == 0 ? Colors.Black : Colors.White;

    private static BitmapSource CreateBitmap(int width, int height, Func<int, int, Color>? getColor = null)
    {
        var stride = checked(width * BytesPerPixel);
        var pixels = new byte[checked(stride * height)];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = getColor?.Invoke(x, y) ?? Colors.White;
                var offset = y * stride + x * BytesPerPixel;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = color.A;
            }
        }

        return BitmapSource.Create(width, height, SourceDpi, SourceDpi, PixelFormats.Bgra32, null, pixels, stride);
    }

    private static byte[] ReadPixels(BitmapSource bitmap)
    {
        var stride = checked(bitmap.PixelWidth * BytesPerPixel);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        var converted = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixels = ReadPixels(bitmap);
        var offset = (y * bitmap.PixelWidth + x) * BytesPerPixel;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static void AssertOutsideRegionUnchanged(byte[] original, byte[] actual, int width, int height, Int32Rect selection)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= selection.X && x < selection.X + selection.Width && y >= selection.Y && y < selection.Y + selection.Height)
                {
                    continue;
                }

                var offset = (y * width + x) * BytesPerPixel;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    TestAssert.Equal(original[offset + channel], actual[offset + channel]);
                }
            }
        }
    }

    private static void ExpectException<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
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
