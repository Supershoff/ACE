namespace ACE.Cloud.Domain;

/// <summary>
/// Deterministic multi-target lock ordering for a Cloud reservation or ownership transaction
/// (transaction rule 2: "Lock custody/lot rows in deterministic order for multi-item transactions to
/// avoid deadlocks"). Two concurrent transactions that both touch an overlapping target set always
/// acquire their row locks in the same relative order regardless of the order the caller originally
/// listed them in, so they cannot deadlock against each other. This generalizes the ad hoc ordinal
/// GUID-string ordering already used by ACE.Cloud.Persistence's
/// <c>CloudStackLotTransactionAuthority</c> lot-merge lock order into a single reusable,
/// independently testable rule.
/// </summary>
public static class CloudReservationTargetOrdering
{
    public static IReadOnlyList<CloudReservationTarget> Order(IEnumerable<CloudReservationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        return targets
            .Select(target => (Target: target, Key: LockKey(target)))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Target)
            .ToList();
    }

    private static string LockKey(CloudReservationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            CloudReservationTargetKind.Item => $"0:{target.ItemId!.Value:D10}",
            CloudReservationTargetKind.StackLot => $"1:{target.StackLotId!.Value:D}",
            _ => throw new ArgumentOutOfRangeException(nameof(target), "Unrecognized Cloud reservation target kind."),
        };
    }
}
