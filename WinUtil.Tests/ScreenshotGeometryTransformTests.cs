using System.Windows;
using System.Windows.Media;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class ScreenshotGeometryTransformTests
{
    internal static void GeometryMovementAndResizeRespectNonzeroLocalBounds()
    {
        var limits = new Rect(-50, -20, 160, 100);
        var annotation = new ScreenshotAnnotation(ScreenshotAnnotationKind.Rectangle, new Point(-40, -10), new Point(40, 30), Colors.Red);
        TestAssert.Equal(annotation, ScreenshotAnnotationGeometry.Move(annotation, new Vector(), limits));
        var moved = ScreenshotAnnotationGeometry.Move(annotation, new Vector(-1000, -1000), limits);
        TestAssert.Equal(new Point(-50, -20), moved.Start);
        TestAssert.Equal(new Point(30, 20), moved.End);
        var resized = ScreenshotAnnotationGeometry.Resize(annotation, ScreenshotResizeHandle.BottomRight, new Point(1000, 1000), limits);
        TestAssert.Equal(new Point(110, 80), resized.End);
        var text = annotation with { Kind = ScreenshotAnnotationKind.Text, End = annotation.Start, Text = "Label" };
        TestAssert.Equal(new Point(109, 79), ScreenshotAnnotationGeometry.Move(text, new Vector(1000, 1000), limits).Start);
    }

    internal static void GeometryRejectsNullAndInvalidLocalBounds()
    {
        var annotation = new ScreenshotAnnotation(ScreenshotAnnotationKind.Rectangle, new Point(), new Point(10, 10), Colors.Red);
        foreach (var bounds in new[] { Rect.Empty, new Rect(0, 0, 0, 1), new Rect(double.NaN, 0, 1, 1), new Rect(double.MaxValue, 0, double.MaxValue, 1) })
        {
            ExpectArgument(() => ScreenshotAnnotationGeometry.Move(annotation, new Vector(), bounds));
            ExpectArgument(() => ScreenshotAnnotationGeometry.Resize(annotation, ScreenshotResizeHandle.BottomRight, new Point(), bounds));
        }
        ExpectArgument(() => ScreenshotAnnotationGeometry.Move(null!, new Vector(), new Rect(0, 0, 10, 10)));
        ExpectArgument(() => ScreenshotAnnotationGeometry.Resize(null!, ScreenshotResizeHandle.End, new Point(), new Rect(0, 0, 10, 10)));
    }

    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid geometry to be rejected.");
    }
}
