using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace WinUtil.Services;

internal static class ScreenshotImageLoader
{
    private const double EditorDpi = 96;
    private const int BitsPerByte = 8;
    private const int MinimumDimension = 1;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    internal static BitmapSource LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            throw new NotSupportedException("Choose a PNG, JPEG, or BMP image file.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = CreateDecoder(stream);
        if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder and not BmpBitmapDecoder)
        {
            throw new NotSupportedException("The file contains an unsupported image format. Choose a PNG, JPEG, or BMP image.");
        }

        try
        {
            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException("The image file does not contain any image frames.");
            }

            return CreateDetachedCopy(decoder.Frames[0]);
        }
        catch (Exception exception) when (exception is FileFormatException or NotSupportedException or COMException)
        {
            throw new InvalidDataException("The image file is corrupt or could not be decoded.", exception);
        }
    }

    internal static BitmapSource PrepareClipboardImage(BitmapSource? image)
    {
        if (image is null)
        {
            throw new InvalidOperationException("The clipboard does not contain an image.");
        }

        return CreateDetachedCopy(image);
    }

    internal static void ValidateDimensions(int width, int height)
    {
        if (width < MinimumDimension || height < MinimumDimension
            || width > ScreenshotDocument.MaximumSourceDimension || height > ScreenshotDocument.MaximumSourceDimension
            || (long)width * height > ScreenshotDocument.MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The image dimensions exceed the editor's image limits.");
        }
    }

    private static BitmapDecoder CreateDecoder(Stream stream)
    {
        try
        {
            return BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        }
        catch (Exception exception) when (exception is FileFormatException or NotSupportedException or COMException)
        {
            throw new InvalidDataException("The image file is corrupt or could not be decoded.", exception);
        }
    }

    private static BitmapSource CreateDetachedCopy(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        ValidateDimensions(width, height);
        var format = image.Format;
        if (format.BitsPerPixel < 1)
        {
            throw new InvalidOperationException("The image pixel format is unavailable.");
        }

        var stride = checked((width * format.BitsPerPixel + BitsPerByte - 1) / BitsPerByte);
        var pixels = new byte[checked(stride * height)];
        image.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(width, height, EditorDpi, EditorDpi, format, image.Palette, pixels, stride);
        copy.Freeze();
        return copy;
    }
}
