namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudReservationPolicy.Open"/> or <see cref="CloudReservationPolicy.Release"/>:
/// exactly a success carrying the resulting reservation (and, for <c>Open</c>, its new allocation
/// rows) or a failure carrying an exact <see cref="CloudCustodyTransitionErrorKind"/>.
/// </summary>
public sealed record CloudReservationResult
{
    public bool IsSuccess { get; }

    public CloudReservation? Reservation { get; }

    public IReadOnlyList<CloudReservationAllocation> Allocations { get; }

    public CloudCustodyTransitionErrorKind? ErrorKind { get; }

    public string? Reason { get; }

    public IReadOnlyList<CloudReservationTarget> ConflictingTargets { get; }

    private CloudReservationResult(
        bool isSuccess,
        CloudReservation? reservation,
        IReadOnlyList<CloudReservationAllocation> allocations,
        CloudCustodyTransitionErrorKind? errorKind,
        string? reason,
        IReadOnlyList<CloudReservationTarget> conflictingTargets)
    {
        IsSuccess = isSuccess;
        Reservation = reservation;
        Allocations = allocations;
        ErrorKind = errorKind;
        Reason = reason;
        ConflictingTargets = conflictingTargets;
    }

    public static CloudReservationResult Success(CloudReservation reservation, IReadOnlyList<CloudReservationAllocation>? allocations = null)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return new CloudReservationResult(true, reservation, allocations ?? [], null, null, []);
    }

    public static CloudReservationResult Failure(
        CloudCustodyTransitionErrorKind errorKind, string reason, IReadOnlyList<CloudReservationTarget>? conflictingTargets = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed reservation transition requires a reason.", nameof(reason));
        }

        return new CloudReservationResult(false, null, [], errorKind, reason, conflictingTargets ?? []);
    }
}
