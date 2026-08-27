using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned envelope a committed Cloud boundary mutation's Activity Ledger entry or Custody
/// Outbox entry travels in (EVT-001, EVT-002, ARCH-007): shard ID, the resulting authoritative
/// aggregate version, the correlation/idempotency ID that produced it, and its committed database
/// time. This envelope carries no <see cref="ICloudPublicContract"/> marker; its payload may
/// contain administrator-only or audit-only detail and must never be forwarded to an unauthorized
/// or public surface unmodified.
/// </summary>
public sealed record CloudEventEnvelope<TPayload>
{
    public CloudShardId ShardId { get; }

    public CloudAggregateVersion Version { get; }

    public CloudIdempotencyKey CorrelationId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public TPayload Payload { get; }

    public CloudEventEnvelope(
        CloudShardId shardId,
        CloudAggregateVersion version,
        CloudIdempotencyKey correlationId,
        DateTimeOffset occurredAtUtc,
        TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(payload);

        ShardId = shardId;
        Version = version;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
        Payload = payload;
    }
}
