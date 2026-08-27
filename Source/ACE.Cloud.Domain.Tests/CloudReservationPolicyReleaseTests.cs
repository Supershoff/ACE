namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Table-driven legal/illegal coverage for <see cref="CloudReservationPolicy.Release"/> (issue #7
/// Red section): only the reservation's own owning workflow may release it, a stale expected version
/// is a conflict, a reservation cannot be released twice, and an expired reservation can only be
/// released as Expired -- never silently fulfilled.
/// </summary>
[TestClass]
public sealed class CloudReservationPolicyReleaseTests
{
    private static readonly CloudReservationId ReservationId = new(Guid.NewGuid());
    private static readonly CloudAccountId OwnerId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static CloudReservation NewActiveReservation(CloudReservationKind kind, DateTimeOffset? expiresAtUtc = null) =>
        new(ReservationId, kind, OwnerId, CreatedAt, expiresAtUtc);

    [TestMethod]
    public void Release_ByItsOwningWorkflow_Succeeds()
    {
        var reservation = NewActiveReservation(CloudReservationKind.Withdrawal);

        var result = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Withdrawal, CloudAggregateVersion.Initial,
            CreatedAt.AddMinutes(1), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudReservationStatus.Released, result.Reservation!.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Fulfilled, result.Reservation.ReleaseReason);
        Assert.AreEqual(CloudAggregateVersion.Initial.Next(), result.Reservation.Version);
    }

    [TestMethod]
    [DataRow(CloudReservationKind.Listing)]
    [DataRow(CloudReservationKind.Offer)]
    [DataRow(CloudReservationKind.BidEscrow)]
    public void Release_ByAnyWorkflowOtherThanItsOwnKind_IsRejected(CloudReservationKind otherWorkflow)
    {
        var reservation = NewActiveReservation(CloudReservationKind.Withdrawal);

        var result = CloudReservationPolicy.Release(
            reservation, otherWorkflow, CloudAggregateVersion.Initial,
            CreatedAt.AddMinutes(1), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.WrongReleasingWorkflow, result.ErrorKind);
    }

    [TestMethod]
    public void Release_WithAStaleExpectedVersion_IsRejectedAsAConflict()
    {
        var reservation = NewActiveReservation(CloudReservationKind.Listing);
        var staleVersion = new CloudAggregateVersion(7);

        var result = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Listing, staleVersion,
            CreatedAt.AddMinutes(1), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.VersionConflict, result.ErrorKind);
    }

    [TestMethod]
    public void Release_ATwiceReleasedReservation_IsRejected()
    {
        var reservation = NewActiveReservation(CloudReservationKind.Offer);
        var firstRelease = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Offer, CloudAggregateVersion.Initial,
            CreatedAt.AddMinutes(1), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);
        Assert.IsTrue(firstRelease.IsSuccess);

        var secondRelease = CloudReservationPolicy.Release(
            firstRelease.Reservation!, CloudReservationKind.Offer, firstRelease.Reservation!.Version,
            CreatedAt.AddMinutes(2), CloudReservationReleaseReason.Cancelled, CloudMutationGateState.Open);

        Assert.IsFalse(secondRelease.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.AlreadyReleased, secondRelease.ErrorKind);
    }

    [TestMethod]
    public void Release_FulfillingAnExpiredReservation_IsRejected()
    {
        var expiresAt = CreatedAt.AddMinutes(15);
        var reservation = NewActiveReservation(CloudReservationKind.Withdrawal, expiresAt);

        var result = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Withdrawal, CloudAggregateVersion.Initial,
            expiresAt.AddSeconds(1), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.CannotFulfillExpiredReservation, result.ErrorKind);
    }

    [TestMethod]
    [DataRow(CloudReservationReleaseReason.Expired)]
    [DataRow(CloudReservationReleaseReason.Cancelled)]
    [DataRow(CloudReservationReleaseReason.AdminIntervention)]
    public void Release_AnExpiredReservationForAnyNonFulfillReason_Succeeds(CloudReservationReleaseReason reason)
    {
        var expiresAt = CreatedAt.AddMinutes(15);
        var reservation = NewActiveReservation(CloudReservationKind.Withdrawal, expiresAt);

        var result = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Withdrawal, CloudAggregateVersion.Initial,
            expiresAt.AddSeconds(1), reason, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(reason, result.Reservation!.ReleaseReason);
    }

    [TestMethod]
    public void Release_WhileMutationsAreFrozen_IsRejectedRegardlessOfReservationState()
    {
        var reservation = NewActiveReservation(CloudReservationKind.Withdrawal);

        var result = CloudReservationPolicy.Release(
            reservation, CloudReservationKind.Withdrawal, CloudAggregateVersion.Initial,
            CreatedAt.AddMinutes(1), CloudReservationReleaseReason.Fulfilled, CloudMutationGateState.Frozen);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.MutationsFrozen, result.ErrorKind);
    }
}
