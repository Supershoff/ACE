namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section coverage for <see cref="CloudTransferOfferPolicy.Accept"/>,
/// <see cref="CloudTransferOfferPolicy.Decline"/>, and <see cref="CloudTransferOfferPolicy.Cancel"/>
/// (issue #35, XFER-002): atomic accept, sender cancel, recipient decline, simultaneous terminal
/// commands, and stale versions.
/// </summary>
[TestClass]
public sealed class CloudTransferOfferPolicyResolveTests
{
    private static readonly CloudAccountId SenderId = new(Guid.NewGuid());
    private static readonly CloudAccountId RecipientId = new(Guid.NewGuid());
    private static readonly CloudAccountId StrangerId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset ExpiresAtUtc = CreatedAtUtc.AddDays(7);

    private static CloudTransferOffer NewPendingOffer() => new(
        new CloudTransferOfferId(Guid.NewGuid()), SenderId, RecipientId, new CloudReservationId(Guid.NewGuid()), CreatedAtUtc, ExpiresAtUtc);

    [TestMethod]
    public void Accept_ByTheRecipient_TransitionsToAcceptedAndAdvancesVersion()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Accept(
            offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferStatus.Accepted, result.Offer!.Status);
        Assert.AreEqual(offer.Version.Next(), result.Offer.Version);
        Assert.AreEqual(CreatedAtUtc.AddHours(1), result.Offer.ResolvedAtUtc);
    }

    [TestMethod]
    public void Accept_ByTheSender_IsNotAuthorized()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Accept(offer, SenderId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotAuthorized, result.RejectionCode);
    }

    [TestMethod]
    public void Accept_ByAnUnrelatedAccount_IsNotAuthorized()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Accept(offer, StrangerId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotAuthorized, result.RejectionCode);
    }

    [TestMethod]
    public void Accept_PastTheSevenDayDeadline_IsRejectedEvenThoughStillPending()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Accept(offer, RecipientId, offer.Version, ExpiresAtUtc.AddSeconds(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.AlreadyExpired, result.RejectionCode);
    }

    [TestMethod]
    public void Decline_ByTheRecipient_TransitionsToDeclinedAndAdvancesVersion()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Decline(
            offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferStatus.Declined, result.Offer!.Status);
        Assert.AreEqual(offer.Version.Next(), result.Offer.Version);
    }

    [TestMethod]
    public void Decline_ByTheSender_IsNotAuthorized()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Decline(offer, SenderId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotAuthorized, result.RejectionCode);
    }

    [TestMethod]
    public void Cancel_ByTheSender_TransitionsToCancelledAndAdvancesVersion()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Cancel(
            offer, SenderId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferStatus.Cancelled, result.Offer!.Status);
        Assert.AreEqual(offer.Version.Next(), result.Offer.Version);
    }

    [TestMethod]
    public void Cancel_ByTheRecipient_IsNotAuthorized()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Cancel(offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotAuthorized, result.RejectionCode);
    }

    [TestMethod]
    public void Cancel_PastTheSevenDayDeadlineButNotYetSwept_StillSucceeds()
    {
        // Unlike Accept, Cancel/Decline never fulfill anything -- letting a technically-expired-but-
        // unswept offer still be explicitly cancelled/declined is harmless and mirrors
        // CloudReservationPolicy.Release's own asymmetry (only Fulfilled blocks on expiry).
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Cancel(offer, SenderId, offer.Version, ExpiresAtUtc.AddSeconds(1), CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Resolve_WithAStaleExpectedVersion_IsRejectedAsAVersionConflict()
    {
        var offer = NewPendingOffer();
        var staleVersion = new CloudAggregateVersion(offer.Version.Value + 1);

        var result = CloudTransferOfferPolicy.Accept(offer, RecipientId, staleVersion, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.VersionConflict, result.RejectionCode);
    }

    [TestMethod]
    public void SimultaneousTerminalCommands_OnlyTheFirstAppliedTransitionWins()
    {
        // Simulates two racing callers who both read the same Pending offer/version: the sender's
        // Cancel and the recipient's Accept. Whichever the caller applies first (mirroring a real
        // boundary's committed row lock order) produces the new committed offer; replaying the second
        // command against the pre-transition offer/version must be rejected, never silently accepted
        // or double-applied.
        var offer = NewPendingOffer();

        var acceptResult = CloudTransferOfferPolicy.Accept(
            offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);
        Assert.IsTrue(acceptResult.IsSuccess);

        // The sender's Cancel command was built against the same pre-transition offer/version and
        // loses the race -- it must be rejected against the *new* committed offer, not silently
        // re-applied on top of the stale one it captured.
        var cancelResult = CloudTransferOfferPolicy.Cancel(
            acceptResult.Offer!, SenderId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open);

        Assert.IsFalse(cancelResult.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotPending, cancelResult.RejectionCode);
    }

    [TestMethod]
    public void Resolve_AnAlreadyResolvedOffer_IsRejectedAsNotPending()
    {
        var offer = NewPendingOffer();
        var accepted = CloudTransferOfferPolicy.Accept(
            offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open).Offer!;

        var secondAccept = CloudTransferOfferPolicy.Accept(
            accepted, RecipientId, accepted.Version, CreatedAtUtc.AddHours(2), CloudMutationGateState.Open);

        Assert.IsFalse(secondAccept.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotPending, secondAccept.RejectionCode);
    }

    [TestMethod]
    public void Resolve_WhileMutationsAreFrozen_IsRejectedRegardlessOfActorOrState()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Accept(offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Frozen);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.MutationsFrozen, result.RejectionCode);
    }
}
