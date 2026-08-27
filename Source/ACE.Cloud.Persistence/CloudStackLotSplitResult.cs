namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of splitting a Cloud Stack Lot in two: the original lot with its reduced
/// remaining quantity, and the new lot carved off for (typically) a different owner.
/// </summary>
public sealed record CloudStackLotSplitResult(CloudStackLot RemainingLot, CloudStackLot NewLot);
