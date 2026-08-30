namespace ACE.Cloud.Domain;

/// <summary>
/// The deterministic ordering every Mule Page grid and spreadsheet view shares (UI-003: "Grid sort
/// order is deterministic, offers user-selectable sort keys, and uses stable item identity to break
/// equal-value ties"). A null <see cref="CloudInventorySortableItem.Value"/>/
/// <see cref="CloudInventorySortableItem.Burden"/> (an item ACE never recorded that property for)
/// always sorts after every present value regardless of direction, so a numeric sort's page
/// boundaries stay stable rather than having missing values jump to the front on Descending.
/// <see cref="CloudInventorySortableItem.ItemId"/> is the final, always-ascending tie-break: two
/// items that compare equal on the chosen key never depend on delivery/enumeration order to decide
/// which page they land on.
/// </summary>
public static class CloudInventoryItemOrderPolicy
{
    public static IReadOnlyList<T> Sort<T>(
        IEnumerable<T> items, CloudInventorySortKey sortKey, CloudInventorySortDirection sortDirection)
        where T : ICloudInventorySortable
    {
        ArgumentNullException.ThrowIfNull(items);

        var comparer = Comparer<T>.Create((left, right) => Compare(left, right, sortKey, sortDirection));
        return items.OrderBy(item => item, comparer).ToList();
    }

    private static int Compare(
        ICloudInventorySortable left, ICloudInventorySortable right, CloudInventorySortKey sortKey, CloudInventorySortDirection sortDirection)
    {
        var primary = sortKey switch
        {
            CloudInventorySortKey.Name => Direction(string.CompareOrdinal(left.Name, right.Name), sortDirection),
            CloudInventorySortKey.Value => CompareNullableDescendingLast(left.Value, right.Value, sortDirection),
            CloudInventorySortKey.Burden => CompareNullableDescendingLast(left.Burden, right.Burden, sortDirection),
            _ => throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, "Unrecognized sort key."),
        };

        return primary != 0 ? primary : left.ItemId.Value.CompareTo(right.ItemId.Value);
    }

    private static int Direction(int comparison, CloudInventorySortDirection sortDirection) =>
        sortDirection == CloudInventorySortDirection.Descending ? -comparison : comparison;

    /// <summary>
    /// A null value always sorts after every present value, regardless of direction; only the
    /// relative order of two present values is direction-sensitive.
    /// </summary>
    private static int CompareNullableDescendingLast(int? left, int? right, CloudInventorySortDirection sortDirection)
    {
        if (left.HasValue && right.HasValue)
        {
            return Direction(left.Value.CompareTo(right.Value), sortDirection);
        }

        if (!left.HasValue && !right.HasValue)
        {
            return 0;
        }

        return left.HasValue ? -1 : 1;
    }
}
