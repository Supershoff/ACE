using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Representative Custody Outbox payload (ARCH-007): the durable notification intent ACE commits
/// alongside a boundary mutation so the companion web can consume it idempotently and rebuild read
/// models without depending on web availability at commit time. Carries no
/// <see cref="ICloudPublicContract"/> marker; it is internal handoff material, not a public event.
/// </summary>
public sealed record CloudCustodyOutboxEventPayload
{
    public CloudItemId ItemId { get; }

    public CloudAccountId OwnerId { get; }

    /// <summary>The operation kind this outbox entry announces, for example "Deposit" or "Withdrawal".</summary>
    public string EventKind { get; }

    public CloudCustodyOutboxEventPayload(CloudItemId itemId, CloudAccountId ownerId, string eventKind)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(ownerId);

        if (string.IsNullOrWhiteSpace(eventKind))
        {
            throw new ArgumentException("An outbox event requires an event kind.", nameof(eventKind));
        }

        ItemId = itemId;
        OwnerId = ownerId;
        EventKind = eventKind;
    }
}
