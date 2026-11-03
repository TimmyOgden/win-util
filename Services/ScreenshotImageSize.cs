namespace WinUtil.Services;

internal readonly record struct ImagePixelSize(int Width, int Height);

internal static class ScreenshotImageSize
{
    private const int MinimumDimension = 1;

    internal static ImagePixelSize ResizeToWidth(int originalWidth, int originalHeight, int width)
    {
        Validate(originalWidth, originalHeight);
        ValidateDimension(width);
        var height = Math.Max(MinimumDimension, (int)Math.Round(originalHeight * (double)width / originalWidth, MidpointRounding.AwayFromZero));
        Validate(width, height);
        return new(width, height);
    }

    internal static ImagePixelSize ResizeToHeight(int originalWidth, int originalHeight, int height)
    {
        Validate(originalWidth, originalHeight);
        ValidateDimension(height);
        var width = Math.Max(MinimumDimension, (int)Math.Round(originalWidth * (double)height / originalHeight, MidpointRounding.AwayFromZero));
        Validate(width, height);
        return new(width, height);
    }

    private static void Validate(int width, int height)
    {
        ValidateDimension(width);
        ValidateDimension(height);
        if ((long)width * height > ScreenshotDocument.MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The image exceeds the editor's pixel limit.");
        }
    }

    private static void ValidateDimension(int value)
    {
        if (value < MinimumDimension || value > ScreenshotDocument.MaximumSourceDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Image dimensions must be between {MinimumDimension} and {ScreenshotDocument.MaximumSourceDimension} pixels.");
        }
    }
}
