using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Representative Activity Ledger payload (EVT-001, EVT-002): immutable actor and owner identity,
/// the affected item, outcome, and reason. Carries no <see cref="ICloudPublicContract"/> marker;
/// ledger detail is private to the owner, allegiance members for vault history, and administrators.
/// </summary>
public sealed record CloudActivityLedgerEventPayload
{
    public CloudActorIdentity Actor { get; }

    public CloudAccountId OwnerId { get; }

    public CloudItemId ItemId { get; }

    /// <summary>The operation kind this ledger entry records, for example "Deposit" or "WithdrawalRedeemed".</summary>
    public string EventKind { get; }

    /// <summary>The operation's outcome, for example "Committed" or "Rejected".</summary>
    public string Outcome { get; }

    public string? Reason { get; }

    public CloudActivityLedgerEventPayload(
        CloudActorIdentity actor,
        CloudAccountId ownerId,
        CloudItemId itemId,
        string eventKind,
        string outcome,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(itemId);

        if (string.IsNullOrWhiteSpace(eventKind))
        {
            throw new ArgumentException("A ledger event requires an event kind.", nameof(eventKind));
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new ArgumentException("A ledger event requires an outcome.", nameof(outcome));
        }

        Actor = actor;
        OwnerId = ownerId;
        ItemId = itemId;
        EventKind = eventKind;
        Outcome = outcome;
        Reason = reason;
    }
}
