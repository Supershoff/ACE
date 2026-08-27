namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Table-driven legal/illegal coverage for <see cref="CloudOwnershipTransferPolicy.Transfer"/>, the
/// core custody state model's "immediate cloud transfer" edge: a reservation-free target at its
/// expected version may change owner; an actively reserved target, a stale version, or a no-op
/// transfer must all be rejected with an exact domain error.
/// </summary>
[TestClass]
public sealed class CloudOwnershipTransferPolicyTests
{
    private static readonly CloudReservationTarget Target = CloudReservationTarget.ForItem(new CloudItemId(99));
    private static readonly CloudAccountId CurrentOwnerId = new(Guid.NewGuid());
    private static readonly CloudAccountId NewOwnerId = new(Guid.NewGuid());

    [TestMethod]
    public void Transfer_AReservationFreeTargetAtItsExpectedVersion_Succeeds()
    {
        var currentVersion = CloudAggregateVersion.Initial;

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, currentVersion, activeAllocation: null, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(NewOwnerId, result.NewOwnerId);
        Assert.AreEqual(currentVersion.Next(), result.NewVersion);
    }

    [TestMethod]
    public void Transfer_ATargetWithAnActiveReservation_IsRejected()
    {
        var currentVersion = CloudAggregateVersion.Initial;
        var activeAllocation = new CloudReservationAllocation(
            new CloudReservationId(Guid.NewGuid()), Target, CloudReservationKind.Listing, CloudReservationStatus.Active);

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, currentVersion, activeAllocation, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.TargetAlreadyReserved, result.ErrorKind);
    }

    [TestMethod]
    public void Transfer_ATargetWithAReleasedReservation_Succeeds()
    {
        var currentVersion = CloudAggregateVersion.Initial;
        var releasedAllocation = new CloudReservationAllocation(
            new CloudReservationId(Guid.NewGuid()), Target, CloudReservationKind.Listing, CloudReservationStatus.Released);

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, currentVersion, releasedAllocation, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Transfer_WithAStaleExpectedVersion_IsRejectedAsAConflict()
    {
        var currentVersion = new CloudAggregateVersion(5);
        var staleExpectedVersion = new CloudAggregateVersion(4);

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, staleExpectedVersion, activeAllocation: null, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.VersionConflict, result.ErrorKind);
    }

    [TestMethod]
    public void Transfer_ToTheSameOwner_IsRejectedAsInvalid()
    {
        var currentVersion = CloudAggregateVersion.Initial;

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, CurrentOwnerId, currentVersion, currentVersion, activeAllocation: null, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.InvalidRequest, result.ErrorKind);
    }

    [TestMethod]
    public void Transfer_WhileMutationsAreFrozen_IsRejectedEvenWhenOtherwiseLegal()
    {
        var currentVersion = CloudAggregateVersion.Initial;

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, currentVersion, activeAllocation: null, CloudMutationGateState.Frozen);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.MutationsFrozen, result.ErrorKind);
    }

    [TestMethod]
    public void Transfer_ReservationCheckTakesPriorityOverAStaleVersion()
    {
        // Reporting the exclusivity conflict first, even when the version is also stale, matches
        // WDR-001's own precedence: a reservation blocks the action outright, independent of the
        // caller's view of the aggregate version.
        var staleVersion = new CloudAggregateVersion(1);
        var currentVersion = new CloudAggregateVersion(2);
        var activeAllocation = new CloudReservationAllocation(
            new CloudReservationId(Guid.NewGuid()), Target, CloudReservationKind.Offer, CloudReservationStatus.Active);

        var result = CloudOwnershipTransferPolicy.Transfer(
            Target, CurrentOwnerId, NewOwnerId, currentVersion, staleVersion, activeAllocation, CloudMutationGateState.Open);

        Assert.AreEqual(CloudCustodyTransitionErrorKind.TargetAlreadyReserved, result.ErrorKind);
    }
}
