namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of a withdrawal handoff: which native biota was returned to world
/// possession, into which recipient container, and which Cloud owner it was withdrawn from.
/// </summary>
public sealed record CloudWithdrawalResult(uint BiotaId, uint RecipientContainerId, Guid FormerOwnerId);
