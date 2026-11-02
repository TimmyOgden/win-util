using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using System.Windows.Media;

namespace WinUtil.Models;

internal enum ScreenshotAnnotationKind
{
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Blur
}

internal sealed record ScreenshotAnnotation(
    ScreenshotAnnotationKind Kind,
    Point Start,
    Point End,
    Color Color,
    double StrokeWidth = 4,
    string Text = "",
    double FontSize = 24,
    double BlurRadius = 12)
{
    internal Matrix LocalToSource { get; init; } = Matrix.Identity;
}
