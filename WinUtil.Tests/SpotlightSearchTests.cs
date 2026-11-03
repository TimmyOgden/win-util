using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.Tests;

internal static class SpotlightSearchTests
{
    internal static void EmptyQueriesShowFavoritesThenUniqueRecentsOrAllTools()
    {
        var actions = SpotlightActionCatalog.All;
        var preferences = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.Password, SpotlightActionIds.Uuid],
            RecentActionIds = [SpotlightActionIds.Uuid, SpotlightActionIds.FormatJson]
        };
        foreach (var query in new[] { null, "", "   " })
        {
            TestAssert.SequenceEqual(new[] { SpotlightActionIds.Password, SpotlightActionIds.Uuid, SpotlightActionIds.FormatJson },
                SpotlightSearch.Rank(actions, query, preferences).Select(action => action.Id));
            TestAssert.SequenceEqual(actions, SpotlightSearch.Rank(actions, query));
        }

        TestAssert.Empty(SpotlightSearch.Rank([], null, preferences));
        TestAssert.SequenceEqual(actions, SpotlightSearch.Rank(actions, "", new SpotlightPreferences { FavoriteActionIds = ["missing"] }));
    }

    internal static void ExactPrefixSubstringSubsequenceAndTypoMatchesHaveStableOrder()
    {
        SpotlightAction[] actions = [Tool("typo", "fornat"), Tool("subsequence", "f_o_r_m_a_t"), Tool("substring", "json format"), Tool("prefix", "format json"), Tool("exact", "format")];
        TestAssert.SequenceEqual(new[] { "exact", "prefix", "substring", "subsequence", "typo" }, SpotlightSearch.Rank(actions, " FORMAT ").Select(action => action.Id));
        TestAssert.Equal(SpotlightActionIds.Password, SpotlightSearch.Rank(SpotlightActionCatalog.All, "passwrod").First().Id);
        TestAssert.Equal(SpotlightActionIds.Uuid, SpotlightSearch.Rank(SpotlightActionCatalog.All, "gudi").First().Id);
    }

    internal static void FavoritesAndRecencyBreakOnlyEquivalentMatchRanks()
    {
        SpotlightAction[] actions = [Tool(SpotlightActionIds.Uuid, "format"), Tool(SpotlightActionIds.Password, "format"), Tool(SpotlightActionIds.JwtSecret, "format"), Tool(SpotlightActionIds.FormatJson, "format json")];
        var preferences = new SpotlightPreferences
        {
            FavoriteActionIds = [SpotlightActionIds.FormatJson, SpotlightActionIds.Password],
            RecentActionIds = [SpotlightActionIds.JwtSecret]
        };
        var expected = new[] { SpotlightActionIds.Password, SpotlightActionIds.JwtSecret, SpotlightActionIds.Uuid, SpotlightActionIds.FormatJson };
        TestAssert.SequenceEqual(expected, SpotlightSearch.Rank(actions, "format", preferences).Select(action => action.Id));
        TestAssert.SequenceEqual(expected, SpotlightSearch.Rank(actions, "format", preferences).Select(action => action.Id));
        TestAssert.SequenceEqual(actions, SpotlightSearch.Rank(actions, "format"));
    }

    internal static void SearchBoundsQueriesAndAvoidsFuzzyNoiseForShortInput()
    {
        var maximum = new string('z', SpotlightSearch.MaximumQueryLength);
        SpotlightAction[] actions = [Tool("maximum", maximum), Tool("short", "axbxc")];
        TestAssert.Equal("maximum", TestAssert.Single(SpotlightSearch.Rank(actions, maximum)).Id);
        TestAssert.Empty(SpotlightSearch.Rank(actions, maximum + "z"));
        TestAssert.Empty(SpotlightSearch.Rank(actions, "abc"));
        TestAssert.Empty(SpotlightSearch.Rank(actions, "unrelated phrase"));
        try { SpotlightSearch.Rank(null!, "query"); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("Expected null actions to be rejected.");
    }

    private static SpotlightAction Tool(string id, string title) => SpotlightAction.CreateGenerator(id, "Test", title, "", [], () => "result");
}
