namespace WinUtil.Models;

internal sealed record SpotlightPreferences
{
    internal const int MaximumRecentActions = 10;

    public bool RememberRecentCommands { get; init; } = true;

    public string[] FavoriteActionIds { get; init; } = [];

    public string[] RecentActionIds { get; init; } = [];

    internal SpotlightPreferences Normalize() => this with
    {
        FavoriteActionIds = NormalizeIds(FavoriteActionIds, SpotlightShortcuts.SupportedActionIds.Count),
        RecentActionIds = RememberRecentCommands ? NormalizeIds(RecentActionIds, MaximumRecentActions) : []
    };

    internal SpotlightPreferences ToggleFavorite(string actionId)
    {
        ValidateActionId(actionId);
        var preferences = Normalize();
        return preferences with
        {
            FavoriteActionIds = preferences.FavoriteActionIds.Contains(actionId, StringComparer.Ordinal)
                ? preferences.FavoriteActionIds.Where(id => id != actionId).ToArray()
                : [.. preferences.FavoriteActionIds, actionId]
        };
    }

    internal SpotlightPreferences RecordSuccess(string actionId)
    {
        ValidateActionId(actionId);
        var preferences = Normalize();
        return preferences.RememberRecentCommands
            ? preferences with
            {
                RecentActionIds = new[] { actionId }.Concat(preferences.RecentActionIds.Where(id => id != actionId))
                    .Take(MaximumRecentActions).ToArray()
            }
            : preferences;
    }

    internal SpotlightPreferences ClearHistory() => Normalize() with { RecentActionIds = [] };

    private static string[] NormalizeIds(string[]? ids, int maximumCount)
    {
        var normalized = (ids ?? []).Where(id => SpotlightShortcuts.SupportedActionIds.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).Take(maximumCount).ToArray();
        return ids is not null && ids.SequenceEqual(normalized, StringComparer.Ordinal) ? ids : normalized;
    }

    private static void ValidateActionId(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (!SpotlightShortcuts.SupportedActionIds.Contains(actionId, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choose a supported Spotlight command.", nameof(actionId));
        }
    }
}
