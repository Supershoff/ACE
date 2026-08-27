namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of a Cloud Stack Lot withdrawal: which native biota was actually delivered
/// (the original backing biota for a full-stack withdrawal, or a materialized child for a partial
/// one), into which recipient container, for how much quantity, and from which owner.
/// </summary>
public sealed record CloudStackWithdrawalResult(uint DeliveredBiotaId, uint RecipientContainerId, Guid FormerOwnerId, int Quantity);
