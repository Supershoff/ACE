namespace ACE.Cloud.TestKit;

/// <summary>
/// The minimal surface an adapter exposes so
/// <see cref="CloudLotConservationInvariantSuite{TLotId, TOwnerId}"/> can prove ARCH-010, ARCH-011,
/// INV-001, and ADR-0002's conservation invariant: a randomized sequence of split/merge/transfer
/// operations against one backing stack must never create, lose, duplicate, or over-allocate its
/// quantity.
/// </summary>
public interface ICloudLotConservationHarness<TLotId, TOwnerId>
    where TLotId : notnull
{
    /// <summary>The fixed total quantity every snapshot's lots must always sum to exactly.</summary>
    int TotalQuantity { get; }

    Task<IReadOnlyList<CloudLotSnapshot<TLotId, TOwnerId>>> GetLotsAsync();

    /// <summary>Carves <paramref name="quantity"/> off an existing lot into a new lot for <paramref name="newOwnerId"/>.</summary>
    Task<bool> SplitAsync(TLotId lotId, int expectedVersion, TOwnerId newOwnerId, int quantity);

    /// <summary>Merges <paramref name="mergeLotId"/>'s quantity into <paramref name="keepLotId"/>.</summary>
    Task<bool> MergeAsync(TLotId keepLotId, int expectedKeepVersion, TLotId mergeLotId, int expectedMergeVersion);

    /// <summary>Reassigns a lot to a new owner without changing its quantity.</summary>
    Task<bool> TransferAsync(TLotId lotId, int expectedVersion, TOwnerId newOwnerId);

    /// <summary>Produces a fresh owner identity distinct from any lot's current owner.</summary>
    TOwnerId NewOwnerId();
}
