using WinUtil.Models;

namespace WinUtil.Services;

internal static class SpotlightActionCatalog
{
    internal static IReadOnlyList<SpotlightAction> All => Create(() => WidgetSettings.Default);

    internal static IReadOnlyList<SpotlightAction> Create(Func<WidgetSettings> settingsProvider) =>
    [
        SpotlightAction.CreateGenerator(
            SpotlightActionIds.Uuid,
            SpotlightActionCategories.Generators,
            "Generate UUID",
            "Create a new globally unique identifier.",
            ["uuid", "guid", "identifier", "generate uuid"],
            () => GenerateUuid(settingsProvider().UuidVersion)),
        SpotlightAction.CreateGenerator(
            SpotlightActionIds.Password,
            SpotlightActionCategories.Generators,
            "Generate password",
            "Create a secure password using your saved settings.",
            ["password", "secure", "generate password"],
            () => PasswordGenerator.Generate(settingsProvider())),
        SpotlightAction.CreateGenerator(
            SpotlightActionIds.JwtSecret,
            SpotlightActionCategories.Generators,
            "Generate JWT secret",
            "Create a Base64URL secret for JWT signing.",
            ["jwt", "jwt secret", "token", "hmac", "secret"],
            () => JwtSecretGenerator.Generate(settingsProvider())),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.FormatJson,
            SpotlightActionCategories.Json,
            "Format JSON",
            "Pretty-print your selected JSON and replace it in place.",
            ["json", "format json", "prettify json", "beautify json"],
            JsonTextFormatter.Format),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.MinifyJson,
            SpotlightActionCategories.Json,
            "Minify JSON",
            "Remove whitespace from selected JSON.",
            ["json", "minify json", "compact json"],
            JsonTextFormatter.Minify),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.Base64Encode,
            SpotlightActionCategories.Encoding,
            "Base64 encode",
            "Encode the selected text as Base64.",
            ["base64", "encode", "base64 encode"],
            TextTransformations.Base64Encode),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.Base64Decode,
            SpotlightActionCategories.Encoding,
            "Base64 decode",
            "Decode selected Base64 as UTF-8 text.",
            ["base64", "decode", "base64 decode"],
            TextTransformations.Base64Decode),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.UrlEncode,
            SpotlightActionCategories.Encoding,
            "URL encode",
            "Percent-encode the selected text.",
            ["url", "encode", "url encode", "percent encode"],
            TextTransformations.UrlEncode),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.UrlDecode,
            SpotlightActionCategories.Encoding,
            "URL decode",
            "Decode percent-encoded selected text.",
            ["url", "decode", "url decode", "percent decode"],
            TextTransformations.UrlDecode),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.Uppercase,
            SpotlightActionCategories.Case,
            "Convert to uppercase",
            "Replace selected text with uppercase characters.",
            ["uppercase", "upper", "case", "convert uppercase"],
            TextTransformations.ToUpperInvariant),
        SpotlightAction.CreateSelectedTextTransformer(
            SpotlightActionIds.Lowercase,
            SpotlightActionCategories.Case,
            "Convert to lowercase",
            "Replace selected text with lowercase characters.",
            ["lowercase", "lower", "case", "convert lowercase"],
            TextTransformations.ToLowerInvariant)
    ];

    internal static IReadOnlyList<SpotlightAction> Filter(string query) => Filter(All, query);

    internal static IReadOnlyList<SpotlightAction> Filter(IReadOnlyList<SpotlightAction> actions, string query)
        => SpotlightSearch.Rank(actions, query);

    private static string GenerateUuid(UuidVersion uuidVersion) => uuidVersion switch
    {
        UuidVersion.Version4 => Guid.NewGuid().ToString(),
        UuidVersion.Version7 => Guid.CreateVersion7().ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(uuidVersion), uuidVersion, "Unsupported UUID version.")
    };
}
