namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of a stack deposit: the stack Cloud Custody Record created and the single
/// Cloud Stack Lot that initially claims its entire quantity for the depositing owner.
/// </summary>
public sealed record CloudStackDepositResult(CloudCustodyRecord CustodyRecord, CloudStackLot Lot);
