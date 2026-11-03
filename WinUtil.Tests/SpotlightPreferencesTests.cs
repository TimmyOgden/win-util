using System.Text.Json;
using WinUtil.Models;

namespace WinUtil.Tests;

internal static class SpotlightPreferencesTests
{
    internal static void NormalizeAcceptsLegacyAndMalformedCollections()
    {
        var empty = new SpotlightPreferences { FavoriteActionIds = null!, RecentActionIds = null! }.Normalize();
        TestAssert.Empty(empty.FavoriteActionIds);
        TestAssert.Empty(empty.RecentActionIds);
        TestAssert.True(empty.RememberRecentCommands);
        var preferences = new SpotlightPreferences
        {
            FavoriteActionIds = [null!, "", "unknown", "UUID", SpotlightActionIds.Uuid, SpotlightActionIds.Uuid],
            RecentActionIds = [SpotlightActionIds.Password, "secret input", SpotlightActionIds.Password]
        }.Normalize();
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uuid }, preferences.FavoriteActionIds);
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.Password }, preferences.RecentActionIds);
        TestAssert.Equal(preferences, preferences.Normalize());
    }

    internal static void FavoritesToggleWithoutChangingHistoryOrOriginalArrays()
    {
        var original = new SpotlightPreferences { RecentActionIds = [SpotlightActionIds.Password] };
        var favorite = original.ToggleFavorite(SpotlightActionIds.Uuid).ToggleFavorite(SpotlightActionIds.Password);
        var removed = favorite.ToggleFavorite(SpotlightActionIds.Uuid);
        TestAssert.Empty(original.FavoriteActionIds);
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.Uuid, SpotlightActionIds.Password }, favorite.FavoriteActionIds);
        TestAssert.SequenceEqual(new[] { SpotlightActionIds.Password }, removed.FavoriteActionIds);
        TestAssert.SequenceEqual(original.RecentActionIds, removed.RecentActionIds);
    }

    internal static void RecentCommandsAreUniqueBoundedAndMostRecentFirst()
    {
        var preferences = new SpotlightPreferences();
        foreach (var id in SpotlightShortcuts.SupportedActionIds)
        {
            preferences = preferences.RecordSuccess(id);
        }

        TestAssert.Equal(SpotlightPreferences.MaximumRecentActions, preferences.RecentActionIds.Length);
        TestAssert.SequenceEqual(SpotlightShortcuts.SupportedActionIds.Reverse().Take(SpotlightPreferences.MaximumRecentActions), preferences.RecentActionIds);
        var oldest = preferences.RecentActionIds.Last();
        var updated = preferences.RecordSuccess(oldest);
        TestAssert.Equal(oldest, updated.RecentActionIds[0]);
        TestAssert.Equal(SpotlightPreferences.MaximumRecentActions, updated.RecentActionIds.Distinct().Count());
        TestAssert.Equal(oldest, preferences.RecentActionIds.Last());
        var oversized = new SpotlightPreferences { RecentActionIds = SpotlightShortcuts.SupportedActionIds.ToArray() }.Normalize();
        TestAssert.Equal(SpotlightPreferences.MaximumRecentActions, oversized.RecentActionIds.Length);
    }

    internal static void DisablingAndClearingHistoryPreserveFavorites()
    {
        var preferences = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.Uuid],
            RecentActionIds = [SpotlightActionIds.Password]
        };
        TestAssert.Empty(preferences.ClearHistory().RecentActionIds);
        TestAssert.SequenceEqual(preferences.FavoriteActionIds, preferences.ClearHistory().FavoriteActionIds);
        var disabled = (preferences with { RememberRecentCommands = false }).RecordSuccess(SpotlightActionIds.Uuid);
        TestAssert.Empty(disabled.RecentActionIds);
        TestAssert.SequenceEqual(preferences.FavoriteActionIds, disabled.FavoriteActionIds);
        TestAssert.Equal(1, preferences.RecentActionIds.Length);
    }

    internal static void PreferenceChangesRejectInvalidCommandIds()
    {
        foreach (var id in new[] { null, "", " ", "missing-tool", "secret value" })
        {
            ExpectArgumentException(() => new SpotlightPreferences().ToggleFavorite(id!));
            ExpectArgumentException(() => new SpotlightPreferences().RecordSuccess(id!));
        }
    }

    internal static void PreferencesSerializeOnlyIdentifiersAndHistoryChoice()
    {
        var preferences = new SpotlightPreferences().ToggleFavorite(SpotlightActionIds.Password).RecordSuccess(SpotlightActionIds.Uuid);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(preferences));
        TestAssert.SequenceEqual(new[] { "RememberRecentCommands", "FavoriteActionIds", "RecentActionIds" }, document.RootElement.EnumerateObject().Select(property => property.Name));
        var restored = JsonSerializer.Deserialize<SpotlightPreferences>(document.RootElement.GetRawText())!.Normalize();
        TestAssert.SequenceEqual(preferences.FavoriteActionIds, restored.FavoriteActionIds);
        TestAssert.SequenceEqual(preferences.RecentActionIds, restored.RecentActionIds);
    }

    private static void ExpectArgumentException(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected an invalid command ID to be rejected.");
    }
}
