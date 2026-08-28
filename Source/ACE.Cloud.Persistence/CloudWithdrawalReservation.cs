using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// ACE's local authority record for one Withdrawal Token's exclusive Withdrawal Reservation
/// (WDR-001, WDR-002, WDR-003). Persisting this record on the ACE side -- not only in the
/// companion web schema -- is what lets ACE validate and redeem an already-issued token entirely
/// from its own database during a web outage (WDR-008: "An already-issued Withdrawal Token remains
/// redeemable during a web outage because ACE can validate its local reservation and redemption
/// rules"). Scope: this issue covers whole-item (non-stack) reservations only; a Cloud Stack Lot
/// withdrawal reservation follows the same shape in a later issue.
///
/// <see cref="TokenHash"/> stores a one-way verifier of the Withdrawal Token's high-entropy secret
/// (security baseline: "store a one-way verifier if practical; compare safely"), never the secret
/// itself.
/// </summary>
public sealed class CloudWithdrawalReservation
{
    private CloudWithdrawalReservation()
    {
    }

    private CloudWithdrawalReservation(
        Guid id,
        string shardId,
        uint biotaId,
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
        BiotaId = biotaId;
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
    /// whole boundary transaction, that <paramref name="biotaId"/> carries no other active
    /// reservation (<see cref="CloudReservationPolicy.Open"/> makes that decision; see
    /// <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync"/>).
    /// </summary>
    public static CloudWithdrawalReservation Open(
        string shardId, uint biotaId, Guid ownerId, string tokenHash, Guid openIdempotencyKey, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Withdrawal Reservation requires a Cloud Shard ID.", nameof(shardId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A Withdrawal Reservation requires a real native biota GUID.");
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

        return new CloudWithdrawalReservation(
            Guid.NewGuid(), shardId, biotaId, ownerId, tokenHash, openIdempotencyKey,
            CloudReservationStatus.Active, releaseReason: null, version: 1, createdAtUtc, expiresAtUtc, releasedAtUtc: null);
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>The native biota GUID this reservation exclusively holds.</summary>
    public uint BiotaId { get; private set; }

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

    /// <summary>
    /// True at or after <see cref="ExpiresAtUtc"/>. Matches <c>CloudReservation.IsExpiredAt</c>:
    /// expiry alone never changes <see cref="Status"/>, an explicit release still must record it.
    /// </summary>
    public bool IsExpiredAt(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Ends this reservation. Callers must already hold this row's lock for the whole boundary
    /// transaction and have validated <see cref="Status"/>/<see cref="Version"/> themselves; this
    /// method only performs the state transition (mirrors <c>CloudReservationPolicy.Release</c>'s
    /// rules -- not literally reused because <c>CloudReservation.Released</c> is internal to
    /// ACE.Cloud.Domain and this persisted row already carries its own authoritative version).
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
