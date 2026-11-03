using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace WinUtil.Services;

internal sealed class ScreenshotDocument
{
    internal const double MinimumStrokeWidth = 1;
    internal const double MaximumStrokeWidth = 100;
    internal const double MinimumFontSize = 8;
    internal const double MaximumFontSize = 200;
    internal const double MinimumBlurRadius = 1;
    internal const double MaximumBlurRadius = 64;
    internal const int MaximumTextLength = 4096;
    internal const int MaximumSourceDimension = 32768;
    internal const long MaximumPixelCount = 64_000_000;

    private const double RenderDpi = 96;
    private const int BytesPerPixel = 4;
    private const int BlurPassCount = 3;
    private const double ArrowHeadStrokeMultiplier = 4;
    private const double MinimumArrowHeadLength = 12;
    private const double ArrowHeadLengthRatio = 0.5;
    private const double ArrowHeadWidthRatio = 0.5;
    private const int FullRotationQuarterTurns = 4;

    private readonly BitmapSource source;
    private readonly bool sourceIsOpaque;
    private ScreenshotDocumentState state;
    private ImmutableStack<ScreenshotDocumentState> undoHistory = ImmutableStack<ScreenshotDocumentState>.Empty;
    private ImmutableStack<ScreenshotDocumentState> redoHistory = ImmutableStack<ScreenshotDocumentState>.Empty;

