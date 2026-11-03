using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class ScreenshotImageLoaderTests
{
    private const int FixtureWidth = 3;
    private const int FixtureHeight = 2;
    private const int BytesPerPixel = 4;
    private const double SourceDpi = 192;
    private const double EditorDpi = 96;

    internal static void LoadFilePreservesPngPixelsAlphaAndReleasesTheFile() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("image.png");
        var source = CreateImage();
        SaveImage(path, source, new PngBitmapEncoder());

        var loaded = ScreenshotImageLoader.LoadFile(path);

        TestAssert.Equal(FixtureWidth, loaded.PixelWidth);
        TestAssert.Equal(FixtureHeight, loaded.PixelHeight);
        TestAssert.Equal(EditorDpi, loaded.DpiX);
        TestAssert.Equal(EditorDpi, loaded.DpiY);
        TestAssert.True(loaded.IsFrozen);
        TestAssert.SequenceEqual(ReadPixels(source), ReadPixels(loaded));
        using var exclusiveFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        TestAssert.True(exclusiveFile.Length > 0);
    });

    internal static void LoadFileAcceptsJpegBmpAndCaseInsensitiveExtensions() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var source = CreateImage();
        var jpeg = directory.PathFor("photo.JPEG");
        var jpg = directory.PathFor("photo.jpg");
        var bmp = directory.PathFor("image.BMP");
        SaveImage(jpeg, source, new JpegBitmapEncoder());
        SaveImage(jpg, source, new JpegBitmapEncoder());
        SaveImage(bmp, source, new BmpBitmapEncoder());

        TestAssert.Equal(FixtureWidth, ScreenshotImageLoader.LoadFile(jpeg).PixelWidth);
        TestAssert.Equal(FixtureHeight, ScreenshotImageLoader.LoadFile(jpg).PixelHeight);
        TestAssert.Equal(FixtureWidth, ScreenshotImageLoader.LoadFile(bmp).PixelWidth);
    });

    internal static void LoadFileUsesSupportedContentAndRejectsDisguisedUnsupportedFormats() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var supported = directory.PathFor("png-content.jpg");
        var disguised = directory.PathFor("gif-content.png");
        var unsupportedExtension = directory.PathFor("image.gif");
        SaveImage(supported, CreateImage(), new PngBitmapEncoder());
        SaveImage(disguised, CreateImage(), new GifBitmapEncoder());
        SaveImage(unsupportedExtension, CreateImage(), new PngBitmapEncoder());

        TestAssert.Equal(FixtureWidth, ScreenshotImageLoader.LoadFile(supported).PixelWidth);
        ExpectException<NotSupportedException>(() => ScreenshotImageLoader.LoadFile(disguised));
        ExpectException<NotSupportedException>(() => ScreenshotImageLoader.LoadFile(unsupportedExtension));
    });

    internal static void LoadFileRejectsMissingBlankAndCorruptFiles() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var corrupt = directory.PathFor("corrupt.png");
        var empty = directory.PathFor("empty.bmp");
        File.WriteAllText(corrupt, "This is not an image.");
        File.WriteAllBytes(empty, []);

        ExpectException<ArgumentNullException>(() => ScreenshotImageLoader.LoadFile(null!));
        ExpectException<ArgumentException>(() => ScreenshotImageLoader.LoadFile(string.Empty));
        ExpectException<ArgumentException>(() => ScreenshotImageLoader.LoadFile(" "));
        ExpectException<FileNotFoundException>(() => ScreenshotImageLoader.LoadFile(directory.PathFor("missing.png")));
        ExpectException<InvalidDataException>(() => ScreenshotImageLoader.LoadFile(corrupt));
        ExpectException<InvalidDataException>(() => ScreenshotImageLoader.LoadFile(empty));
        using var exclusiveFile = new FileStream(corrupt, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        TestAssert.True(exclusiveFile.Length > 0);
    });

    internal static void LoadFileRejectsOversizedImageDimensionsFromMetadata() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("oversized.png");
        var width = ScreenshotDocument.MaximumSourceDimension + 1;
        var source = BitmapSource.Create(width, 1, SourceDpi, SourceDpi, PixelFormats.Bgra32, null, new byte[width * BytesPerPixel], width * BytesPerPixel);
        SaveImage(path, source, new PngBitmapEncoder());

        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.LoadFile(path));
        using var exclusiveFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        TestAssert.True(exclusiveFile.Length > 0);
    });

    internal static void PrepareClipboardImageCopiesPixelsAndDetachesFromMutableSource() => RunOnStaThread(() =>
    {
        var source = new WriteableBitmap(CreateImage());
        var originalPixels = ReadPixels(source);

        var prepared = ScreenshotImageLoader.PrepareClipboardImage(source);
        source.WritePixels(new Int32Rect(0, 0, FixtureWidth, FixtureHeight), new byte[originalPixels.Length], FixtureWidth * BytesPerPixel, 0);

        TestAssert.True(prepared.IsFrozen);
        TestAssert.Equal(EditorDpi, prepared.DpiX);
        TestAssert.Equal(EditorDpi, prepared.DpiY);
        TestAssert.SequenceEqual(originalPixels, ReadPixels(prepared));
        TestAssert.False(ReferenceEquals(source, prepared));
    });

    internal static void PrepareClipboardImagePreservesIndexedPalette() => RunOnStaThread(() =>
    {
        var palette = new BitmapPalette([Colors.Transparent, Colors.Red]);
        const int width = 2;
        const int height = 1;
        var source = BitmapSource.Create(width, height, SourceDpi, SourceDpi, PixelFormats.Indexed8, palette, new byte[] { 0, 1 }, width);

        var prepared = ScreenshotImageLoader.PrepareClipboardImage(source);

        TestAssert.Equal(PixelFormats.Indexed8, prepared.Format);
        TestAssert.SequenceEqual(palette.Colors, prepared.Palette.Colors);
        var actual = new byte[width];
        prepared.CopyPixels(actual, width, 0);
        TestAssert.SequenceEqual(new byte[] { 0, 1 }, actual);
    });

    internal static void PrepareClipboardImageRejectsMissingAndUninitializedImages() => RunOnStaThread(() =>
    {
        var missing = ExpectException<InvalidOperationException>(() => ScreenshotImageLoader.PrepareClipboardImage(null));

        TestAssert.Equal("The clipboard does not contain an image.", missing.Message);
        ExpectException<InvalidOperationException>(() => ScreenshotImageLoader.PrepareClipboardImage(new BitmapImage()));
    });

    internal static void PrepareClipboardImageRejectsOversizedMetadataBeforeReadingPixels() => RunOnStaThread(() =>
    {
        var excessiveWidth = new MetadataOnlyBitmap(ScreenshotDocument.MaximumSourceDimension + 1, 1);
        var excessiveHeight = new MetadataOnlyBitmap(1, ScreenshotDocument.MaximumSourceDimension + 1);
        const int excessiveAreaDimension = 8001;
        var excessiveArea = new MetadataOnlyBitmap(excessiveAreaDimension, excessiveAreaDimension);

        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.PrepareClipboardImage(excessiveWidth));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.PrepareClipboardImage(excessiveHeight));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.PrepareClipboardImage(excessiveArea));
        TestAssert.False(excessiveWidth.PixelsRequested || excessiveHeight.PixelsRequested || excessiveArea.PixelsRequested);
    });

    internal static void ValidateDimensionsChecksZeroOverflowAndExactMaximumWithoutAllocatingPixels()
    {
        const int maximumAreaDimension = 8000;
        ScreenshotImageLoader.ValidateDimensions(1, 1);
        ScreenshotImageLoader.ValidateDimensions(ScreenshotDocument.MaximumSourceDimension, 1);
        ScreenshotImageLoader.ValidateDimensions(1, ScreenshotDocument.MaximumSourceDimension);
        ScreenshotImageLoader.ValidateDimensions(maximumAreaDimension, maximumAreaDimension);

        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.ValidateDimensions(0, 1));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.ValidateDimensions(1, 0));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.ValidateDimensions(-1, 1));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.ValidateDimensions(int.MaxValue, int.MaxValue));
        ExpectException<ArgumentOutOfRangeException>(() => ScreenshotImageLoader.ValidateDimensions(maximumAreaDimension, maximumAreaDimension + 1));
    }

    internal static void PrepareClipboardImageAcceptsMaximumWidthWithSmallPixelCount() => RunOnStaThread(() =>
    {
        var width = ScreenshotDocument.MaximumSourceDimension;
        var source = BitmapSource.Create(width, 1, SourceDpi, SourceDpi, PixelFormats.Bgra32, null, new byte[width * BytesPerPixel], width * BytesPerPixel);

        var prepared = ScreenshotImageLoader.PrepareClipboardImage(source);

        TestAssert.Equal(width, prepared.PixelWidth);
        TestAssert.Equal(1, prepared.PixelHeight);
        TestAssert.True(prepared.IsFrozen);
    });

    private static BitmapSource CreateImage()
    {
        byte[] pixels = [0, 0, 255, 255, 0, 255, 0, 128, 255, 0, 0, 64,
            25, 50, 75, 0, 10, 20, 30, 200, 255, 255, 255, 255];
        return BitmapSource.Create(FixtureWidth, FixtureHeight, SourceDpi, SourceDpi, PixelFormats.Bgra32, null, pixels, FixtureWidth * BytesPerPixel);
    }

    private static byte[] ReadPixels(BitmapSource image)
    {
        var readable = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = image.PixelWidth * BytesPerPixel;
        var pixels = new byte[stride * image.PixelHeight];
        readable.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static void SaveImage(string path, BitmapSource source, BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static TException ExpectException<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class MetadataOnlyBitmap(int width, int height) : BitmapSource
    {
        internal bool PixelsRequested { get; private set; }

        public override int PixelWidth => width;

        public override int PixelHeight => height;

        public override PixelFormat Format => PixelFormats.Bgra32;

        public override void CopyPixels(Array pixels, int stride, int offset)
        {
            PixelsRequested = true;
            throw new InvalidOperationException("An oversized image must be rejected before copying pixels.");
        }

        protected override Freezable CreateInstanceCore() => new MetadataOnlyBitmap(width, height);
    }
}
