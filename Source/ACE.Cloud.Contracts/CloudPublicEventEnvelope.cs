using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned envelope a Live State Stream update travels in (EVT-007): public marketplace
/// changes and authorized private inventory/reservation/bid/listing/offer/notification changes.
/// Optimistic UI must reconcile to <see cref="Version"/> and visibly reverse a rejected action.
/// Both this envelope and its <typeparamref name="TPayload"/> are constrained to
/// <see cref="ICloudPublicContract"/> so only shapes proven free of private account names and
/// secret-bearing material can reach this surface.
/// </summary>
public sealed record CloudPublicEventEnvelope<TPayload> : ICloudPublicContract
    where TPayload : ICloudPublicContract
{
    public CloudShardId ShardId { get; }

    public CloudAggregateVersion Version { get; }

    /// <summary>The public event kind, for example "ListingPublished" or "AuctionSettled".</summary>
    public string EventKind { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public TPayload Payload { get; }

    public CloudPublicEventEnvelope(
        CloudShardId shardId,
        CloudAggregateVersion version,
        string eventKind,
        DateTimeOffset occurredAtUtc,
        TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(eventKind))
        {
            throw new ArgumentException("A Live State Stream envelope requires an event kind.", nameof(eventKind));
        }

        ShardId = shardId;
        Version = version;
        EventKind = eventKind;
        OccurredAtUtc = occurredAtUtc;
        Payload = payload;
    }
}
