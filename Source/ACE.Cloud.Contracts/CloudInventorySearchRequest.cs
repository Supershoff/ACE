using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The wire request for one authorization-scoped inventory search (SRCH-001: "Normal and property
/// search run against an authorization-scoped prepared index"), mirroring
/// <see cref="CloudInventoryQueryRequest"/>'s deliberate omission of a target owner or viewer identity
/// -- which inventory to search and who is asking are trust decisions the server resolves from the
/// authenticated session (security baseline: "Authorization is server-side on every object query").
/// <see cref="RegexPattern"/> is the only advanced, explicitly opted-in field: Progressive Interface
/// keeps Safe Regex Search out of the ordinary text/property path entirely rather than always running
/// it in the background.
/// </summary>
public sealed record CloudInventorySearchRequest
{
    public CloudInventoryCategory? Category { get; init; }

    /// <summary>Normal search: case-insensitive substring match against an item's name.</summary>
    public string? NameContains { get; init; }

    /// <summary>Property search: inclusive numeric bounds.</summary>
    public int? MinValue { get; init; }

    public int? MaxValue { get; init; }

    public int? MinBurden { get; init; }

    public int? MaxBurden { get; init; }

    public int? MinQuantity { get; init; }

    public int? MaxQuantity { get; init; }

    /// <summary>An advanced, explicitly opted-in Safe Regex Search pattern matched against an item's name.</summary>
    public string? RegexPattern { get; init; }

    /// <summary>1-based Mule Page number.</summary>
    public int Page { get; init; } = 1;

    public CloudInventorySortKey SortKey { get; init; } = CloudInventorySortKey.Name;

    public CloudInventorySortDirection SortDirection { get; init; } = CloudInventorySortDirection.Ascending;
}
