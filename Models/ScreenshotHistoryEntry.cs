namespace WinUtil.Models;

internal enum ScreenshotHistorySource
{
    Capture,
    Import,
    Export
}

internal sealed record ScreenshotHistoryEntry(
    string FileName,
    DateTimeOffset CreatedAt,
    ScreenshotHistorySource Source,
    long SizeBytes)
{
    public string DisplayName => Source switch
    {
        ScreenshotHistorySource.Capture => "Screenshot",
        ScreenshotHistorySource.Import => "Imported image",
        ScreenshotHistorySource.Export => "Exported image",
        _ => throw new InvalidOperationException("Unknown screenshot history source.")
    };

    public string CreatedText => CreatedAt.ToLocalTime().ToString("ddd, dd MMM yyyy · HH:mm:ss");

    public string SizeText => $"{SizeBytes:N0} bytes";
}
