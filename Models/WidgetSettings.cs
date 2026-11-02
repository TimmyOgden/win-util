namespace WinUtil.Models;

internal sealed record WidgetSettings
{
    internal const double MaximumBackgroundOpacity = 1.0;
    internal const double MaximumFadedOpacity = 0.90;
    internal const double MaximumOpacity = 1.0;
    internal const double MinimumBackgroundOpacity = 0.55;
    internal const double MinimumFadedOpacity = 0.10;
    internal const double MinimumOpacity = 0.55;
    internal const int ShortAutoHideDelaySeconds = 10;
    internal const int DefaultAutoHideDelaySeconds = 20;
    internal const int MinimumPasswordLength = 8;
    internal const int MaximumPasswordLength = 64;
    internal const int DefaultPasswordLength = 20;
    internal const int JwtSecretBytesFor256Bits = 32;
    internal const int JwtSecretBytesFor384Bits = 48;
    internal const int JwtSecretBytesFor512Bits = 64;
    internal const int DefaultJwtSecretLengthBytes = JwtSecretBytesFor256Bits;

    internal static WidgetSettings Default { get; } = new();

    public bool KeepOnTop { get; init; } = true;

    public bool RunAtStartup { get; init; } = true;

    public bool OpenScreenshotEditorAfterCapture { get; init; } = true;

    public ScreenshotEditorOptions ScreenshotEditor { get; init; } = new();

    public BatteryAlertSettings BatteryAlerts { get; init; } = new();

    public int AutoHideDelaySeconds { get; init; } = DefaultAutoHideDelaySeconds;

    public double BackgroundOpacity { get; init; } = MaximumBackgroundOpacity;

    public double FadedOpacity { get; init; } = MinimumFadedOpacity;

    public double? Left { get; init; }

    public double Opacity { get; init; } = 0.92;

    public bool PasswordIncludesDigits { get; init; } = true;

    public bool PasswordIncludesLowercase { get; init; } = true;

    public bool PasswordIncludesSymbols { get; init; } = true;

    public bool PasswordIncludesUppercase { get; init; } = true;

    public int PasswordLength { get; init; } = DefaultPasswordLength;

    public int JwtSecretLengthBytes { get; init; } = DefaultJwtSecretLengthBytes;

    public double? Top { get; init; }

    public bool ShowSeconds { get; init; } = true;

    public SpotlightShortcuts SpotlightShortcuts { get; init; } = new();

    public SpotlightPreferences SpotlightPreferences { get; init; } = new();

    public UuidVersion UuidVersion { get; init; } = UuidVersion.Version4;

    internal WidgetSettings Normalize()
    {
        var opacity = NormalizeOpacity(Opacity, MinimumOpacity, MaximumOpacity, Default.Opacity);

        return this with
        {
            AutoHideDelaySeconds = IsSupportedAutoHideDelay(AutoHideDelaySeconds)
                ? AutoHideDelaySeconds
                : DefaultAutoHideDelaySeconds,
            BackgroundOpacity = NormalizeOpacity(
                BackgroundOpacity,
                MinimumBackgroundOpacity,
                MaximumBackgroundOpacity,
                Default.BackgroundOpacity),
            FadedOpacity = Math.Min(
                NormalizeOpacity(FadedOpacity, MinimumFadedOpacity, MaximumFadedOpacity, Default.FadedOpacity),
                opacity),
            JwtSecretLengthBytes = IsSupportedJwtSecretLength(JwtSecretLengthBytes)
                ? JwtSecretLengthBytes
                : DefaultJwtSecretLengthBytes,
            Left = NormalizePosition(Left),
            Opacity = opacity,
            PasswordIncludesDigits = HasPasswordCharacterSet ? PasswordIncludesDigits : Default.PasswordIncludesDigits,
            PasswordIncludesLowercase = HasPasswordCharacterSet ? PasswordIncludesLowercase : Default.PasswordIncludesLowercase,
            PasswordIncludesSymbols = HasPasswordCharacterSet ? PasswordIncludesSymbols : Default.PasswordIncludesSymbols,
            PasswordIncludesUppercase = HasPasswordCharacterSet ? PasswordIncludesUppercase : Default.PasswordIncludesUppercase,
            PasswordLength = Math.Clamp(PasswordLength, MinimumPasswordLength, MaximumPasswordLength),
            ScreenshotEditor = (ScreenshotEditor ?? new ScreenshotEditorOptions()).Normalize(),
            BatteryAlerts = (BatteryAlerts ?? new BatteryAlertSettings()).Normalize(),
            SpotlightShortcuts = (SpotlightShortcuts ?? new SpotlightShortcuts()).Normalize(),
            SpotlightPreferences = (SpotlightPreferences ?? new SpotlightPreferences()).Normalize(),
            Top = NormalizePosition(Top),
            UuidVersion = Enum.IsDefined(UuidVersion) ? UuidVersion : Default.UuidVersion
        };
    }

    private bool HasPasswordCharacterSet => PasswordIncludesDigits
        || PasswordIncludesLowercase
        || PasswordIncludesSymbols
        || PasswordIncludesUppercase;

    private static bool IsSupportedAutoHideDelay(int delaySeconds) => delaySeconds is
        ShortAutoHideDelaySeconds or DefaultAutoHideDelaySeconds;

    private static bool IsSupportedJwtSecretLength(int lengthBytes) => lengthBytes is
        JwtSecretBytesFor256Bits or JwtSecretBytesFor384Bits or JwtSecretBytesFor512Bits;

    private static double? NormalizePosition(double? position) => position is double value
        && !double.IsNaN(value)
        && !double.IsInfinity(value)
        ? value
        : null;

    private static double NormalizeOpacity(double opacity, double minimum, double maximum, double fallback) =>
        double.IsNaN(opacity) || double.IsInfinity(opacity)
            ? fallback
            : Math.Clamp(opacity, minimum, maximum);
}
