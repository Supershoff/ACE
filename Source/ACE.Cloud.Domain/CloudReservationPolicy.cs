namespace ACE.Cloud.Domain;

/// <summary>
/// Pure state-machine rules for opening and releasing typed exclusive Cloud reservations (ARCH-003,
/// ARCH-006, WDR-001, MKT-007, XFER-002, transaction rules 2-9). Every method here is a pure
/// function over its inputs: it never queries or mutates a database itself, matching the Green
/// section's "pure aggregates and policies" and letting the same rule run identically wherever it is
/// called from -- the companion backend's boundary transaction or ACE's world-boundary code.
/// Cross-shard authorization is a separate, already-covered concern (ACE.Cloud.Contracts's
/// <c>CloudCommandGuard</c>, ARCH-001) and is intentionally not duplicated here.
/// </summary>
public static class CloudReservationPolicy
{
    /// <summary>
    /// Opens a new exclusive reservation over every target in <paramref name="targets"/>, or none of
    /// them (the "All-or-none multi-asset transitions are expressible without partial aggregate
    /// commits" acceptance criterion): if any requested target already carries an active allocation,
    /// the whole request is rejected and every conflicting target is reported, without granting the
    /// remaining free targets.
    /// </summary>
    public static CloudReservationResult Open(
        CloudReservationId reservationId,
        CloudReservationKind kind,
        CloudAccountId ownerId,
        IReadOnlyList<CloudReservationTarget> targets,
        IReadOnlyDictionary<CloudReservationTarget, CloudReservationAllocation> existingAllocationsByTarget,
        DateTimeOffset nowUtc,
        CloudMutationGateState gateState,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(existingAllocationsByTarget);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (targets.Count == 0)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.InvalidRequest, "A reservation requires at least one target.");
        }

        if (targets.Distinct().Count() != targets.Count)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.DuplicateTargetsInRequest,
                "A single reservation request cannot name the same item or quantity more than once.");
        }

        if (timeToLive is not null && timeToLive.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "A reservation time-to-live must be positive.");
        }

        var conflicts = targets
            .Where(target =>
                existingAllocationsByTarget.TryGetValue(target, out var allocation)
                && allocation.Status == CloudReservationStatus.Active)
            .ToList();

        if (conflicts.Count > 0)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.TargetAlreadyReserved,
                $"{conflicts.Count} of {targets.Count} requested target(s) already carry an active exclusive reservation.",
                conflicts);
        }

        var expiresAtUtc = timeToLive is null ? (DateTimeOffset?)null : nowUtc + timeToLive.Value;
        var reservation = new CloudReservation(reservationId, kind, ownerId, nowUtc, expiresAtUtc);
        var allocations = targets
            .Select(target => new CloudReservationAllocation(reservationId, target, kind, CloudReservationStatus.Active))
            .ToList();

        return CloudReservationResult.Success(reservation, allocations);
    }

    /// <summary>
    /// Ends a reservation. Only the workflow matching the reservation's own
    /// <see cref="CloudReservation.Kind"/> may release it (Red section: "only the owning workflow
    /// may release its typed reservation"); every other caller receives
    /// <see cref="CloudCustodyTransitionErrorKind.WrongReleasingWorkflow"/>. Fulfilling a reservation
    /// that has already passed its <see cref="CloudReservation.ExpiresAtUtc"/> is also rejected: an
    /// expired hold must be released as <see cref="CloudReservationReleaseReason.Expired"/> instead
    /// of silently treated as completed.
    /// </summary>
    public static CloudReservationResult Release(
        CloudReservation reservation,
        CloudReservationKind releasingWorkflow,
        CloudAggregateVersion expectedVersion,
        DateTimeOffset nowUtc,
        CloudReservationReleaseReason reason,
        CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (reservation.Kind != releasingWorkflow)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.WrongReleasingWorkflow,
                $"Cloud Reservation {reservation.Id} is a {reservation.Kind} reservation and can only be released by its owning "
                    + $"{reservation.Kind} workflow, not {releasingWorkflow}.");
        }

        if (reservation.Status != CloudReservationStatus.Active)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.AlreadyReleased,
                $"Cloud Reservation {reservation.Id} was already released ({reservation.ReleaseReason}) and cannot be released again.");
        }

        if (reservation.Version != expectedVersion)
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.VersionConflict,
                $"Cloud Reservation {reservation.Id} is at version {reservation.Version}, not the expected version {expectedVersion}.");
        }

        if (reason == CloudReservationReleaseReason.Fulfilled && reservation.IsExpiredAt(nowUtc))
        {
            return CloudReservationResult.Failure(
                CloudCustodyTransitionErrorKind.CannotFulfillExpiredReservation,
                $"Cloud Reservation {reservation.Id} expired at {reservation.ExpiresAtUtc:O} and cannot be fulfilled; "
                    + "release it as Expired instead.");
        }

        return CloudReservationResult.Success(reservation.Released(nowUtc, reason));
    }
}
