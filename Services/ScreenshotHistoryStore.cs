using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using WinUtil.Models;

namespace WinUtil.Services;

internal sealed class ScreenshotHistoryStore
{
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";
    private const string ImageExtension = ".png";
    private const string PendingExtension = ".pending";
    private const int FileNamePartCount = 3;
    internal const int MaximumPreviewDimensionPixels = 960;

    internal ScreenshotHistoryStore(string? directoryPath = null)
    {
        DirectoryPath = Path.GetFullPath(directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUtil", "ScreenshotHistory"));
    }

    internal string DirectoryPath { get; }

    internal ScreenshotHistoryEntry Add(BitmapSource image, ScreenshotHistorySource source)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Enum.IsDefined(source)) { throw new ArgumentOutOfRangeException(nameof(source)); }
        ScreenshotImageLoader.ValidateDimensions(image.PixelWidth, image.PixelHeight);
        var createdAt = DateTimeOffset.UtcNow;
        var timestamp = createdAt.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var fileName = $"{timestamp}-{Guid.NewGuid():N}-{source}{ImageExtension}";
        var path = Path.Combine(DirectoryPath, fileName);
        var pendingPath = path + PendingExtension;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(DirectoryPath);

        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
            File.Move(pendingPath, path);
        }
        finally
        {
            if (File.Exists(pendingPath)) { File.Delete(pendingPath); }
        }

        return new ScreenshotHistoryEntry(fileName, createdAt, source, new FileInfo(path).Length);
    }

    internal IReadOnlyList<ScreenshotHistoryEntry> List()
    {
        if (!Directory.Exists(DirectoryPath)) { return []; }
        return new DirectoryInfo(DirectoryPath).EnumerateFiles($"*{ImageExtension}")
            .Select(file => TryParseFileName(file.Name, out var createdAt, out var source)
                ? new ScreenshotHistoryEntry(file.Name, createdAt, source, file.Length) : null)
            .OfType<ScreenshotHistoryEntry>()
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.FileName, StringComparer.Ordinal)
            .ToArray();
    }

    internal BitmapSource Load(ScreenshotHistoryEntry entry) => ScreenshotImageLoader.LoadFile(GetPath(entry));

    internal BitmapSource LoadPreview(ScreenshotHistoryEntry entry)
    {
        using var stream = File.OpenRead(GetPath(entry));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        ScreenshotImageLoader.ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= frame.PixelHeight)
        {
            image.DecodePixelWidth = Math.Min(MaximumPreviewDimensionPixels, frame.PixelWidth);
        }
        else { image.DecodePixelHeight = Math.Min(MaximumPreviewDimensionPixels, frame.PixelHeight); }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private string GetPath(ScreenshotHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!TryParseFileName(entry.FileName, out _, out _))
        {
            throw new ArgumentException("Choose an image from screenshot history.", nameof(entry));
        }
        return Path.Combine(DirectoryPath, entry.FileName);
    }

    private static bool TryParseFileName(string? fileName, out DateTimeOffset createdAt, out ScreenshotHistorySource source)
    {
        createdAt = default;
        source = default;
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(ImageExtension, StringComparison.Ordinal)) { return false; }
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('-');
        return parts.Length == FileNamePartCount
            && DateTimeOffset.TryParseExact(parts[0], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out createdAt)
            && Guid.TryParseExact(parts[1], "N", out _)
            && Enum.TryParse(parts[2], out source) && Enum.IsDefined(source)
            && parts[2] == source.ToString();
    }
}
