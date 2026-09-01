namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section coverage for <see cref="CloudTransferOfferPolicy.Expire"/> and
/// <see cref="CloudTransferOfferPolicy.ShiftExpiry"/> (issue #35, XFER-002, ADM-004): seven-day
/// expiry and Global Cloud Maintenance clock pauses.
/// </summary>
[TestClass]
public sealed class CloudTransferOfferPolicyExpireTests
{
    private static readonly CloudAccountId SenderId = new(Guid.NewGuid());
    private static readonly CloudAccountId RecipientId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset ExpiresAtUtc = CreatedAtUtc.AddDays(7);

    private static CloudTransferOffer NewPendingOffer() => new(
        new CloudTransferOfferId(Guid.NewGuid()), SenderId, RecipientId, new CloudReservationId(Guid.NewGuid()), CreatedAtUtc, ExpiresAtUtc);

    [TestMethod]
    public void Expire_AtOrPastTheDeadline_TransitionsToExpiredAndAdvancesVersion()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Expire(offer, offer.Version, ExpiresAtUtc, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferStatus.Expired, result.Offer!.Status);
        Assert.AreEqual(offer.Version.Next(), result.Offer.Version);
        Assert.AreEqual(ExpiresAtUtc, result.Offer.ResolvedAtUtc);
    }

    [TestMethod]
    public void Expire_BeforeTheDeadline_Throws()
    {
        var offer = NewPendingOffer();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CloudTransferOfferPolicy.Expire(offer, offer.Version, ExpiresAtUtc.AddSeconds(-1), CloudMutationGateState.Open));
    }

    [TestMethod]
    public void Expire_AnAlreadyResolvedOffer_IsRejectedAsNotPending()
    {
        var offer = NewPendingOffer();
        var accepted = CloudTransferOfferPolicy.Accept(
            offer, RecipientId, offer.Version, CreatedAtUtc.AddHours(1), CloudMutationGateState.Open).Offer!;

        var result = CloudTransferOfferPolicy.Expire(accepted, accepted.Version, ExpiresAtUtc, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.NotPending, result.RejectionCode);
    }

    [TestMethod]
    public void Expire_WithAStaleExpectedVersion_IsRejectedAsAVersionConflict()
    {
        var offer = NewPendingOffer();
        var staleVersion = new CloudAggregateVersion(offer.Version.Value + 1);

        var result = CloudTransferOfferPolicy.Expire(offer, staleVersion, ExpiresAtUtc, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.VersionConflict, result.RejectionCode);
    }

    [TestMethod]
    public void Expire_WhileMutationsAreFrozen_IsRejected()
    {
        var offer = NewPendingOffer();

        var result = CloudTransferOfferPolicy.Expire(offer, offer.Version, ExpiresAtUtc, CloudMutationGateState.Frozen);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.MutationsFrozen, result.RejectionCode);
    }

    [TestMethod]
    public void ShiftExpiry_ByTheFrozenDuration_MovesTheDeadlineForwardAndAdvancesVersion()
    {
        var offer = NewPendingOffer();
        var frozenDuration = TimeSpan.FromHours(6);

        var shifted = CloudTransferOfferPolicy.ShiftExpiry(offer, frozenDuration);

        Assert.AreEqual(ExpiresAtUtc + frozenDuration, shifted.ExpiresAtUtc);
        Assert.AreEqual(offer.Version.Next(), shifted.Version);
        Assert.AreEqual(CloudTransferOfferStatus.Pending, shifted.Status);
    }

    [TestMethod]
    public void ShiftExpiry_ThenExpireAtTheOriginalDeadline_NoLongerExpires()
    {
        // ADM-004: "Entering Global Cloud Maintenance pauses ... offer ... clocks; leaving it shifts
        // deadlines by the exact frozen duration." An offer that would have expired at its original
        // deadline must not expire there anymore once its clock has been shifted.
        var offer = NewPendingOffer();
        var frozenDuration = TimeSpan.FromDays(2);
        var shifted = CloudTransferOfferPolicy.ShiftExpiry(offer, frozenDuration);

        Assert.IsFalse(shifted.IsExpiredAt(ExpiresAtUtc));
        Assert.IsTrue(shifted.IsExpiredAt(ExpiresAtUtc + frozenDuration));
    }

    [TestMethod]
    public void ShiftExpiry_WithANonPositiveDuration_Throws()
    {
        var offer = NewPendingOffer();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudTransferOfferPolicy.ShiftExpiry(offer, TimeSpan.Zero));
    }
}
