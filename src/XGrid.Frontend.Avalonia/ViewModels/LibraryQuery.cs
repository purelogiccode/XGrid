using System;
using System.Collections.Generic;
using System.Globalization;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Library collection filters shown in the toolbar.</summary>
public enum LibraryFilter
{
    /// <summary>Show every game.</summary>
    All,

    /// <summary>Show only favorites.</summary>
    Favorites,

    /// <summary>Show games that have been played.</summary>
    Played,

    /// <summary>Show games that have never been played.</summary>
    Unplayed,
}

/// <summary>Library sort orders shown in the toolbar.</summary>
public enum LibrarySort
{
    /// <summary>Sort by title, then path.</summary>
    Title,

    /// <summary>Sort by the most recent launch.</summary>
    RecentlyPlayed,

    /// <summary>Sort by accumulated play time.</summary>
    MostPlayed,

    /// <summary>Sort by release year, newest first.</summary>
    ReleaseYear,

    /// <summary>Sort by publisher, then title.</summary>
    Publisher,
}

/// <summary>Search, filter and sort rules shared by the library page and tests.</summary>
public static class LibraryQuery
{
    /// <summary>Returns whether an item matches the search text and collection filter.</summary>
    /// <param name="item">Library item.</param>
    /// <param name="query">Search text; empty matches everything.</param>
    /// <param name="filter">Collection filter.</param>
    /// <returns><see langword="true"/> when the item should be visible.</returns>
    public static bool Matches(GameListItem item, string? query, LibraryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!MatchesFilter(item, filter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string text = query.Trim();
        return Contains(item.Title, text)
               || Contains(item.Path, text)
               || Contains(item.Folder, text)
               || Contains(item.FormatText, text)
               || Contains(item.TitleIdText, text)
               || Contains(item.MetadataText, text)
               || Contains(item.Publisher, text)
               || Contains(item.Developer, text)
               || Contains(item.Genre, text)
               || Contains(item.Regions, text)
               || Contains(item.Year, text)
               || Contains(item.Description, text);
    }

    /// <summary>Compares two items for the requested sort order.</summary>
    /// <param name="left">Left item.</param>
    /// <param name="right">Right item.</param>
    /// <param name="sort">Sort order.</param>
    /// <returns>A negative, zero, or positive value.</returns>
    public static int Compare(GameListItem left, GameListItem right, LibrarySort sort)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return sort switch
        {
            LibrarySort.RecentlyPlayed => CompareRecentlyPlayed(left, right) is var dateOrder and not 0
                ? dateOrder
                : CompareTitles(left, right),
            LibrarySort.MostPlayed => right.PlaytimeSeconds.CompareTo(left.PlaytimeSeconds) is var timeOrder and not 0
                ? timeOrder
                : CompareTitles(left, right),
            LibrarySort.ReleaseYear => CompareYears(right.Year, left.Year) is var yearOrder and not 0
                ? yearOrder
                : CompareTitles(left, right),
            LibrarySort.Publisher => CompareText(left.Publisher, right.Publisher) is var publisherOrder and not 0
                ? publisherOrder
                : CompareTitles(left, right),
            _ => CompareTitles(left, right),
        };
    }

    /// <summary>Filters and sorts a sequence of items.</summary>
    /// <param name="items">Items to process.</param>
    /// <param name="query">Search text; empty matches everything.</param>
    /// <param name="filter">Collection filter.</param>
    /// <param name="sort">Sort order.</param>
    /// <returns>A new, sorted list.</returns>
    public static List<GameListItem> Apply(
        IEnumerable<GameListItem> items,
        string? query,
        LibraryFilter filter,
        LibrarySort sort)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new List<GameListItem>();
        foreach (GameListItem item in items)
        {
            if (Matches(item, query, filter))
            {
                result.Add(item);
            }
        }

        result.Sort((left, right) => Compare(left, right, sort));
        return result;
    }

    private static bool MatchesFilter(GameListItem item, LibraryFilter filter)
    {
        return filter switch
        {
            LibraryFilter.Favorites => item.IsFavorite,
            LibraryFilter.Played => item.PlaytimeSeconds > 0 || item.LastPlayedUtc is not null,
            LibraryFilter.Unplayed => item.PlaytimeSeconds <= 0 && item.LastPlayedUtc is null,
            _ => true,
        };
    }

    private static bool Contains(string value, string query)
    {
        return value.Length > 0 && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareTitles(GameListItem left, GameListItem right)
    {
        int title = CompareText(left.Title, right.Title);
        return title != 0 ? title : CompareText(left.Path, right.Path);
    }

    private static int CompareText(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareRecentlyPlayed(GameListItem left, GameListItem right)
    {
        DateTime? leftDate = left.LastPlayedUtc;
        DateTime? rightDate = right.LastPlayedUtc;
        if (leftDate is null && rightDate is null)
        {
            return 0;
        }

        if (leftDate is null)
        {
            return 1;
        }

        return rightDate is null ? -1 : rightDate.Value.CompareTo(leftDate.Value);
    }

    private static int CompareYears(string left, string right)
    {
        bool hasLeft = int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftYear);
        bool hasRight = int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightYear);
        if (hasLeft && hasRight)
        {
            return leftYear.CompareTo(rightYear);
        }

        if (hasLeft)
        {
            return -1;
        }

        return hasRight ? 1 : 0;
    }
}
