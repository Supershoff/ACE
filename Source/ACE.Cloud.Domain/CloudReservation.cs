namespace ACE.Cloud.Domain;

/// <summary>
/// One exclusive Cloud reservation: a Withdrawal Reservation, Listing Reservation, Transfer Offer
/// hold, or Bid Escrow allocation (IMPLEMENTATION-BRIEF.md's core custody state model). Immutable;
/// every state transition (<see cref="CloudReservationPolicy.Release"/>) returns a new instance
/// carrying the next <see cref="Version"/> rather than mutating this one, so a caller can never
/// observe a half-applied transition (ARCH-006, transaction rule 3).
/// </summary>
public sealed class CloudReservation
{
    public CloudReservationId Id { get; }

    public CloudReservationKind Kind { get; }

    public CloudAccountId OwnerId { get; }

    public CloudReservationStatus Status { get; }

    public CloudAggregateVersion Version { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// The database-time deadline after which this reservation may no longer be fulfilled
    /// (<see cref="CloudReservationReleaseReason.Fulfilled"/>); null for a reservation with no fixed
    /// lifetime. Expiry never ends a reservation by itself -- only an explicit
    /// <see cref="CloudReservationPolicy.Release"/> by its owning workflow does (Green section:
    /// "explicit workflow-owned release commands").
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; }

    public DateTimeOffset? ReleasedAtUtc { get; }

    public CloudReservationReleaseReason? ReleaseReason { get; }

    public CloudReservation(
        CloudReservationId id,
        CloudReservationKind kind,
        CloudAccountId ownerId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc)
        : this(
            id, kind, ownerId, CloudReservationStatus.Active, CloudAggregateVersion.Initial,
            createdAtUtc, expiresAtUtc, releasedAtUtc: null, releaseReason: null)
    {
        if (expiresAtUtc is not null && expiresAtUtc.Value <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A reservation's expiry must be after its creation time.");
        }
    }

    private CloudReservation(
        CloudReservationId id,
        CloudReservationKind kind,
        CloudAccountId ownerId,
        CloudReservationStatus status,
        CloudAggregateVersion version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? releasedAtUtc,
        CloudReservationReleaseReason? releaseReason)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(version);

        Id = id;
        Kind = kind;
        OwnerId = ownerId;
        Status = status;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ReleasedAtUtc = releasedAtUtc;
        ReleaseReason = releaseReason;
    }

    /// <summary>
    /// True when <paramref name="nowUtc"/> is at or past <see cref="ExpiresAtUtc"/>. A caller must
    /// still explicitly release an expired-but-still-<see cref="CloudReservationStatus.Active"/>
    /// reservation with reason <see cref="CloudReservationReleaseReason.Expired"/>; expiry alone
    /// never changes <see cref="Status"/>.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset nowUtc) => ExpiresAtUtc is not null && nowUtc >= ExpiresAtUtc.Value;

    internal CloudReservation Released(DateTimeOffset releasedAtUtc, CloudReservationReleaseReason reason) =>
        new(Id, Kind, OwnerId, CloudReservationStatus.Released, Version.Next(), CreatedAtUtc, ExpiresAtUtc, releasedAtUtc, reason);
}
