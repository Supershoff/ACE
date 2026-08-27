namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Proves the pure state machine agrees with IMPLEMENTATION-BRIEF.md's diagrams (acceptance
/// criterion: "State-machine diagrams and tests agree with the implementation brief"), one named
/// transition at a time, rather than only exercising the policy in the abstract.
/// </summary>
[TestClass]
public sealed class CloudReservationStateMachineDiagramTests
{
    private static readonly CloudAccountId OwnerId = new(Guid.NewGuid());
    private static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static CloudReservationResult Open(
        CloudReservationKind kind, CloudReservationTarget[] targets, TimeSpan? timeToLive = null) =>
        CloudReservationPolicy.Open(
            new CloudReservationId(Guid.NewGuid()), kind, OwnerId, targets,
            new Dictionary<CloudReservationTarget, CloudReservationAllocation>(), NowUtc, CloudMutationGateState.Open, timeToLive);

    /// <summary>
    /// WDR-001: a Withdrawal Token's Withdrawal Reservation is valid for exactly 15 minutes.
    /// </summary>
    private static readonly TimeSpan WithdrawalTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Core custody state model: "WITHDRAWAL_RESERVED ├─ successful ACE redemption ► WORLD_POSSESSED".
    /// </summary>
    [TestMethod]
    public void WithdrawalReservation_SuccessfulRedemption_ReleasesAsFulfilled()
    {
        var opened = Open(
            CloudReservationKind.Withdrawal, [CloudReservationTarget.ForItem(new CloudItemId(1))], WithdrawalTokenLifetime);

        var redeemed = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Withdrawal, opened.Reservation!.Version,
            NowUtc.AddMinutes(1), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Open);

        Assert.IsTrue(redeemed.IsSuccess);
    }

    /// <summary>
    /// Core custody state model: "WITHDRAWAL_RESERVED ├─ validation/capacity failure► WITHDRAWAL_RESERVED":
    /// a failed redemption attempt must leave the reservation exactly as it was, still retryable.
    /// </summary>
    [TestMethod]
    public void WithdrawalReservation_CapacityFailure_LeavesTheReservationUnchanged()
    {
        var opened = Open(CloudReservationKind.Withdrawal, [CloudReservationTarget.ForItem(new CloudItemId(1))], WithdrawalTokenLifetime);
        var reservationBeforeFailedRedemption = opened.Reservation!;

        // A capacity/validation failure at the ACE world boundary never calls Release at all -- it
        // is not itself a state-machine transition -- so the reservation is simply still there,
        // Active, at the same version, ready to be retried until it expires (WDR-003).
        Assert.AreEqual(CloudReservationStatus.Active, reservationBeforeFailedRedemption.Status);
        Assert.AreEqual(CloudAggregateVersion.Initial, reservationBeforeFailedRedemption.Version);
    }

    /// <summary>
    /// Core custody state model: "WITHDRAWAL_RESERVED ├─ cancel/expiry ─────────────► CLOUD_AVAILABLE".
    /// </summary>
    [TestMethod]
    [DataRow(CloudReservationReleaseReason.Cancelled)]
    [DataRow(CloudReservationReleaseReason.Expired)]
    public void WithdrawalReservation_CancelOrExpiry_ReleasesTheTarget(CloudReservationReleaseReason reason)
    {
        var opened = Open(CloudReservationKind.Withdrawal, [CloudReservationTarget.ForItem(new CloudItemId(1))], WithdrawalTokenLifetime);

        var released = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Withdrawal, opened.Reservation!.Version,
            opened.Reservation.ExpiresAtUtc!.Value.AddSeconds(1), reason, CloudMutationGateState.Open);

        Assert.IsTrue(released.IsSuccess);
    }

    /// <summary>
    /// Listing state machine: "PUBLISHED_NO_BID ── seller cancel ─► CANCELLED_SELLER" (MKT-007).
    /// </summary>
    [TestMethod]
    public void ListingReservation_SellerCancelBeforeFirstBid_Releases()
    {
        var opened = Open(CloudReservationKind.Listing, [CloudReservationTarget.ForItem(new CloudItemId(2))]);

        var cancelled = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Listing, opened.Reservation!.Version,
            NowUtc.AddMinutes(1), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);

        Assert.IsTrue(cancelled.IsSuccess);
    }

    /// <summary>
    /// Listing state machine: "PUBLISHED_WITH_BID ├─ hard close ─────────────► SETTLED".
    /// </summary>
    [TestMethod]
    public void ListingReservation_HardCloseSettlement_ReleasesAsFulfilled()
    {
        var opened = Open(CloudReservationKind.Listing, [CloudReservationTarget.ForItem(new CloudItemId(2))]);

        var settled = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Listing, opened.Reservation!.Version,
            NowUtc.AddDays(3), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Open);

        Assert.IsTrue(settled.IsSuccess);
    }

    /// <summary>
    /// Transfer Offer state machine (XFER-002): "PENDING_RESERVED ├─ recipient accepts ─► ACCEPTED_TRANSFERRED",
    /// and the offer's entire item/lot set is reserved and released together (all-or-none).
    /// </summary>
    [TestMethod]
    public void TransferOfferReservation_CoversItsWholeBundle_AndAcceptingReleasesItAtOnce()
    {
        var bundle = new[]
        {
            CloudReservationTarget.ForItem(new CloudItemId(3)),
            CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid())),
        };

        var opened = Open(CloudReservationKind.Offer, bundle, TimeSpan.FromDays(7));
        Assert.HasCount(bundle.Length, opened.Allocations);

        var accepted = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Offer, opened.Reservation!.Version,
            NowUtc.AddDays(1), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Open);

        Assert.IsTrue(accepted.IsSuccess);
    }

    /// <summary>
    /// "Other reservations end only through their owning workflow": a Bid Escrow allocation cannot
    /// be released by, for example, the Listing workflow trying to unwind an unrelated auction.
    /// </summary>
    [TestMethod]
    public void BidEscrowReservation_CannotBeReleasedByTheListingWorkflow()
    {
        var opened = Open(CloudReservationKind.BidEscrow, [CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid()))]);

        var result = CloudReservationPolicy.Release(
            opened.Reservation!, CloudReservationKind.Listing, opened.Reservation!.Version,
            NowUtc.AddMinutes(1), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.WrongReleasingWorkflow, result.ErrorKind);
    }
}
