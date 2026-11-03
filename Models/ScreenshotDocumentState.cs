using System.Collections.Immutable;
using System.Windows;

namespace WinUtil.Models;

internal sealed record ScreenshotDocumentState(
    ImmutableArray<ScreenshotAnnotation> Annotations,
    Rect SourceRegion,
    int QuarterTurns,
    int Width,
    int Height);