    internal ScreenshotDocument(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.PixelWidth < 1 || source.PixelHeight < 1 ||
            source.PixelWidth > MaximumSourceDimension || source.PixelHeight > MaximumSourceDimension ||
            (long)source.PixelWidth * source.PixelHeight > MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "The screenshot dimensions exceed the editor's image limits.");
        }

        SourceWidth = source.PixelWidth;
        SourceHeight = source.PixelHeight;
        state = new ScreenshotDocumentState([], new Rect(0, 0, SourceWidth, SourceHeight), 0, SourceWidth, SourceHeight);
        var convertedSource = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = checked(Width * BytesPerPixel);
        var pixels = new byte[checked(stride * Height)];
        convertedSource.CopyPixels(pixels, stride, 0);
        sourceIsOpaque = HasOpaquePixels(pixels);
        this.source = CreateBitmap(pixels);
    }

    internal int Width => state.Width;
    internal int Height => state.Height;
    internal int SourceWidth { get; }
    internal int SourceHeight { get; }
    internal ScreenshotDocumentState State => state;
    internal Matrix SourceToOutput => CreateSourceToOutput(state);
    internal Matrix OutputToSource => Invert(SourceToOutput);
    internal bool CanUndo => !undoHistory.IsEmpty;
    internal bool CanRedo => !redoHistory.IsEmpty;
    internal bool HasAnnotations => !state.Annotations.IsEmpty;
    internal ImmutableArray<ScreenshotAnnotation> Annotations => state.Annotations;

    internal bool HasSameContent(ScreenshotDocumentState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return state.SourceRegion == other.SourceRegion && state.QuarterTurns == other.QuarterTurns &&
            Width == other.Width && Height == other.Height && Annotations.SequenceEqual(other.Annotations);
    }

    internal Matrix GetAnnotationToOutput(ScreenshotAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidateTransform(annotation.LocalToSource);
        var transform = Matrix.Multiply(annotation.LocalToSource, SourceToOutput);
        ValidateTransform(transform);
        return transform;
    }

    internal Rect GetAnnotationLocalBounds(ScreenshotAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidateTransform(annotation.LocalToSource);
        return TransformBounds(new Rect(0, 0, SourceWidth, SourceHeight), Invert(annotation.LocalToSource));
    }

    internal bool Crop(Int32Rect outputBounds)
    {
        if (outputBounds.X < 0 || outputBounds.Y < 0 || outputBounds.Width <= 0 || outputBounds.Height <= 0 ||
            (long)outputBounds.X + outputBounds.Width > Width || (long)outputBounds.Y + outputBounds.Height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(outputBounds), "The crop must be a nonempty region inside the image.");
        }

        if (outputBounds == new Int32Rect(0, 0, Width, Height)) { return false; }
        var region = TransformBounds(new Rect(outputBounds.X, outputBounds.Y, outputBounds.Width, outputBounds.Height), OutputToSource);
        return Commit(state with { SourceRegion = region, Width = outputBounds.Width, Height = outputBounds.Height });
    }

    internal bool Resize(int width, int height)
    {
        ValidateDimensions(width, height);
        var expectedHeight = Math.Max(1, Math.Round((double)Height * width / Width, MidpointRounding.AwayFromZero));
        var expectedWidth = Math.Max(1, Math.Round((double)Width * height / Height, MidpointRounding.AwayFromZero));
        if (height != expectedHeight && width != expectedWidth)
        {
            throw new ArgumentException("Resize dimensions must preserve the image aspect ratio.");
        }

        return Commit(state with { Width = width, Height = height });
    }

    internal bool RotateClockwise() => Rotate(1);

    internal bool RotateCounterclockwise() => Rotate(FullRotationQuarterTurns - 1);

    private bool Rotate(int quarterTurns) => Commit(state with
    {
        QuarterTurns = (state.QuarterTurns + quarterTurns) % FullRotationQuarterTurns,
        Width = Height,
        Height = Width
    });

    private bool Commit(ScreenshotDocumentState updated)
    {
        ValidateTransform(CreateSourceToOutput(updated));
        if (HasSameContent(updated)) { return false; }
        undoHistory = undoHistory.Push(state);
        state = updated;
        redoHistory = ImmutableStack<ScreenshotDocumentState>.Empty;
        return true;
    }

    internal void Add(ScreenshotAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var normalizedAnnotation = Normalize(annotation);
        Commit(state with { Annotations = Annotations.Add(normalizedAnnotation) });
    }

    internal void Replace(int index, ScreenshotAnnotation annotation)
    {
        ValidateIndex(index);
        ArgumentNullException.ThrowIfNull(annotation);
        var normalizedAnnotation = Normalize(annotation);

        if (Annotations[index] == normalizedAnnotation)
        {
            return;
        }

        Commit(state with { Annotations = Annotations.SetItem(index, normalizedAnnotation) });
    }

    internal void Remove(int index)
    {
        ValidateIndex(index);
        Commit(state with { Annotations = Annotations.RemoveAt(index) });
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= Annotations.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Select an existing annotation.");
        }
    }

    internal void Undo()
    {
        if (undoHistory.IsEmpty)
        {
            return;
        }

        redoHistory = redoHistory.Push(state);
        state = undoHistory.Peek();
        undoHistory = undoHistory.Pop();
    }

    internal void Redo()
    {
        if (redoHistory.IsEmpty)
        {
            return;
        }

        undoHistory = undoHistory.Push(state);
        state = redoHistory.Peek();
        redoHistory = redoHistory.Pop();
    }

    internal BitmapSource Render() => Render(Annotations);

    internal BitmapSource RenderReplacing(int index, ScreenshotAnnotation annotation)
    {
        ValidateIndex(index);
        ArgumentNullException.ThrowIfNull(annotation);
        return Render(Annotations.SetItem(index, Normalize(annotation)));
    }

    private BitmapSource Render(ImmutableArray<ScreenshotAnnotation> renderedAnnotations)
    {
        if (renderedAnnotations.IsEmpty) { return RenderSource(); }
        var background = new ImageDrawing(source, new Rect(0, 0, SourceWidth, SourceHeight));
        var layers = new DrawingGroup();
        layers.Children.Add(background);
        foreach (var annotation in renderedAnnotations)
        {
            var layer = new DrawingGroup { Transform = new MatrixTransform(annotation.LocalToSource) };
            using (var drawing = layer.Open())
            {
                if (annotation.Kind == ScreenshotAnnotationKind.Blur)
                {
                    var bounds = GetBlurPixelBounds(annotation);
                    drawing.DrawImage(RenderBlurPatch(annotation, bounds, layers), bounds);
                    var uncovered = new DrawingGroup
                    {
                        ClipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude,
                            new RectangleGeometry(new Rect(0, 0, SourceWidth, SourceHeight)),
                            new RectangleGeometry(TransformBounds(bounds, annotation.LocalToSource)))
                    };
                    uncovered.Children.Add(layers);
                    layers = new DrawingGroup();
                    layers.Children.Add(uncovered);
                }
                else
                {
                    DrawAnnotation(drawing, annotation);
                }
            }
            layer.Freeze();
            layers.Children.Add(layer);
        }
        var croppedSource = CreateCroppedSourceDrawing();
        background.ImageSource = croppedSource.ImageSource;
        background.Rect = croppedSource.Rect;
        layers.Freeze();
        return DrawLayers(layers);
    }

    private ScreenshotAnnotation Normalize(ScreenshotAnnotation annotation)
    {
        if (!Enum.IsDefined(annotation.Kind) || !IsFinite(annotation.Start) || !IsFinite(annotation.End))
        {
            throw new ArgumentException("The annotation must use a supported tool and finite coordinates.", nameof(annotation));
        }

        if (!IsInRange(annotation.StrokeWidth, MinimumStrokeWidth, MaximumStrokeWidth) ||
            !IsInRange(annotation.FontSize, MinimumFontSize, MaximumFontSize) ||
            !IsInRange(annotation.BlurRadius, MinimumBlurRadius, MaximumBlurRadius))
        {
            throw new ArgumentOutOfRangeException(nameof(annotation), "The annotation style exceeds the supported limits.");
        }

        if (annotation.Text is null || annotation.Text.Length > MaximumTextLength)
        {
            throw new ArgumentException("The annotation text exceeds the supported length.", nameof(annotation));
        }

        var localBounds = GetAnnotationLocalBounds(annotation);
        _ = GetAnnotationToOutput(annotation);
        var start = Clip(annotation.Start, localBounds);
        var end = Clip(annotation.End, localBounds);

        if (annotation.Kind == ScreenshotAnnotationKind.Text)
        {
            if (string.IsNullOrWhiteSpace(annotation.Text) || start.X >= localBounds.Right || start.Y >= localBounds.Bottom)
            {
                throw new ArgumentException("Text must contain visible characters and start inside the screenshot.", nameof(annotation));
            }
        }
        else if (annotation.Kind == ScreenshotAnnotationKind.Arrow)
        {
            if (start == end)
            {
                throw new ArgumentException("An arrow must have distinct endpoints inside the screenshot.", nameof(annotation));
            }
        }
        else if (start.X == end.X || start.Y == end.Y)
        {
            throw new ArgumentException("The selected region must have a nonzero width and height.", nameof(annotation));
        }

        var normalized = annotation with { Start = start, End = end };
        if (annotation.Kind == ScreenshotAnnotationKind.Blur) { _ = GetBlurPixelBounds(normalized); }
        return normalized;
    }

    private static Point Clip(Point point, Rect bounds) => new(Math.Clamp(point.X, bounds.Left, bounds.Right), Math.Clamp(point.Y, bounds.Top, bounds.Bottom));

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool IsInRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;

    private static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaximumSourceDimension || height > MaximumSourceDimension ||
            (long)width * height > MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The image dimensions exceed the editor's image limits.");
        }
    }

    private static Matrix CreateSourceToOutput(ScreenshotDocumentState content)
    {
        var region = content.SourceRegion;
        var isRotated = content.QuarterTurns % 2 != 0;
        var scaleX = content.Width / (isRotated ? region.Height : region.Width);
        var scaleY = content.Height / (isRotated ? region.Width : region.Height);
        return content.QuarterTurns switch
        {
            0 => new Matrix(scaleX, 0, 0, scaleY, -region.Left * scaleX, -region.Top * scaleY),
            1 => new Matrix(0, scaleY, -scaleX, 0, region.Bottom * scaleX, -region.Left * scaleY),
            2 => new Matrix(-scaleX, 0, 0, -scaleY, region.Right * scaleX, region.Bottom * scaleY),
            3 => new Matrix(0, -scaleY, scaleX, 0, -region.Top * scaleX, region.Right * scaleY),
            _ => throw new ArgumentOutOfRangeException(nameof(content))
        };
    }

    private static void ValidateTransform(Matrix transform)
    {
        var axisAligned = (transform.M12 == 0 && transform.M21 == 0 && transform.M11 != 0 && transform.M22 != 0)
            || (transform.M11 == 0 && transform.M22 == 0 && transform.M12 != 0 && transform.M21 != 0);
        if (!IsFinite(transform) || !axisAligned || !transform.HasInverse ||
            !double.IsFinite(transform.Determinant) || transform.Determinant <= 0 || !IsFinite(Invert(transform)))
        {
            throw new ArgumentException("Image transforms must use finite, invertible scaling and quarter-turn rotations.", nameof(transform));
        }
    }

    private static bool IsFinite(Matrix matrix) => double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12)
        && double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) && double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);

    private static Matrix Invert(Matrix matrix)
    {
        matrix.Invert();
        return matrix;
    }

    private static Rect TransformBounds(Rect bounds, Matrix transform)
    {
        var start = transform.Transform(bounds.TopLeft);
        var end = transform.Transform(bounds.BottomRight);
        if (!IsFinite(start) || !IsFinite(end))
        {
            throw new ArgumentException("The transformed image bounds exceed the supported coordinate range.", nameof(transform));
        }
        var transformed = new Rect(start, end);
        if (!double.IsFinite(transformed.Width) || !double.IsFinite(transformed.Height) || transformed.Width <= 0 || transformed.Height <= 0)
        {
            throw new ArgumentException("The transformed image bounds must have a finite width and height.", nameof(transform));
        }
        return transformed;
    }

    private BitmapSource RenderSource()
    {
        var transform = SourceToOutput;
        if (transform.IsIdentity && Width == SourceWidth && Height == SourceHeight) { return source; }
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
            drawing.PushTransform(new MatrixTransform(transform));
            drawing.DrawDrawing(CreateCroppedSourceDrawing());
            drawing.Pop();
            drawing.Pop();
        }
        return RenderVisual(visual, Width, Height);
    }

    private ImageDrawing CreateCroppedSourceDrawing()
    {
        var region = state.SourceRegion;
        var left = Math.Max(0, (int)Math.Floor(region.Left));
        var top = Math.Max(0, (int)Math.Floor(region.Top));
        var right = Math.Min(SourceWidth, (int)Math.Ceiling(region.Right));
        var bottom = Math.Min(SourceHeight, (int)Math.Ceiling(region.Bottom));
        var sourceBounds = new Int32Rect(left, top, right - left, bottom - top);
        var croppedSource = new CroppedBitmap(source, sourceBounds);
        croppedSource.Freeze();
        return new ImageDrawing(croppedSource, new Rect(left, top, sourceBounds.Width, sourceBounds.Height));
    }

    private BitmapSource DrawLayers(DrawingGroup layers)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var drawing = visual.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));

            drawing.PushTransform(new MatrixTransform(SourceToOutput));
            // WPF exclusion groups can round opaque glyph alpha down; opaque source images must remain opaque after these edits.
            if (sourceIsOpaque) { drawing.DrawDrawing(CreateCroppedSourceDrawing()); }
            drawing.DrawDrawing(layers);
            drawing.Pop();
            drawing.Pop();
        }

        return RenderVisual(visual, Width, Height);
    }

    private static BitmapSource RenderVisual(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawAnnotation(DrawingContext drawing, ScreenshotAnnotation annotation)
    {
        var brush = new SolidColorBrush(annotation.Color);
        brush.Freeze();
        var pen = new Pen(brush, annotation.StrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        var bounds = new Rect(annotation.Start, annotation.End);

        switch (annotation.Kind)
        {
            case ScreenshotAnnotationKind.Arrow:
                var direction = annotation.End - annotation.Start;
                var headLength = Math.Min(Math.Max(annotation.StrokeWidth * ArrowHeadStrokeMultiplier, MinimumArrowHeadLength), direction.Length * ArrowHeadLengthRatio);
                direction.Normalize();
                var perpendicular = new Vector(-direction.Y, direction.X);
                var headBase = annotation.End - direction * headLength;
                var head = new StreamGeometry();
                using (var geometry = head.Open())
                {
                    geometry.BeginFigure(annotation.End, true, true);
                    geometry.LineTo(headBase + perpendicular * headLength * ArrowHeadWidthRatio, true, false);
                    geometry.LineTo(headBase - perpendicular * headLength * ArrowHeadWidthRatio, true, false);
                }

                head.Freeze();
                drawing.DrawLine(pen, annotation.Start, headBase);
                drawing.DrawGeometry(brush, pen, head);
                break;
            case ScreenshotAnnotationKind.Rectangle:
                drawing.DrawRectangle(null, pen, bounds);
                break;
            case ScreenshotAnnotationKind.Ellipse:
                drawing.DrawEllipse(null, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2);
                break;
            case ScreenshotAnnotationKind.Text:
                var text = new FormattedText(annotation.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), annotation.FontSize, brush, 1);
                drawing.DrawText(text, annotation.Start);
                break;
            default:
                throw new InvalidOperationException("Only drawing annotations can be rendered as vectors.");
        }
    }

    private static Rect GetBlurPixelBounds(ScreenshotAnnotation annotation)
    {
        var bounds = new Rect(annotation.Start, annotation.End);
        var left = Math.Floor(bounds.Left);
        var top = Math.Floor(bounds.Top);
        var right = Math.Ceiling(bounds.Right);
        var bottom = Math.Ceiling(bounds.Bottom);
        var width = right - left;
        var height = bottom - top;
        if (width < 1 || height < 1 || width > MaximumSourceDimension || height > MaximumSourceDimension || width * height > MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(annotation), "The blur selection exceeds the editor's image limits.");
        }
        return new Rect(left, top, width, height);
    }

    private BitmapSource RenderBlurPatch(ScreenshotAnnotation annotation, Rect bounds, DrawingGroup previousLayers)
    {
        var sourceToPatch = Invert(annotation.LocalToSource);
        sourceToPatch.Translate(-bounds.Left, -bounds.Top);
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            drawing.PushTransform(new MatrixTransform(sourceToPatch));
            if (sourceIsOpaque) { drawing.DrawImage(source, new Rect(0, 0, SourceWidth, SourceHeight)); }
            drawing.DrawDrawing(previousLayers);
            drawing.Pop();
            drawing.Pop();
        }
        var background = RenderVisual(visual, width, height);
        var stride = checked(width * BytesPerPixel);
        var pixels = new byte[checked(stride * height)];
        background.CopyPixels(pixels, stride, 0);
        var buffer = new byte[pixels.Length];
        var radius = (int)Math.Ceiling(annotation.BlurRadius);

        // Filtering before the document crop keeps existing blur edges unchanged when the image is cropped again.
        for (var pass = 0; pass < BlurPassCount; pass++)
        {
            BlurAxis(pixels, buffer, width, height, radius, horizontal: true);
            BlurAxis(buffer, pixels, width, height, radius, horizontal: false);
        }
        return CreateBitmap(pixels, width, height);
    }

    private static void BlurAxis(byte[] input, byte[] output, int width, int height, int radius, bool horizontal)
    {
        var lineLength = horizontal ? width : height;
        var lineCount = horizontal ? height : width;
        var pixelStep = horizontal ? BytesPerPixel : width * BytesPerPixel;
        var lineStep = horizontal ? width * BytesPerPixel : BytesPerPixel;
        var windowLength = radius * 2 + 1;
        var sums = new int[BytesPerPixel];

        for (var line = 0; line < lineCount; line++)
        {
            Array.Clear(sums);
            var lineStart = line * lineStep;

            var firstPixelCopies = radius + 1;
            var lastPixelCopies = Math.Max(0, radius - lineLength + 1);
            for (var channel = 0; channel < BytesPerPixel; channel++)
            {
                sums[channel] = input[lineStart + channel] * firstPixelCopies
                    + input[lineStart + (lineLength - 1) * pixelStep + channel] * lastPixelCopies;
            }

            for (var offset = 1; offset <= Math.Min(radius, lineLength - 1); offset++)
            {
                var index = lineStart + offset * pixelStep;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    sums[channel] += input[index + channel];
                }
            }

            for (var position = 0; position < lineLength; position++)
            {
                var index = lineStart + position * pixelStep;
                var leaving = lineStart + Math.Clamp(position - radius, 0, lineLength - 1) * pixelStep;
                var entering = lineStart + Math.Clamp(position + radius + 1, 0, lineLength - 1) * pixelStep;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    output[index + channel] = (byte)((sums[channel] + windowLength / 2) / windowLength);
                    sums[channel] += input[entering + channel] - input[leaving + channel];
                }
            }
        }
    }

    private BitmapSource CreateBitmap(byte[] pixels) => CreateBitmap(pixels, Width, Height);

    private static bool HasOpaquePixels(ReadOnlySpan<byte> pixels)
    {
        for (var index = BytesPerPixel - 1; index < pixels.Length; index += BytesPerPixel)
        {
            if (pixels[index] != byte.MaxValue) { return false; }
        }
        return true;
    }

    private static BitmapSource CreateBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, RenderDpi, RenderDpi, PixelFormats.Pbgra32, null, pixels, checked(width * BytesPerPixel));
        bitmap.Freeze();
        return bitmap;
    }
}
