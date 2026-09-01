using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// ACE Cloud Transaction Authority's own persisted Transfer Offer aggregate (issue #35, XFER-001,
/// XFER-002): both the offer itself and its own backing exclusive <see cref="CloudReservationKind.Offer"/>
/// hold in one row, exactly mirroring <see cref="CloudWithdrawalReservation"/>'s combined
/// aggregate+reservation shape rather than materializing a separate generic reservation row --
/// <see cref="Id"/> doubles as the reservation ID <see cref="CloudTransferOfferPolicy"/> reasons
/// about. <see cref="RecipientAccountId"/> is resolved exactly once at creation from the sender's
/// typed current character name and never re-resolved (XFER-001).
/// </summary>
public sealed class CloudTransferOfferRecord
{
    private CloudTransferOfferRecord()
    {
    }

    private CloudTransferOfferRecord(
        Guid id,
        string shardId,
        Guid senderAccountId,
        Guid recipientAccountId,
        Guid createIdempotencyKey,
        CloudTransferOfferStatus status,
        int version,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? resolvedAtUtc)
    {
        Id = id;
        ShardId = shardId;
        SenderAccountId = senderAccountId;
        RecipientAccountId = recipientAccountId;
        CreateIdempotencyKey = createIdempotencyKey;
        Status = status;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ResolvedAtUtc = resolvedAtUtc;
    }

    public static CloudTransferOfferRecord Open(
        Guid id,
        string shardId,
        Guid senderAccountId,
        Guid recipientAccountId,
        Guid createIdempotencyKey,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Transfer Offer requires a Cloud Shard ID.", nameof(shardId));
        }

        if (senderAccountId == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer requires a sender.", nameof(senderAccountId));
        }

        if (recipientAccountId == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer requires a recipient.", nameof(recipientAccountId));
        }

        if (createIdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer requires a non-empty idempotency key.", nameof(createIdempotencyKey));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A Transfer Offer's expiry must be after its creation time.");
        }

        return new CloudTransferOfferRecord(
            id, shardId, senderAccountId, recipientAccountId, createIdempotencyKey,
            CloudTransferOfferStatus.Pending, version: 1, createdAtUtc, expiresAtUtc, resolvedAtUtc: null);
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid SenderAccountId { get; private set; }

    public Guid RecipientAccountId { get; private set; }

    /// <summary>The idempotency key that opened this offer (transaction rule 4).</summary>
    public Guid CreateIdempotencyKey { get; private set; }

    public CloudTransferOfferStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public bool IsExpiredAt(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Resolves this offer to a terminal <paramref name="status"/> (Accepted, Declined, Cancelled, or
    /// Expired). Callers must already hold this row's lock for the whole boundary transaction and
    /// have validated actor authorization, <see cref="Status"/>, <see cref="Version"/>, and expiry
    /// themselves via <see cref="CloudTransferOfferPolicy"/> (mirrors <c>CloudWithdrawalReservation.Release</c>'s
    /// established rationale: not literally reused because <see cref="CloudTransferOffer"/>'s own
    /// transition methods are internal to ACE.Cloud.Domain and this persisted row already carries its
    /// own authoritative version); this method only performs the already-decided state transition.
    /// </summary>
    internal void Resolve(CloudTransferOfferStatus status, DateTime resolvedAtUtc)
    {
        if (Status != CloudTransferOfferStatus.Pending)
        {
            throw new InvalidOperationException($"Transfer Offer {Id} is already {Status} and cannot be resolved again.");
        }

        if (status == CloudTransferOfferStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Resolving a Transfer Offer requires a terminal status.");
        }

        Status = status;
        ResolvedAtUtc = resolvedAtUtc;
        Version++;
    }

    /// <summary>ADM-004: shifts this still-Pending offer's expiry forward by exactly <paramref name="frozenDuration"/>.</summary>
    internal void ShiftExpiry(TimeSpan frozenDuration)
    {
        if (frozenDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frozenDuration), "A frozen-duration expiry shift must be positive.");
        }

        ExpiresAtUtc += frozenDuration;
        Version++;
    }
}
