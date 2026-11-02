using WinUtil.Services;

namespace WinUtil.Models;

internal sealed record SpotlightShortcuts
{
    internal const uint PaletteModifiers = GlobalShortcut.AltModifier;
    internal const uint PaletteVirtualKey = 0x32;

    internal static GlobalShortcut DefaultPalette { get; } = new()
    {
        Modifiers = PaletteModifiers,
        VirtualKey = PaletteVirtualKey
    };

    // JSON shape is one named property per action; this table is the single place that maps action ids onto them.
    private static readonly IReadOnlyList<ShortcutAccessor> Accessors =
    [
        new(SpotlightActionIds.Uuid, shortcuts => shortcuts.Uuid, (shortcuts, value) => shortcuts with { Uuid = value }),
        new(SpotlightActionIds.Password, shortcuts => shortcuts.Password, (shortcuts, value) => shortcuts with { Password = value }),
        new(SpotlightActionIds.JwtSecret, shortcuts => shortcuts.JwtSecret, (shortcuts, value) => shortcuts with { JwtSecret = value }),
        new(SpotlightActionIds.FormatJson, shortcuts => shortcuts.FormatJson, (shortcuts, value) => shortcuts with { FormatJson = value }),
        new(SpotlightActionIds.MinifyJson, shortcuts => shortcuts.MinifyJson, (shortcuts, value) => shortcuts with { MinifyJson = value }),
        new(SpotlightActionIds.Base64Encode, shortcuts => shortcuts.Base64Encode, (shortcuts, value) => shortcuts with { Base64Encode = value }),
        new(SpotlightActionIds.Base64Decode, shortcuts => shortcuts.Base64Decode, (shortcuts, value) => shortcuts with { Base64Decode = value }),
        new(SpotlightActionIds.UrlEncode, shortcuts => shortcuts.UrlEncode, (shortcuts, value) => shortcuts with { UrlEncode = value }),
        new(SpotlightActionIds.UrlDecode, shortcuts => shortcuts.UrlDecode, (shortcuts, value) => shortcuts with { UrlDecode = value }),
        new(SpotlightActionIds.Uppercase, shortcuts => shortcuts.Uppercase, (shortcuts, value) => shortcuts with { Uppercase = value }),
        new(SpotlightActionIds.Lowercase, shortcuts => shortcuts.Lowercase, (shortcuts, value) => shortcuts with { Lowercase = value })
    ];

    public GlobalShortcut Palette { get; init; } = DefaultPalette;

    public GlobalShortcut? FormatJson { get; init; }

    public GlobalShortcut? Base64Decode { get; init; }

    public GlobalShortcut? Base64Encode { get; init; }

    public GlobalShortcut? JwtSecret { get; init; }

    public GlobalShortcut? Lowercase { get; init; }

    public GlobalShortcut? MinifyJson { get; init; }

    public GlobalShortcut? Password { get; init; }

    public GlobalShortcut? Uppercase { get; init; }

    public GlobalShortcut? UrlDecode { get; init; }

    public GlobalShortcut? UrlEncode { get; init; }

    public GlobalShortcut? Uuid { get; init; }

    internal static IReadOnlyList<string> SupportedActionIds { get; } = Accessors.Select(accessor => accessor.ActionId).ToArray();

    internal GlobalShortcut? GetShortcut(string actionId) => FindAccessor(actionId).Get(this);

    internal SpotlightShortcuts WithShortcut(string actionId, GlobalShortcut? shortcut) => FindAccessor(actionId).Set(this, shortcut);

    internal IReadOnlyList<SpotlightShortcutBinding> GetBindings() => Accessors
        .Select(accessor => (accessor.ActionId, Shortcut: accessor.Get(this)))
        .Where(binding => binding.Shortcut is not null)
        .Select(binding => new SpotlightShortcutBinding(binding.ActionId, binding.Shortcut!))
        .ToArray();

    internal string? Validate()
    {
        var bindings = GetBindings();

        if (Palette is not { IsValid: true })
        {
            return "The Spotlight shortcut must include Ctrl, Alt, or Shift and a regular key.";
        }

        if (bindings.Any(binding => !binding.Shortcut.IsValid))
        {
            return "Each shortcut must include Ctrl, Alt, or Shift and a regular key.";
        }

        if (bindings.Any(binding => binding.Shortcut.Modifiers == Palette.Modifiers && binding.Shortcut.VirtualKey == Palette.VirtualKey))
        {
            return $"{ShortcutFormatter.Format(Palette)} is reserved for opening Spotlight.";
        }

        if (bindings.GroupBy(binding => binding.Shortcut).Any(group => group.Count() > 1))
        {
            return "Each Spotlight function needs a different shortcut.";
        }

        return null;
    }

    internal SpotlightShortcuts Normalize() => Accessors.Aggregate(
        this with { Palette = NormalizeShortcut(Palette) ?? DefaultPalette },
        (shortcuts, accessor) => accessor.Set(shortcuts, NormalizeShortcut(accessor.Get(shortcuts))));

    private static ShortcutAccessor FindAccessor(string actionId) =>
        Accessors.FirstOrDefault(accessor => accessor.ActionId == actionId)
        ?? throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unsupported Spotlight action.");

    private static GlobalShortcut? NormalizeShortcut(GlobalShortcut? shortcut) => shortcut is { IsValid: true }
        ? shortcut
        : null;

    private sealed record ShortcutAccessor(
        string ActionId,
        Func<SpotlightShortcuts, GlobalShortcut?> Get,
        Func<SpotlightShortcuts, GlobalShortcut?, SpotlightShortcuts> Set);
}

internal sealed record SpotlightShortcutBinding(string ActionId, GlobalShortcut Shortcut);
