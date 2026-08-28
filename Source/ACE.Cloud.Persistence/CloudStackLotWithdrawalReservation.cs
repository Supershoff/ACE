using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// ACE's local authority record for one Withdrawal Token's exclusive Withdrawal Reservation over a
/// quantity claim against one Cloud Stack Lot (WDR-001, WDR-002, WDR-003, WDR-008, INV-002, INV-003).
/// Mirrors <see cref="CloudWithdrawalReservation"/>'s whole-item shape exactly, but targets a
/// <see cref="CloudStackLot"/> quantity instead of a whole biota -- the target CloudReservationPolicy
/// already models via <see cref="CloudReservationTarget.ForStackLot"/>.
///
/// <see cref="TokenHash"/> stores a one-way verifier of the Withdrawal Token's high-entropy secret
/// (security baseline: "store a one-way verifier if practical; compare safely"), never the secret
/// itself.
/// </summary>
public sealed class CloudStackLotWithdrawalReservation
{
    private CloudStackLotWithdrawalReservation()
    {
    }

    private CloudStackLotWithdrawalReservation(
        Guid id,
        string shardId,
        Guid lotId,
        int quantity,
        Guid ownerId,
        string tokenHash,
        Guid openIdempotencyKey,
        CloudReservationStatus status,
        CloudReservationReleaseReason? releaseReason,
        int version,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? releasedAtUtc)
    {
        Id = id;
        ShardId = shardId;
        LotId = lotId;
        Quantity = quantity;
        OwnerId = ownerId;
        TokenHash = tokenHash;
        OpenIdempotencyKey = openIdempotencyKey;
        Status = status;
        ReleaseReason = releaseReason;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ReleasedAtUtc = releasedAtUtc;
    }

    /// <summary>
    /// Opens a new active reservation. Callers must already have proved, under a lock held for the
    /// whole boundary transaction, that <paramref name="lotId"/> carries no other active reservation
    /// (<see cref="CloudReservationPolicy.Open"/> makes that decision; see
    /// <see cref="CloudCustodyBoundary.ReserveStackLotForWithdrawalAsync"/>).
    /// </summary>
    public static CloudStackLotWithdrawalReservation Open(
        string shardId, Guid lotId, int quantity, Guid ownerId, string tokenHash, Guid openIdempotencyKey,
        DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Withdrawal Reservation requires a Cloud Shard ID.", nameof(shardId));
        }

        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot Withdrawal Reservation requires a real Cloud Stack Lot ID.", nameof(lotId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A Cloud Stack Lot Withdrawal Reservation requires a positive quantity.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Withdrawal Reservation requires an owner.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A Withdrawal Reservation requires a Withdrawal Token hash.", nameof(tokenHash));
        }

        if (openIdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Withdrawal Reservation requires a non-empty idempotency key.", nameof(openIdempotencyKey));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A Withdrawal Reservation's expiry must be after its creation time.");
        }

        return new CloudStackLotWithdrawalReservation(
            Guid.NewGuid(), shardId, lotId, quantity, ownerId, tokenHash, openIdempotencyKey,
            CloudReservationStatus.Active, releaseReason: null, version: 1, createdAtUtc, expiresAtUtc, releasedAtUtc: null);
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>The Cloud Stack Lot GUID this reservation exclusively holds.</summary>
    public Guid LotId { get; private set; }

    /// <summary>The exact quantity reserved from <see cref="LotId"/> (INV-002).</summary>
    public int Quantity { get; private set; }

    public Guid OwnerId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    /// <summary>
    /// The idempotency key that opened this reservation (transaction rule 4): a repeated open
    /// request with the same key returns this same row instead of a duplicate reservation.
    /// </summary>
    public Guid OpenIdempotencyKey { get; private set; }

    public CloudReservationStatus Status { get; private set; }

    public CloudReservationReleaseReason? ReleaseReason { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }

    public bool IsExpiredAt(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Ends this reservation. Callers must already hold this row's lock for the whole boundary
    /// transaction and have validated <see cref="Status"/>/<see cref="Version"/> themselves.
    /// </summary>
    internal void Release(DateTime releasedAtUtc, CloudReservationReleaseReason reason)
    {
        if (Status != CloudReservationStatus.Active)
        {
            throw new InvalidOperationException($"Withdrawal Reservation {Id} was already released and cannot be released again.");
        }

        Status = CloudReservationStatus.Released;
        ReleaseReason = reason;
        ReleasedAtUtc = releasedAtUtc;
        Version++;
    }
}
