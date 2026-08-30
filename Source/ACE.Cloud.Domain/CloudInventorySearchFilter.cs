namespace ACE.Cloud.Domain;

/// <summary>
/// One typed, already-parsed inventory search query (SRCH-001 Green: "typed query parsing ... without
/// string-built SQL"). Every field is a plain typed bound or pattern; there is no path from a
/// caller-supplied string to a SQL fragment anywhere in this type or its consumers
/// (<see cref="CloudInventorySearchEngine"/> only ever compares these values against already-fetched,
/// already-authorized candidate rows in memory, and
/// <see cref="ACE.Cloud.Persistence.CloudInventorySearchReader"/> only ever passes them to EF Core as
/// ordinary parameterized predicates).
/// </summary>
public sealed record CloudInventorySearchFilter
{
    public CloudInventoryCategory? Category { get; init; }

    /// <summary>Normal search: case-insensitive substring match against an item's name.</summary>
    public string? NameContains { get; init; }

    /// <summary>
    /// Property search: inclusive numeric bounds. An item with no recorded Value/Burden never
    /// satisfies a Value/Burden bound (matching <see cref="CloudInventoryItemOrderPolicy"/>'s "a null
    /// value always sorts after every present value" treatment of missing properties as absent, not
    /// zero).
    /// </summary>
    public int? MinValue { get; init; }

    public int? MaxValue { get; init; }

    public int? MinBurden { get; init; }

    public int? MaxBurden { get; init; }

    public int? MinQuantity { get; init; }

    public int? MaxQuantity { get; init; }

    /// <summary>
    /// An advanced, explicitly opted-in Safe Regex Search pattern matched against an item's name.
    /// Null/empty means Safe Regex Search never runs for this query, regardless of
    /// <see cref="CloudSearchConfiguration.RegexSearchEnabled"/> (Progressive Interface: advanced
    /// capability appears only through direct opt-in, never as a permanently active mode).
    /// </summary>
    public string? RegexPattern { get; init; }

    /// <summary>1-based Mule Page number.</summary>
    public int Page { get; init; } = 1;

    public CloudInventorySortKey SortKey { get; init; } = CloudInventorySortKey.Name;

    public CloudInventorySortDirection SortDirection { get; init; } = CloudInventorySortDirection.Ascending;
}
