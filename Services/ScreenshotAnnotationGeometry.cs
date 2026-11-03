using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinUtil.Models;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WinUtil.Services;

internal enum ScreenshotResizeHandle
{
    Move,
    Start,
    End,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

internal static class ScreenshotAnnotationGeometry
{
    private const double LastPixelOffset = 1;

    internal static Rect GetBounds(ScreenshotAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        if (annotation.Kind != ScreenshotAnnotationKind.Text)
        {
            return new Rect(annotation.Start, annotation.End);
        }

        var text = new FormattedText(annotation.Text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), annotation.FontSize, Brushes.Black, 1);
        return new Rect(annotation.Start, new Size(text.WidthIncludingTrailingWhitespace, text.Height));
    }

    internal static bool HitTest(ScreenshotAnnotation annotation, Point point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidatePoint(point);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (!double.IsFinite(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        if (annotation.Kind == ScreenshotAnnotationKind.Arrow)
        {
            var direction = annotation.End - annotation.Start;
            var fraction = direction.LengthSquared == 0 ? 0 :
                Math.Clamp(Vector.Multiply(point - annotation.Start, direction) / direction.LengthSquared, 0, 1);
            return (point - (annotation.Start + direction * fraction)).Length <= tolerance + annotation.StrokeWidth;
        }

        var bounds = GetBounds(annotation);
        bounds.Inflate(tolerance, tolerance);
        return bounds.Contains(point);
    }

    internal static ScreenshotAnnotation Move(ScreenshotAnnotation annotation, Vector offset, int width, int height)
    {
        ValidateDimensions(width, height);
        return Move(annotation, offset, new Rect(0, 0, width, height));
    }

    internal static ScreenshotAnnotation Move(ScreenshotAnnotation annotation, Vector offset, Rect limits)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidatePoint(new Point(offset.X, offset.Y));
        ValidateLimits(limits);

        if (annotation.Kind == ScreenshotAnnotationKind.Text)
        {
            var anchor = new Point(
                Math.Clamp(annotation.Start.X + offset.X, limits.Left, Math.Max(limits.Left, limits.Right - LastPixelOffset)),
                Math.Clamp(annotation.Start.Y + offset.Y, limits.Top, Math.Max(limits.Top, limits.Bottom - LastPixelOffset)));
            return annotation with { Start = anchor, End = anchor };
        }

        var bounds = GetBounds(annotation);
        var maximumLeft = Math.Max(limits.Left, limits.Right - bounds.Width);
        var maximumTop = Math.Max(limits.Top, limits.Bottom - bounds.Height);
        var appliedOffset = new Vector(
            Math.Clamp(bounds.Left + offset.X, limits.Left, maximumLeft) - bounds.Left,
            Math.Clamp(bounds.Top + offset.Y, limits.Top, maximumTop) - bounds.Top);
        var start = annotation.Start + appliedOffset;
        var end = annotation.End + appliedOffset;

        return annotation with { Start = start, End = end };
    }

    internal static ScreenshotAnnotation Resize(ScreenshotAnnotation annotation, ScreenshotResizeHandle handle, Point point, int width, int height)
    {
        ValidateDimensions(width, height);
        return Resize(annotation, handle, point, new Rect(0, 0, width, height));
    }

    internal static ScreenshotAnnotation Resize(ScreenshotAnnotation annotation, ScreenshotResizeHandle handle, Point point, Rect limits)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidatePoint(point);
        ValidateLimits(limits);

        if (!Enum.IsDefined(handle) || handle == ScreenshotResizeHandle.Move || annotation.Kind == ScreenshotAnnotationKind.Text)
        {
            throw new ArgumentException("Choose a resize handle on a shape or arrow.", nameof(handle));
        }

        point = new Point(Math.Clamp(point.X, limits.Left, limits.Right), Math.Clamp(point.Y, limits.Top, limits.Bottom));

        if (annotation.Kind == ScreenshotAnnotationKind.Arrow)
        {
            var changed = handle switch
            {
                ScreenshotResizeHandle.Start => annotation with { Start = point },
                ScreenshotResizeHandle.End => annotation with { End = point },
                _ => throw new ArgumentException("Arrows resize using their endpoints.", nameof(handle))
            };
            return changed.Start == changed.End ? annotation : changed;
        }

        var bounds = new Rect(annotation.Start, annotation.End);
        var (originalCorner, opposite) = handle switch
        {
            ScreenshotResizeHandle.TopLeft => (bounds.TopLeft, bounds.BottomRight),
            ScreenshotResizeHandle.TopRight => (bounds.TopRight, bounds.BottomLeft),
            ScreenshotResizeHandle.BottomLeft => (bounds.BottomLeft, bounds.TopRight),
            ScreenshotResizeHandle.BottomRight => (bounds.BottomRight, bounds.TopLeft),
            _ => throw new ArgumentException("Shapes resize using their corners.", nameof(handle))
        };
        return point == originalCorner || point.X == opposite.X || point.Y == opposite.Y
            ? annotation
            : annotation with { Start = opposite, End = point };
    }

    private static void ValidatePoint(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Coordinates must be finite.");
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
    }

    private static void ValidateLimits(Rect limits)
    {
        if (limits.IsEmpty || !double.IsFinite(limits.Left) || !double.IsFinite(limits.Top)
            || !double.IsFinite(limits.Right) || !double.IsFinite(limits.Bottom)
            || !double.IsFinite(limits.Width) || !double.IsFinite(limits.Height) || limits.Width <= 0 || limits.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Annotation limits must be finite and nonempty.");
        }
    }
}
