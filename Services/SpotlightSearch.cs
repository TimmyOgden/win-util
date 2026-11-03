using WinUtil.Models;

namespace WinUtil.Services;

internal static class SpotlightSearch
{
    internal const int MaximumQueryLength = 256;
    private const int MinimumFuzzyQueryLength = 4;
    private const int LongerQueryLength = 8;
    private const int ShortQueryEditDistance = 1;
    private const int LongQueryEditDistance = 2;
    private const int ExactRank = 0;
    private const int PrefixRank = 1;
    private const int SubstringRank = 2;
    private const int SubsequenceRank = 3;
    private const int TypoRank = 4;
    private const int NoMatch = int.MaxValue;

    internal static IReadOnlyList<SpotlightAction> Rank(IReadOnlyList<SpotlightAction> actions, string? query, SpotlightPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedQuery.Length > MaximumQueryLength)
        {
            return [];
        }

        var normalizedPreferences = (preferences ?? new SpotlightPreferences()).Normalize();
        if (normalizedQuery.Length == 0)
        {
            var suggestions = normalizedPreferences.FavoriteActionIds.Concat(normalizedPreferences.RecentActionIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => actions.FirstOrDefault(action => action.Id == id))
                .OfType<SpotlightAction>().ToArray();
            return suggestions.Length > 0 ? suggestions : actions.ToArray();
        }

        return actions.Select((action, index) => new
            {
                Action = action,
                Index = index,
                Rank = action.SearchTerms.Append(action.Title).Select(term => MatchRank(term, normalizedQuery)).Min(),
                FavoriteIndex = Array.IndexOf(normalizedPreferences.FavoriteActionIds, action.Id),
                RecentIndex = Array.IndexOf(normalizedPreferences.RecentActionIds, action.Id)
            })
            .Where(match => match.Rank != NoMatch)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.FavoriteIndex < 0 ? NoMatch : match.FavoriteIndex)
            .ThenBy(match => match.RecentIndex < 0 ? NoMatch : match.RecentIndex)
            .ThenBy(match => match.Index)
            .Select(match => match.Action).ToArray();
    }

    private static int MatchRank(string term, string query)
    {
        var candidate = term.ToLowerInvariant();
        if (candidate == query)
        {
            return ExactRank;
        }

        if (candidate.StartsWith(query, StringComparison.Ordinal))
        {
            return PrefixRank;
        }

        if (candidate.Contains(query, StringComparison.Ordinal))
        {
            return SubstringRank;
        }

        if (query.Length < MinimumFuzzyQueryLength)
        {
            return NoMatch;
        }

        var queryIndex = 0;
        foreach (var character in candidate)
        {
            if (character == query[queryIndex] && ++queryIndex == query.Length)
            {
                return SubsequenceRank;
            }
        }

        var maximumDistance = query.Length >= LongerQueryLength ? LongQueryEditDistance : ShortQueryEditDistance;
        return Math.Abs(candidate.Length - query.Length) <= maximumDistance && EditDistance(candidate, query) <= maximumDistance
            ? TypoRank
            : NoMatch;
    }

    private static int EditDistance(string candidate, string query)
    {
        var distances = new int[candidate.Length + 1, query.Length + 1];
        for (var row = 0; row <= candidate.Length; row++)
        {
            distances[row, 0] = row;
        }

        for (var column = 0; column <= query.Length; column++)
        {
            distances[0, column] = column;
        }

        for (var row = 1; row <= candidate.Length; row++)
        {
            for (var column = 1; column <= query.Length; column++)
            {
                var substitution = candidate[row - 1] == query[column - 1] ? 0 : 1;
                distances[row, column] = Math.Min(Math.Min(distances[row - 1, column] + 1, distances[row, column - 1] + 1),
                    distances[row - 1, column - 1] + substitution);
                if (row > 1 && column > 1 && candidate[row - 1] == query[column - 2] && candidate[row - 2] == query[column - 1])
                {
                    distances[row, column] = Math.Min(distances[row, column], distances[row - 2, column - 2] + 1);
                }
            }
        }

        return distances[candidate.Length, query.Length];
    }
}
