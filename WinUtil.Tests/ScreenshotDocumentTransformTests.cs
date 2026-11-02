using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static partial class ScreenshotDocumentTests
{
    internal static void QuarterTurnsPreservePixelsAndReturnToCanonicalState() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, CoordinateColor));
        var initial = document.State;
        var original = document.Render();
        TestAssert.Equal(FixtureWidth, document.SourceWidth);
        TestAssert.Equal(FixtureHeight, document.SourceHeight);
        TestAssert.True(document.RotateClockwise());
        TestAssert.Equal(FixtureHeight, document.Width);
        TestAssert.Equal(FixtureWidth, document.Height);
        var clockwise = document.Render();
        for (var y = 0; y < FixtureHeight; y++)
        {
            for (var x = 0; x < FixtureWidth; x++)
            {
                TestAssert.Equal(PixelAt(original, x, y), PixelAt(clockwise, FixtureHeight - y - 1, x));
            }
        }
        document.RotateClockwise();
        document.RotateClockwise();
        document.RotateClockwise();
        TestAssert.True(document.HasSameContent(initial));
        TestAssert.True(document.SourceToOutput.IsIdentity);
        TestAssert.True(document.OutputToSource.IsIdentity);
        TestAssert.SequenceEqual(ReadPixels(original), ReadPixels(document.Render()));

        document.RotateCounterclockwise();
        var counterclockwise = document.Render();
        for (var y = 0; y < FixtureHeight; y++)
        {
            for (var x = 0; x < FixtureWidth; x++)
            {
                TestAssert.Equal(PixelAt(original, x, y), PixelAt(counterclockwise, y, FixtureWidth - x - 1));
            }
        }
        document.RotateClockwise();
        TestAssert.True(document.HasSameContent(initial));
    });

    internal static void CropUsesRotatedOutputCoordinatesAndPreservesEditableGeometry() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, CoordinateColor));
        document.RotateClockwise();
        var rotated = document.Render();
        var crop = new Int32Rect(3, 4, 12, 9);
        TestAssert.True(document.Crop(crop));
        var cropped = document.Render();
        TestAssert.Equal(crop.Width, document.Width);
        TestAssert.Equal(crop.Height, document.Height);
        for (var y = 0; y < crop.Height; y++)
        {
            for (var x = 0; x < crop.Width; x++)
            {
                TestAssert.Equal(PixelAt(rotated, crop.X + x, crop.Y + y), PixelAt(cropped, x, y));
            }
        }

        var editable = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var rectangle = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        editable.Add(rectangle);
        var beforeCrop = editable.State;
        editable.Crop(new Int32Rect(20, 15, 20, 20));
        TestAssert.Equal(rectangle, editable.Annotations[0]);
        TestAssert.Equal(new Rect(0, 0, FixtureWidth, FixtureHeight), editable.GetAnnotationLocalBounds(rectangle));
        editable.Replace(0, rectangle with { Color = Colors.Blue });
        TestAssert.Equal(rectangle.Start, editable.Annotations[0].Start);
        TestAssert.Equal(rectangle.End, editable.Annotations[0].End);
        TestAssert.Equal(rectangle, beforeCrop.Annotations[0]);
        editable.Undo();
        editable.Undo();
        TestAssert.True(editable.HasSameContent(beforeCrop));

        var isolated = new ScreenshotDocument(CreateBitmap(4, 2, (x, _) => x == 0 ? Colors.Red : x == 3 ? Colors.Green : Colors.Blue));
        isolated.Crop(new Int32Rect(1, 0, 2, 2));
        isolated.Resize(20, 20);
        var expected = new ScreenshotDocument(CreateBitmap(20, 20, (_, _) => Colors.Blue));
        TestAssert.SequenceEqual(ReadPixels(expected.Render()), ReadPixels(isolated.Render()));
    });

    internal static void ResizePreservesAspectAlphaDpiAndOriginalSource() => RunOnStaThread(() =>
    {
        var color = Color.FromArgb(128, 80, 120, 160);
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, (_, _) => color));
        var original = document.Render();
        TestAssert.True(document.Resize(FixtureWidth * 2, FixtureHeight * 2));
        var resized = document.Render();
        TestAssert.Equal(FixtureWidth * 2, resized.PixelWidth);
        TestAssert.Equal(FixtureHeight * 2, resized.PixelHeight);
        TestAssert.Equal(ExportDpi, resized.DpiX);
        TestAssert.Equal(ExportDpi, resized.DpiY);
        TestAssert.Equal(PixelAt(original, 1, 1), PixelAt(resized, FixtureWidth, FixtureHeight));
        TestAssert.Equal(FixtureWidth, document.SourceWidth);
        TestAssert.Equal(FixtureHeight, document.SourceHeight);
        TestAssert.True(resized.IsFrozen);
        TestAssert.True(document.Resize(7, 5));
        TestAssert.Equal(7, document.Width);
        TestAssert.Equal(5, document.Height);
        document.Undo();
        document.Undo();
        TestAssert.SequenceEqual(ReadPixels(original), ReadPixels(document.Render()));
    });

    internal static void TransformHistoryIncludesAnnotationsDimensionsAndSavedSnapshots() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var states = new List<ScreenshotDocumentState> { document.State };
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle));
        states.Add(document.State);
        document.Crop(new Int32Rect(4, 4, 40, 30));
        states.Add(document.State);
        document.RotateClockwise();
        states.Add(document.State);
        document.Resize(15, 20);
        states.Add(document.State);
        var exported = document.State;
        TestAssert.True(document.HasSameContent(exported with { Annotations = exported.Annotations.ToArray().ToImmutableArray() }));
        for (var index = states.Count - 2; index >= 0; index--)
        {
            document.Undo();
            TestAssert.True(document.HasSameContent(states[index]));
            TestAssert.False(document.HasSameContent(exported));
        }
        TestAssert.False(document.CanUndo);
        for (var index = 1; index < states.Count; index++)
        {
            document.Redo();
            TestAssert.True(document.HasSameContent(states[index]));
        }
        TestAssert.True(document.HasSameContent(exported));
        TestAssert.False(document.CanRedo);
        document.Undo();
        document.RotateCounterclockwise();
        TestAssert.False(document.CanRedo);
        TestAssert.False(document.HasSameContent(exported));
    });

    internal static void InvalidAndNoOpTransformsPreserveHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var initial = document.State;
        document.RotateClockwise();
        document.Undo();
        foreach (var crop in new[]
        {
            new Int32Rect(), new Int32Rect(-1, 0, 1, 1), new Int32Rect(0, -1, 1, 1),
            new Int32Rect(FixtureWidth, 0, 1, 1), new Int32Rect(0, FixtureHeight, 1, 1),
            new Int32Rect(1, 1, int.MaxValue, int.MaxValue)
        })
        {
            ExpectException<ArgumentException>(() => document.Crop(crop));
        }
        foreach (var (width, height) in new[]
        {
            (0, 1), (1, 0), (-1, 1), (1, -1), (int.MaxValue, int.MaxValue),
            (ScreenshotDocument.MaximumSourceDimension + 1, 1), (1, ScreenshotDocument.MaximumSourceDimension + 1),
            (10, 10), (ScreenshotDocument.MaximumSourceDimension, ScreenshotDocument.MaximumSourceDimension)
        })
        {
            ExpectException<ArgumentException>(() => document.Resize(width, height));
        }
        TestAssert.False(document.Crop(new Int32Rect(0, 0, FixtureWidth, FixtureHeight)));
        TestAssert.False(document.Resize(FixtureWidth, FixtureHeight));
        TestAssert.True(document.HasSameContent(initial));
        TestAssert.False(document.CanUndo);
        TestAssert.True(document.CanRedo);
        ExpectException<ArgumentNullException>(() => document.HasSameContent(null!));
        ExpectException<ArgumentNullException>(() => document.GetAnnotationToOutput(null!));
        ExpectException<ArgumentNullException>(() => document.GetAnnotationLocalBounds(null!));

        var bounded = new ScreenshotDocument(CreateBitmap(1, 1));
        var largestSquare = (int)Math.Sqrt(ScreenshotDocument.MaximumPixelCount);
        TestAssert.True(bounded.Resize(largestSquare, largestSquare));
        ExpectException<ArgumentOutOfRangeException>(() => bounded.Resize(largestSquare + 1, largestSquare + 1));
        TestAssert.Equal(largestSquare, bounded.Width);
    });

    internal static void AnnotationCreationBasisKeepsNewTextAndStylesInOutputCoordinates() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        document.RotateClockwise();
        document.Resize(FixtureHeight * 2, FixtureWidth * 2);
        var rectangle = CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with { LocalToSource = document.OutputToSource };
        var text = CreateAnnotation(ScreenshotAnnotationKind.Text) with
        {
            Start = new Point(8, 48), End = new Point(8, 48), Text = "Label", FontSize = 16,
            LocalToSource = document.OutputToSource
        };
        document.Add(rectangle);
        document.Add(text);
        TestAssert.True(document.GetAnnotationToOutput(rectangle).IsIdentity);
        TestAssert.True(document.GetAnnotationToOutput(text).IsIdentity);
        TestAssert.Equal(new Rect(0, 0, document.Width, document.Height), document.GetAnnotationLocalBounds(text));
        var reference = new ScreenshotDocument(CreateBitmap(document.Width, document.Height));
        reference.Add(rectangle with { LocalToSource = Matrix.Identity });
        reference.Add(text with { LocalToSource = Matrix.Identity });
        TestAssert.SequenceEqual(ReadPixels(reference.Render()), ReadPixels(document.Render()));
        var originalHeight = document.Height;
        document.RotateClockwise();
        TestAssert.Equal(new Matrix(0, 1, -1, 0, originalHeight, 0), document.GetAnnotationToOutput(text));
        document.Replace(1, text with { Text = "Edited" });
        TestAssert.Equal(text.LocalToSource, document.Annotations[1].LocalToSource);
        TestAssert.Equal(new Matrix(0, 1, -1, 0, originalHeight, 0), document.GetAnnotationToOutput(document.Annotations[1]));
    });

    internal static void ExistingTextRotatesAndRemainsEditable() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth * 4, FixtureHeight * 2));
        var text = CreateAnnotation(ScreenshotAnnotationKind.Text) with { Text = "Wide label", FontSize = 16 };
        document.Add(text);
        var horizontal = InkBounds(ReadPixels(document.Render()), document.Width, document.Height);
        TestAssert.True(horizontal.Width > horizontal.Height);
        document.RotateClockwise();
        var vertical = InkBounds(ReadPixels(document.Render()), document.Width, document.Height);
        TestAssert.True(vertical.Height > vertical.Width);
        TestAssert.Equal(text, document.Annotations[0]);
        document.Replace(0, text with { Text = "New label" });
        TestAssert.Equal("New label", document.Annotations[0].Text);
        TestAssert.True(InkBounds(ReadPixels(document.Render()), document.Width, document.Height).Height > vertical.Width);
        document.Undo();
        TestAssert.Equal(text, document.Annotations[0]);
    });

    internal static void TransformedBlurScalesItsRegionAndPreservesAnnotationOrder() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var rectangle = CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with
        {
            Start = new Point(12, 8), End = new Point(32, 24), StrokeWidth = 6
        };
        var blur = CreateAnnotation(ScreenshotAnnotationKind.Blur) with
        {
            Start = new Point(8, 4), End = new Point(40, 28), BlurRadius = 3
        };
        document.Add(rectangle);
        document.Add(blur);
        document.RotateClockwise();
        document.Resize(FixtureHeight * 2, FixtureWidth * 2);

        var blurred = PixelAt(document.Render(), 80, 40);
        TestAssert.True(blurred.G > 0 && blurred.G < byte.MaxValue);
        document.Add(rectangle with
        {
            Start = new Point(30, 30), End = new Point(70, 90), Color = Colors.Blue, StrokeWidth = 4,
            LocalToSource = document.OutputToSource
        });
        TestAssert.Equal(Colors.Blue, PixelAt(document.Render(), 50, 30));
        TestAssert.Equal(Colors.White, PixelAt(document.Render(), 0, 0));

        var offscreen = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, Checkerboard));
        offscreen.Add(blur with { Start = new Point(0, 0), End = new Point(8, 8) });
        offscreen.Crop(new Int32Rect(16, 16, 16, 16));
        var plain = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, Checkerboard));
        plain.Crop(new Int32Rect(16, 16, 16, 16));
        TestAssert.SequenceEqual(ReadPixels(plain.Render()), ReadPixels(offscreen.Render()));
        TestAssert.Equal(1, offscreen.Annotations.Length);
    });

    internal static void CropPreservesExistingBlurPixelsAndAlpha() => RunOnStaThread(() =>
    {
        const int width = 32;
        const int height = 8;
        const byte alpha = 128;
        var document = new ScreenshotDocument(CreateBitmap(width, height,
            (x, _) => Color.FromArgb(alpha, (byte)(x * 7), (byte)(x * 5), (byte)(x * 3))));
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Blur) with
        {
            Start = new Point(0, 0), End = new Point(width, height), BlurRadius = 3
        });
        var before = document.Render();
        foreach (var value in ReadPixels(before).Where((_, index) => index % BytesPerPixel == BytesPerPixel - 1))
        {
            TestAssert.Equal(alpha, value);
        }
        var crop = new Int32Rect(8, 0, 16, height);
        document.Crop(crop);
        AssertBitmapCropMatches(before, document.Render(), crop);
        document.Undo();
        TestAssert.SequenceEqual(ReadPixels(before), ReadPixels(document.Render()));
    });

    internal static void OpaqueTextAndBlurPreserveAlphaAndPngPixels() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(160, 100, (x, y) =>
        {
            var shade = (byte)((x + y) % 2 == 0 ? 32 : 224);
            return Color.FromRgb(shade, shade, shade);
        }));
        var color = (Color)ColorConverter.ConvertFromString(ScreenshotEditorOptions.DefaultColor);
        document.Add(new ScreenshotAnnotation(ScreenshotAnnotationKind.Rectangle, new Point(12, 12), new Point(120, 80), color));
        document.Add(new ScreenshotAnnotation(ScreenshotAnnotationKind.Text, new Point(18, 20), new Point(18, 20), color, Text: "Review"));
        document.Add(new ScreenshotAnnotation(ScreenshotAnnotationKind.Blur, new Point(40, 40), new Point(100, 72), color));
        var rendered = document.Render();
        var pixels = ReadPixels(rendered);
        foreach (var alpha in pixels.Where((_, index) => index % BytesPerPixel == BytesPerPixel - 1))
        {
            TestAssert.Equal(byte.MaxValue, alpha);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        TestAssert.SequenceEqual(pixels, ReadPixels(decoder.Frames[0]));
    });

    internal static void BlurCreatedAfterResizeUsesOutputPixelRadius() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(32, 16, (x, _) => x % 8 < 4 ? Colors.Black : Colors.White));
        document.RotateClockwise();
        document.Resize(32, 64);
        var reference = new ScreenshotDocument(document.Render());
        var blur = CreateAnnotation(ScreenshotAnnotationKind.Blur) with
        {
            Start = new Point(8, 4), End = new Point(24, 48), BlurRadius = 2,
            LocalToSource = document.OutputToSource
        };
        document.Add(blur);
        reference.Add(blur with { LocalToSource = Matrix.Identity });
        TestAssert.SequenceEqual(ReadPixels(reference.Render()), ReadPixels(document.Render()));
    });

    internal static void OverlappingBlursKeepTheirOrderAndCropAppearance() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight, CoordinateColor));
        var firstBlur = CreateAnnotation(ScreenshotAnnotationKind.Blur) with
        {
            Start = new Point(8, 4), End = new Point(40, 36), BlurRadius = 3
        };
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle));
        document.Add(firstBlur);
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with
        {
            Start = new Point(16, 8), End = new Point(52, 40), Color = Colors.Blue
        });
        TestAssert.Equal(Colors.Blue, PixelAt(document.Render(), 30, 40));
        document.Add(firstBlur with { Start = new Point(20, 12), End = new Point(56, 44), BlurRadius = 2 });
        var before = document.Render();
        TestAssert.True(PixelAt(before, 30, 40).G > 0);
        var crop = new Int32Rect(16, 8, 32, 32);
        document.Crop(crop);
        AssertBitmapCropMatches(before, document.Render(), crop);
        document.Add(CreateAnnotation(ScreenshotAnnotationKind.Rectangle) with
        {
            Start = new Point(4, 4), End = new Point(20, 20), LocalToSource = document.OutputToSource
        });
        TestAssert.Equal(Colors.Red, PixelAt(document.Render(), 10, 4));
    });

    internal static void InvalidAnnotationTransformsDoNotChangeContentOrHistory() => RunOnStaThread(() =>
    {
        var document = new ScreenshotDocument(CreateBitmap(FixtureWidth, FixtureHeight));
        var annotation = CreateAnnotation(ScreenshotAnnotationKind.Rectangle);
        document.Add(annotation);
        var initial = document.State;
        foreach (var transform in new[]
        {
            new Matrix(0, 0, 0, 0, 0, 0), new Matrix(-1, 0, 0, 1, 0, 0), new Matrix(1, 1, 0, 1, 0, 0),
            new Matrix(double.NaN, 0, 0, 1, 0, 0), new Matrix(1, 0, 0, 1, double.PositiveInfinity, 0)
        })
        {
            var invalid = annotation with { LocalToSource = transform };
            ExpectException<ArgumentException>(() => document.Add(invalid));
            ExpectException<ArgumentException>(() => document.Replace(0, invalid));
            ExpectException<ArgumentException>(() => document.RenderReplacing(0, invalid));
            ExpectException<ArgumentException>(() => document.GetAnnotationToOutput(invalid));
            ExpectException<ArgumentException>(() => document.GetAnnotationLocalBounds(invalid));
        }
        foreach (var dimension in new[] { ScreenshotDocument.MaximumSourceDimension + 1, (int)Math.Sqrt(ScreenshotDocument.MaximumPixelCount) + 1 })
        {
            var scale = 1.0 / dimension;
            var oversized = annotation with
            {
                Kind = ScreenshotAnnotationKind.Blur, Start = new Point(), End = new Point(dimension, dimension),
                LocalToSource = new Matrix(scale, 0, 0, scale, 0, 0)
            };
            ExpectException<ArgumentOutOfRangeException>(() => document.Add(oversized));
            ExpectException<ArgumentOutOfRangeException>(() => document.Replace(0, oversized));
            ExpectException<ArgumentOutOfRangeException>(() => document.RenderReplacing(0, oversized));
        }
        TestAssert.True(document.HasSameContent(initial));
        document.Undo();
        TestAssert.False(document.HasAnnotations);
        TestAssert.False(document.CanUndo);
    });

    private static Color CoordinateColor(int x, int y) => Color.FromRgb((byte)x, (byte)y, (byte)(x + y));

    private static void AssertBitmapCropMatches(System.Windows.Media.Imaging.BitmapSource before,
        System.Windows.Media.Imaging.BitmapSource after, Int32Rect crop)
    {
        TestAssert.Equal(crop.Width, after.PixelWidth);
        TestAssert.Equal(crop.Height, after.PixelHeight);
        var expected = ReadPixels(before);
        var actual = ReadPixels(after);
        for (var y = 0; y < crop.Height; y++)
        {
            for (var x = 0; x < crop.Width; x++)
            {
                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    TestAssert.Equal(expected[((y + crop.Y) * before.PixelWidth + x + crop.X) * BytesPerPixel + channel],
                        actual[(y * crop.Width + x) * BytesPerPixel + channel]);
                }
            }
        }
    }

    private static Rect InkBounds(byte[] pixels, int width, int height)
    {
        var left = width;
        var top = height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * BytesPerPixel;
                if (pixels[offset] == byte.MaxValue && pixels[offset + 1] == byte.MaxValue && pixels[offset + 2] == byte.MaxValue) { continue; }
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
