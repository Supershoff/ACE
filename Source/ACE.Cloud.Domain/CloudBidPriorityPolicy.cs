namespace ACE.Cloud.Domain;

/// <summary>
/// Deterministic Bid Priority ordering (MKT-105): higher maximums win; equal maximums favor the
/// earliest server-committed accepted maximum, using authoritative commit order rather than
/// browser-reported time. Also guards the self-dealing rule (MKT-110) that keeps a seller's own
/// Main/Linked ownership group from ever leading its own listing. Cross-shard authorization remains
/// <c>CloudCommandGuard</c>'s already-covered concern (ARCH-001) and is intentionally not
/// duplicated here, matching <see cref="CloudReservationPolicy"/>'s precedent.
/// </summary>
public static class CloudBidPriorityPolicy
{
    public static readonly IComparer<CloudBidCommitment> ByPriority = Comparer<CloudBidCommitment>.Create(Compare);

    /// <summary>Every accepted bid ordered from current leader to lowest priority (MKT-105).</summary>
    public static IReadOnlyList<CloudBidCommitment> OrderByPriority(IEnumerable<CloudBidCommitment> bids)
    {
        ArgumentNullException.ThrowIfNull(bids);
        return bids.OrderBy(b => b, ByPriority).ToList();
    }

    /// <summary>The current leading bid, or null when there are no accepted bids yet.</summary>
    public static CloudBidCommitment? DetermineLeader(IEnumerable<CloudBidCommitment> bids)
    {
        ArgumentNullException.ThrowIfNull(bids);
        return OrderByPriority(bids).FirstOrDefault();
    }

    /// <summary>
    /// True when a bid would be self-dealing (MKT-110): the bidder's resolved ownership group
    /// matches the listing seller's. Both identities must already be resolved to their Main
    /// Account (AUTH-005) before reaching this check; it does not itself resolve Linked Accounts.
    /// </summary>
    public static bool IsSelfDealing(CloudAccountId sellerAccountId, CloudAccountId bidderAccountId)
    {
        ArgumentNullException.ThrowIfNull(sellerAccountId);
        ArgumentNullException.ThrowIfNull(bidderAccountId);

        return sellerAccountId == bidderAccountId;
    }

    private static int Compare(CloudBidCommitment? x, CloudBidCommitment? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var byMax = y.MaxUnits.CompareTo(x.MaxUnits);
        return byMax != 0 ? byMax : x.CommitSequence.CompareTo(y.CommitSequence);
    }
}
