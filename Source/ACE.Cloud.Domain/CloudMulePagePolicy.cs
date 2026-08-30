namespace ACE.Cloud.Domain;

/// <summary>
/// Deterministic Mule Page math (CONTEXT.md's "Mule Page": "a deterministic 102-item virtual page
/// within one Inventory Category"; UI-002: "Each Mule Page contains 102 items and is named
/// '[Category] Mule [n]'"). A page is never a stored/persisted container -- it is purely a function
/// of "how many items does this category currently have, deterministically sorted" -- so a category
/// gains or loses pages automatically as items are deposited/withdrawn/re-sorted (UI-002:
/// "Category pages are created or removed automatically") without any explicit create/delete
/// operation ever running, and it never varies with client viewport/device width (UI-003: pages
/// "reflow... without changing page membership" -- this type accepts no width/viewport input at
/// all, so membership structurally cannot depend on one).
/// </summary>
public static class CloudMulePagePolicy
{
    /// <summary>UI-002: "Each Mule Page contains 102 items."</summary>
    public const int PageSize = 102;

    /// <summary>
    /// The 1-based page number that owns the item at <paramref name="zeroBasedRank"/> in a
    /// category's deterministically sorted item list.
    /// </summary>
    public static int GetPageNumber(int zeroBasedRank)
    {
        if (zeroBasedRank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedRank), "An item's rank cannot be negative.");
        }

        return (zeroBasedRank / PageSize) + 1;
    }

    /// <summary>
    /// How many Mule Pages a category with <paramref name="totalItemCount"/> items currently has.
    /// Zero items means zero pages exist (issue #30 Red: "page boundaries at 0"); every additional
    /// <see cref="PageSize"/> items automatically creates exactly one more page, and removing items
    /// below a page-size boundary automatically removes the trailing page on the very next query
    /// (UI-002: "created or removed automatically") -- there is no separate state to reconcile.
    /// </summary>
    public static int GetPageCount(int totalItemCount)
    {
        if (totalItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItemCount), "A total item count cannot be negative.");
        }

        return totalItemCount == 0 ? 0 : ((totalItemCount - 1) / PageSize) + 1;
    }

    /// <summary>
    /// The zero-based index, within the returned page, of the item at <paramref name="zeroBasedRank"/>
    /// in a category's deterministically sorted item list.
    /// </summary>
    public static int GetPositionWithinPage(int zeroBasedRank)
    {
        if (zeroBasedRank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedRank), "An item's rank cannot be negative.");
        }

        return zeroBasedRank % PageSize;
    }

    /// <summary>
    /// True when <paramref name="pageNumber"/> currently contains at least one item, given
    /// <paramref name="totalItemCount"/> items in the category. A caller must check this before
    /// treating a query result as a real page: requesting page 2 of a 50-item category returns no
    /// items and this method returns false, exactly like the page never existed.
    /// </summary>
    public static bool PageExists(int pageNumber, int totalItemCount)
    {
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "A Mule Page number must be positive.");
        }

        return pageNumber <= GetPageCount(totalItemCount);
    }

    /// <summary>CONTEXT.md's exact Mule Page name format: "[Inventory Category] Mule [number]".</summary>
    public static string FormatPageName(CloudInventoryCategory category, int pageNumber)
    {
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "A Mule Page number must be positive.");
        }

        return $"[{CloudInventoryCategoryDisplayNames.GetDisplayName(category)}] Mule {pageNumber}";
    }
}
