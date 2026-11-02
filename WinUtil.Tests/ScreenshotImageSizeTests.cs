using WinUtil.Services;

namespace WinUtil.Tests;

internal static class ScreenshotImageSizeTests
{
    internal static void ImageResizeCalculatesLinkedDimensionsAndRounding()
    {
        TestAssert.Equal(new ImagePixelSize(320, 240), ScreenshotImageSize.ResizeToWidth(640, 480, 320));
        TestAssert.Equal(new ImagePixelSize(320, 240), ScreenshotImageSize.ResizeToHeight(640, 480, 240));
        TestAssert.Equal(new ImagePixelSize(3, 2), ScreenshotImageSize.ResizeToWidth(2, 1, 3));
        TestAssert.Equal(new ImagePixelSize(2, 3), ScreenshotImageSize.ResizeToHeight(1, 2, 3));
        TestAssert.Equal(new ImagePixelSize(1, 1), ScreenshotImageSize.ResizeToWidth(ScreenshotDocument.MaximumSourceDimension, 1, 1));
        TestAssert.Equal(new ImagePixelSize(1, 1), ScreenshotImageSize.ResizeToHeight(1, ScreenshotDocument.MaximumSourceDimension, 1));
    }

    internal static void ImageResizeAcceptsDimensionAndPixelLimits()
    {
        TestAssert.Equal(new ImagePixelSize(ScreenshotDocument.MaximumSourceDimension, 1),
            ScreenshotImageSize.ResizeToWidth(ScreenshotDocument.MaximumSourceDimension, 1, ScreenshotDocument.MaximumSourceDimension));
        TestAssert.Equal(new ImagePixelSize(1, ScreenshotDocument.MaximumSourceDimension),
            ScreenshotImageSize.ResizeToHeight(1, ScreenshotDocument.MaximumSourceDimension, ScreenshotDocument.MaximumSourceDimension));
        const int maximumSquareDimension = 8000;
        TestAssert.Equal(new ImagePixelSize(maximumSquareDimension, maximumSquareDimension),
            ScreenshotImageSize.ResizeToWidth(1, 1, maximumSquareDimension));
    }

    internal static void ImageResizeRejectsInvalidSourceTargetAndOversizedDerivedDimensions()
    {
        foreach (var dimension in new[] { int.MinValue, -1, 0, ScreenshotDocument.MaximumSourceDimension + 1, int.MaxValue })
        {
            AssertInvalid(() => ScreenshotImageSize.ResizeToWidth(640, 480, dimension));
            AssertInvalid(() => ScreenshotImageSize.ResizeToHeight(640, 480, dimension));
            AssertInvalid(() => ScreenshotImageSize.ResizeToWidth(dimension, 480, 1));
            AssertInvalid(() => ScreenshotImageSize.ResizeToHeight(640, dimension, 1));
        }
        AssertInvalid(() => ScreenshotImageSize.ResizeToWidth(1, ScreenshotDocument.MaximumSourceDimension, 2));
        AssertInvalid(() => ScreenshotImageSize.ResizeToHeight(ScreenshotDocument.MaximumSourceDimension, 1, 2));
        AssertInvalid(() => ScreenshotImageSize.ResizeToWidth(1, 1, 8001));
        AssertInvalid(() => ScreenshotImageSize.ResizeToHeight(1, 1, 8001));
        AssertInvalid(() => ScreenshotImageSize.ResizeToWidth(8001, 8001, 1));
    }

    private static void AssertInvalid(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Expected an unsupported image size to be rejected.");
    }
}
