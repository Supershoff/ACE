namespace ACE.Cloud.Domain;

/// <summary>
/// A Transfer Offer (XFER-001, XFER-002): a time-limited, revocable proposal to transfer a reserved
/// set of Cloud Items to another Main Account upon recipient acceptance. Immutable; every state
/// transition (<see cref="CloudTransferOfferPolicy"/>) returns a new instance carrying the next
/// <see cref="Version"/> rather than mutating this one (ARCH-006, transaction rule 3), mirroring
/// <see cref="CloudReservation"/>'s own shape.
///
/// <see cref="RecipientAccountId"/> is resolved exactly once, at creation, from the sender's current
/// character-name input to an immutable Main Account ID (XFER-001: "resolve a current character name
/// once to immutable recipient Main Account ID; later rename/deletion must not redirect it"). Nothing
/// on this aggregate ever re-resolves that lookup.
/// </summary>
public sealed class CloudTransferOffer
{
    public CloudTransferOfferId Id { get; }

    public CloudAccountId SenderAccountId { get; }

    public CloudAccountId RecipientAccountId { get; }

    /// <summary>The backing exclusive <see cref="CloudReservationKind.Offer"/> reservation over every offered target.</summary>
    public CloudReservationId ReservationId { get; }

    public CloudTransferOfferStatus Status { get; }

    public CloudAggregateVersion Version { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// The database-time deadline after which this offer may no longer be accepted (XFER-002: seven
    /// days). Shifted forward by <see cref="CloudTransferOfferPolicy.ShiftExpiry"/> for every whole
    /// duration Global Cloud Maintenance freezes mutations (ADM-004); expiry alone never changes
    /// <see cref="Status"/>, matching <see cref="CloudReservation.IsExpiredAt"/>'s own discipline.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? ResolvedAtUtc { get; }

    public CloudTransferOffer(
        CloudTransferOfferId id,
        CloudAccountId senderAccountId,
        CloudAccountId recipientAccountId,
        CloudReservationId reservationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
        : this(
            id, senderAccountId, recipientAccountId, reservationId, CloudTransferOfferStatus.Pending,
            CloudAggregateVersion.Initial, createdAtUtc, expiresAtUtc, resolvedAtUtc: null)
    {
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A Transfer Offer's expiry must be after its creation time.");
        }
    }

    private CloudTransferOffer(
        CloudTransferOfferId id,
        CloudAccountId senderAccountId,
        CloudAccountId recipientAccountId,
        CloudReservationId reservationId,
        CloudTransferOfferStatus status,
        CloudAggregateVersion version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? resolvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(senderAccountId);
        ArgumentNullException.ThrowIfNull(recipientAccountId);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(version);

        if (recipientAccountId == senderAccountId)
        {
            throw new ArgumentException("A Transfer Offer cannot name its sender as its own recipient.", nameof(recipientAccountId));
        }

        Id = id;
        SenderAccountId = senderAccountId;
        RecipientAccountId = recipientAccountId;
        ReservationId = reservationId;
        Status = status;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ResolvedAtUtc = resolvedAtUtc;
    }

    /// <summary>
    /// True when <paramref name="nowUtc"/> is at or past <see cref="ExpiresAtUtc"/>. A caller must
    /// still explicitly transition an expired-but-still-<see cref="CloudTransferOfferStatus.Pending"/>
    /// offer to <see cref="CloudTransferOfferStatus.Expired"/> (<see cref="CloudTransferOfferPolicy.Expire"/>);
    /// expiry alone never changes <see cref="Status"/>.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    internal CloudTransferOffer Resolved(CloudTransferOfferStatus status, DateTimeOffset resolvedAtUtc) =>
        new(Id, SenderAccountId, RecipientAccountId, ReservationId, status, Version.Next(), CreatedAtUtc, ExpiresAtUtc, resolvedAtUtc);

    internal CloudTransferOffer WithShiftedExpiry(TimeSpan frozenDuration) =>
        new(Id, SenderAccountId, RecipientAccountId, ReservationId, Status, Version.Next(), CreatedAtUtc, ExpiresAtUtc + frozenDuration, ResolvedAtUtc);
}
