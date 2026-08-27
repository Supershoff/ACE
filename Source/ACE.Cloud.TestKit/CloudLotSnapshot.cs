namespace ACE.Cloud.TestKit;

/// <summary>
/// A point-in-time read of one Cloud Stack Lot, as reported by
/// <see cref="ICloudLotConservationHarness{TLotId, TOwnerId}.GetLotsAsync"/>.
/// </summary>
public sealed record CloudLotSnapshot<TLotId, TOwnerId>(TLotId Id, int Version, TOwnerId OwnerId, int Quantity);
