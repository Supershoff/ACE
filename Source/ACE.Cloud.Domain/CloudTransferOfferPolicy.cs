namespace ACE.Cloud.Domain;

/// <summary>
/// Pure state-machine rules for creating and resolving a Transfer Offer (XFER-001, XFER-002,
/// INV-002, INV-004..006). Every method here is a pure function over its inputs, matching every
/// other Cloud policy in this namespace (<see cref="CloudReservationPolicy"/>,
/// <see cref="CloudAccountLinkPolicy"/>): it never queries or mutates a database itself, so the same
/// rule runs identically wherever it is called from.
/// </summary>
public static class CloudTransferOfferPolicy
{
    /// <summary>
    /// Evaluates one Transfer Offer creation attempt and, on success, opens its backing
    /// <see cref="CloudReservationKind.Offer"/> reservation over every requested target atomically --
    /// all of them or none (the same "all-or-none multi-asset transitions" guarantee
    /// <see cref="CloudReservationPolicy.Open"/> already provides, reused rather than duplicated).
    /// Checks run in a fixed precedence so retrying an identical, still-illegal request always
    /// reports the same exact reason: the mutation gate first, then the request's own shape
    /// (empty/duplicate targets), then the character-resolution facts (unknown/cross-shard/self
    /// recipient) that make the request nonsensical independent of any reservation state, then the
    /// recipient's Storage Quota, and finally target exclusivity.
    /// </summary>
    public static CloudTransferOfferCreateResult Create(
        CloudTransferOfferId offerId,
        CloudReservationId reservationId,
        DateTimeOffset nowUtc,
        TimeSpan timeToLive,
        CloudTransferOfferCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(offerId);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(request);

        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "A Transfer Offer's time-to-live must be positive.");
        }

        if (request.MutationGateState == CloudMutationGateState.Frozen)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (request.Targets.Count == 0)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.EmptyRequest, "A Transfer Offer requires at least one target.");
        }

        if (request.Targets.Distinct().Count() != request.Targets.Count)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.DuplicateTargetsInRequest,
                "A single Transfer Offer cannot name the same item or quantity more than once.");
        }

        if (!request.RecipientCharacterFound || request.RecipientAccountId is null)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.UnknownRecipientCharacter,
                "No current character matching the typed recipient name could be resolved.");
        }

        if (request.RecipientIsCrossShard)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.CrossShardRecipient,
                "The resolved recipient belongs to a different Cloud Shard; Transfer Offers never cross shards.");
        }

        if (request.RecipientAccountId == request.SenderAccountId)
        {
            return CloudTransferOfferCreateResult.Failure(
                CloudTransferOfferRejectionCode.SelfRecipient, "A Transfer Offer cannot be sent to the sender's own ownership group.");
        }

        var quota = CloudStorageQuotaPolicy.CheckNewObligation(
            request.RecipientQuotaLimit, request.RecipientCurrentProjectedCount, request.Targets.Count);
        if (!quota.IsSuccess)
        {
            return CloudTransferOfferCreateResult.Failure(CloudTransferOfferRejectionCode.RecipientOverQuota, quota.Reason!);
        }

        var reservationResult = CloudReservationPolicy.Open(
            reservationId,
            CloudReservationKind.Offer,
            request.SenderAccountId,
            request.Targets,
            request.ExistingAllocationsByTarget,
            nowUtc,
            request.MutationGateState,
            timeToLive);

        if (!reservationResult.IsSuccess)
        {
            var rejectionCode = reservationResult.ErrorKind switch
            {
                CloudCustodyTransitionErrorKind.DuplicateTargetsInRequest => CloudTransferOfferRejectionCode.DuplicateTargetsInRequest,
                CloudCustodyTransitionErrorKind.TargetAlreadyReserved => CloudTransferOfferRejectionCode.TargetAlreadyReserved,
                CloudCustodyTransitionErrorKind.MutationsFrozen => CloudTransferOfferRejectionCode.MutationsFrozen,
                _ => CloudTransferOfferRejectionCode.EmptyRequest,
            };

            return CloudTransferOfferCreateResult.Failure(rejectionCode, reservationResult.Reason!);
        }

        var offer = new CloudTransferOffer(
            offerId, request.SenderAccountId, request.RecipientAccountId, reservationId, nowUtc, nowUtc + timeToLive);

        return CloudTransferOfferCreateResult.Success(offer, reservationResult.Reservation!, reservationResult.Allocations);
    }

    /// <summary>
    /// The recipient accepts: the only transition that fulfills the offer rather than merely
    /// releasing its reservation, so (unlike <see cref="Cancel"/>/<see cref="Decline"/>) it also
    /// refuses an offer that has already passed its deadline even if nothing has swept it yet
    /// (<see cref="CloudTransferOffer.IsExpiredAt"/>) -- mirroring
    /// <see cref="CloudReservationPolicy.Release"/>'s own "cannot fulfill an expired reservation" rule.
    /// </summary>
    public static CloudTransferOfferCommandResult Accept(
        CloudTransferOffer offer,
        CloudAccountId actingAccountId,
        CloudAggregateVersion expectedVersion,
        DateTimeOffset nowUtc,
        CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return Resolve(
            offer, actingAccountId, offer.RecipientAccountId, expectedVersion, nowUtc, gateState,
            CloudTransferOfferStatus.Accepted, checkExpiry: true);
    }

    /// <summary>The recipient declines: releases the reservation back to the sender.</summary>
    public static CloudTransferOfferCommandResult Decline(
        CloudTransferOffer offer,
        CloudAccountId actingAccountId,
        CloudAggregateVersion expectedVersion,
        DateTimeOffset nowUtc,
        CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return Resolve(
            offer, actingAccountId, offer.RecipientAccountId, expectedVersion, nowUtc, gateState,
            CloudTransferOfferStatus.Declined, checkExpiry: false);
    }

    /// <summary>The sender cancels before acceptance: releases the reservation back to the sender.</summary>
    public static CloudTransferOfferCommandResult Cancel(
        CloudTransferOffer offer,
        CloudAccountId actingAccountId,
        CloudAggregateVersion expectedVersion,
        DateTimeOffset nowUtc,
        CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return Resolve(
            offer, actingAccountId, offer.SenderAccountId, expectedVersion, nowUtc, gateState,
            CloudTransferOfferStatus.Cancelled, checkExpiry: false);
    }

    /// <summary>
    /// The database-time expiry worker's own transition (XFER-002: "expires after seven days"): no
    /// acting account, since nobody authorizes it -- database time passing is the only precondition.
    /// Throws if <paramref name="nowUtc"/> has not actually reached <see cref="CloudTransferOffer.ExpiresAtUtc"/>
    /// yet, which would mean the worker's own selection query is broken rather than a legitimate
    /// domain rejection a caller could retry past.
    /// </summary>
    public static CloudTransferOfferCommandResult Expire(
        CloudTransferOffer offer, CloudAggregateVersion expectedVersion, DateTimeOffset nowUtc, CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (offer.Status != CloudTransferOfferStatus.Pending)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.NotPending,
                $"Transfer Offer {offer.Id} is already {offer.Status} and cannot be expired again.");
        }

        if (offer.Version != expectedVersion)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.VersionConflict,
                $"Transfer Offer {offer.Id} is at version {offer.Version}, not the expected version {expectedVersion}.");
        }

        if (!offer.IsExpiredAt(nowUtc))
        {
            throw new InvalidOperationException(
                $"Transfer Offer {offer.Id} has not yet reached its expiry of {offer.ExpiresAtUtc:O} at {nowUtc:O}.");
        }

        return CloudTransferOfferCommandResult.Success(offer.Resolved(CloudTransferOfferStatus.Expired, nowUtc));
    }

    /// <summary>
    /// Shifts a still-<see cref="CloudTransferOfferStatus.Pending"/> offer's expiry forward by exactly
    /// <paramref name="frozenDuration"/> (ADM-004: "resume by shifting deadlines exactly"), the same
    /// database-time span <see cref="CloudGlobalMaintenancePolicy.Exit"/> reports Global Cloud
    /// Maintenance was frozen for. Never cancels or otherwise changes the offer.
    /// </summary>
    public static CloudTransferOffer ShiftExpiry(CloudTransferOffer offer, TimeSpan frozenDuration)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (frozenDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frozenDuration), "A frozen-duration expiry shift must be positive.");
        }

        return offer.WithShiftedExpiry(frozenDuration);
    }

    private static CloudTransferOfferCommandResult Resolve(
        CloudTransferOffer offer,
        CloudAccountId actingAccountId,
        CloudAccountId requiredActorAccountId,
        CloudAggregateVersion expectedVersion,
        DateTimeOffset nowUtc,
        CloudMutationGateState gateState,
        CloudTransferOfferStatus targetStatus,
        bool checkExpiry)
    {
        ArgumentNullException.ThrowIfNull(actingAccountId);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (actingAccountId != requiredActorAccountId)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.NotAuthorized,
                $"Transfer Offer {offer.Id} cannot be resolved by an account that is neither its sender nor its recipient.");
        }

        if (offer.Status != CloudTransferOfferStatus.Pending)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.NotPending,
                $"Transfer Offer {offer.Id} is already {offer.Status} and cannot be resolved again.");
        }

        if (offer.Version != expectedVersion)
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.VersionConflict,
                $"Transfer Offer {offer.Id} is at version {offer.Version}, not the expected version {expectedVersion}.");
        }

        if (checkExpiry && offer.IsExpiredAt(nowUtc))
        {
            return CloudTransferOfferCommandResult.Failure(
                CloudTransferOfferRejectionCode.AlreadyExpired,
                $"Transfer Offer {offer.Id} expired at {offer.ExpiresAtUtc:O} and can no longer be accepted.");
        }

        return CloudTransferOfferCommandResult.Success(offer.Resolved(targetStatus, nowUtc));
    }
}
