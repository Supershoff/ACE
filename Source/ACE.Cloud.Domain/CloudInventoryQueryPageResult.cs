namespace ACE.Cloud.Domain;

/// <summary>
/// One requested Mule Page: either it exists (<see cref="PageExists"/> true, <see cref="Items"/>
/// non-empty) or it does not (a page past <see cref="TotalPages"/>), matching UI-002's "created or
/// removed automatically" -- there is nothing to migrate or clean up when a category shrinks below a
/// page boundary, the very next query for that page number simply reports it does not exist anymore.
/// </summary>
public sealed record CloudInventoryQueryPageResult(
    CloudInventoryCategory? Category,
    string? PageName,
    int PageNumber,
    bool PageExists,
    int TotalItemsInScope,
    int TotalPages,
    IReadOnlyList<CloudInventoryQueryResultItem> Items);
