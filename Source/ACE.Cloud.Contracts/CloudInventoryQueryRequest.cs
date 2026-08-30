using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The one filter/sort/page contract issue #30's Green section asks grid and spreadsheet clients to
/// share. <see cref="Category"/> is required for a grid client (one Mule Page belongs to exactly one
/// category) and left null by a spreadsheet client browsing every category at once; either way the
/// same <see cref="Page"/>/<see cref="SortKey"/>/<see cref="SortDirection"/> facets apply. This type
/// deliberately does not carry a target owner or viewer identity: which inventory to query and who is
/// asking are trust decisions the server resolves from the authenticated session, never from a
/// client-supplied field (security baseline: "Authorization is server-side on every object query").
/// </summary>
public sealed record CloudInventoryQueryRequest
{
    public CloudInventoryCategory? Category { get; init; }

    /// <summary>1-based Mule Page number.</summary>
    public int Page { get; init; } = 1;

    public CloudInventorySortKey SortKey { get; init; } = CloudInventorySortKey.Name;

    public CloudInventorySortDirection SortDirection { get; init; } = CloudInventorySortDirection.Ascending;
}
