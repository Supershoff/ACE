namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of a raw Pyreal Remainder withdrawal (DEP-006): the delivered coin-stack
/// biota GUIDs, the recipient container, and the account's exact new Pyreal Remainder.
/// </summary>
public sealed record CloudPyrealRemainderWithdrawalResult(IReadOnlyList<uint> DeliveredBiotaIds, uint RecipientContainerId, long NewRemainder);
