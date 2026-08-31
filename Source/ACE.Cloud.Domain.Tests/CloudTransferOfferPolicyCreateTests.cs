namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section coverage for <see cref="CloudTransferOfferPolicy.Create"/> (issue #35, XFER-001,
/// XFER-002, INV-002, INV-004..006): character resolution, unknown/self/cross-shard recipients,
/// quantities/lots, quota checks, duplicate assets, and conflicting reservations.
/// </summary>
[TestClass]
public sealed class CloudTransferOfferPolicyCreateTests
{
    private static readonly CloudTransferOfferId NewOfferId = new(Guid.NewGuid());
    private static readonly CloudReservationId NewReservationId = new(Guid.NewGuid());
    private static readonly CloudAccountId SenderId = new(Guid.NewGuid());
    private static readonly CloudAccountId RecipientId = new(Guid.NewGuid());
    private static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);

    private static readonly CloudReservationTarget ItemTarget = CloudReservationTarget.ForItem(new CloudItemId(1));
    private static readonly CloudReservationTarget LotTarget = CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid()));

    private static Dictionary<CloudReservationTarget, CloudReservationAllocation> NoExistingAllocations() => [];

    private static CloudTransferOfferCreateRequest ValidRequest(
        IReadOnlyList<CloudReservationTarget>? targets = null,
        bool recipientFound = true,
        CloudAccountId? recipientAccountId = null,
        bool crossShard = false,
        IReadOnlyDictionary<CloudReservationTarget, CloudReservationAllocation>? existingAllocations = null,
        int currentProjectedCount = 0,
        int? quotaLimit = null,
        CloudMutationGateState gateState = CloudMutationGateState.Open) =>
        new(
            SenderId,
            recipientFound,
            recipientFound ? recipientAccountId ?? RecipientId : null,
            crossShard,
            targets ?? [ItemTarget],
            existingAllocations ?? NoExistingAllocations(),
            currentProjectedCount,
            quotaLimit,
            gateState);

    [TestMethod]
    public void Create_AValidSingleItemOffer_Succeeds()
    {
        var result = CloudTransferOfferPolicy.Create(NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferStatus.Pending, result.Offer!.Status);
        Assert.AreEqual(SenderId, result.Offer.SenderAccountId);
        Assert.AreEqual(RecipientId, result.Offer.RecipientAccountId);
        Assert.AreEqual(NewReservationId, result.Offer.ReservationId);
        Assert.AreEqual(NowUtc + SevenDays, result.Offer.ExpiresAtUtc);
        Assert.AreEqual(CloudReservationKind.Offer, result.Reservation!.Kind);
        Assert.HasCount(1, result.Allocations);
    }

    [TestMethod]
    public void Create_ResolvesTheRecipientOnceIntoAnImmutableAccountId_IndependentOfLaterCharacterChanges()
    {
        // XFER-001: "resolve a current character name once to immutable recipient Main Account ID;
        // later rename/deletion must not redirect it." The pure Create call only ever receives the
        // already-resolved CloudAccountId (never a character name), and CloudTransferOffer exposes no
        // re-resolution method at all -- so a later rename/deletion of the recipient's character (a
        // persistence-layer fact this offer never observes again) cannot possibly change
        // RecipientAccountId once this offer exists.
        var result = CloudTransferOfferPolicy.Create(NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(RecipientId, result.Offer!.RecipientAccountId);
    }

    [TestMethod]
    public void Create_WithMultipleItemAndStackLotTargets_ProducesOneAllocationPerTarget()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(targets: [ItemTarget, LotTarget]));

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(2, result.Allocations);
        Assert.IsTrue(result.Allocations.All(a => a.ReservationId == NewReservationId));
        Assert.IsTrue(result.Allocations.All(a => a.Kind == CloudReservationKind.Offer));
    }

    [TestMethod]
    public void Create_UnknownRecipientCharacter_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(recipientFound: false));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.UnknownRecipientCharacter, result.RejectionCode);
    }

    [TestMethod]
    public void Create_SelfRecipient_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(recipientAccountId: SenderId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.SelfRecipient, result.RejectionCode);
    }

    [TestMethod]
    public void Create_CrossShardRecipient_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(crossShard: true));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.CrossShardRecipient, result.RejectionCode);
    }

    [TestMethod]
    public void Create_DuplicateTargetsInOneRequest_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(targets: [ItemTarget, ItemTarget]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.DuplicateTargetsInRequest, result.RejectionCode);
    }

    [TestMethod]
    public void Create_NoTargets_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(targets: []));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.EmptyRequest, result.RejectionCode);
    }

    [TestMethod]
    public void Create_ATargetAlreadyExclusivelyReservedByAnyReservationKind_IsRejected()
    {
        var existingReservationId = new CloudReservationId(Guid.NewGuid());
        var existing = new Dictionary<CloudReservationTarget, CloudReservationAllocation>
        {
            [ItemTarget] = new(existingReservationId, ItemTarget, CloudReservationKind.Withdrawal, CloudReservationStatus.Active),
        };

        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays, ValidRequest(existingAllocations: existing));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.TargetAlreadyReserved, result.RejectionCode);
    }

    [TestMethod]
    public void Create_MultiTargetRequest_IsAllOrNone_RejectingEvenWhenOnlyOneTargetConflicts()
    {
        var existingReservationId = new CloudReservationId(Guid.NewGuid());
        var existing = new Dictionary<CloudReservationTarget, CloudReservationAllocation>
        {
            [LotTarget] = new(existingReservationId, LotTarget, CloudReservationKind.Offer, CloudReservationStatus.Active),
        };

        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays,
            ValidRequest(targets: [ItemTarget, LotTarget], existingAllocations: existing));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.TargetAlreadyReserved, result.RejectionCode);
        Assert.IsNull(result.Offer);
    }

    [TestMethod]
    public void Create_RecipientAtOrOverStorageQuota_IsRejected()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays,
            ValidRequest(currentProjectedCount: 10, quotaLimit: 10));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.RecipientOverQuota, result.RejectionCode);
    }

    [TestMethod]
    public void Create_RecipientWithRoomUnderStorageQuota_Succeeds()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays,
            ValidRequest(currentProjectedCount: 8, quotaLimit: 10));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Create_RecipientWithUnlimitedQuota_NeverRejectedRegardlessOfCount()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays,
            ValidRequest(currentProjectedCount: 1_000_000, quotaLimit: null));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Create_WhileMutationsAreFrozen_IsRejectedRegardlessOfOtherState()
    {
        var result = CloudTransferOfferPolicy.Create(
            NewOfferId, NewReservationId, NowUtc, SevenDays,
            ValidRequest(gateState: CloudMutationGateState.Frozen));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudTransferOfferRejectionCode.MutationsFrozen, result.RejectionCode);
    }

    [TestMethod]
    public void Create_WithANonPositiveTimeToLive_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CloudTransferOfferPolicy.Create(NewOfferId, NewReservationId, NowUtc, TimeSpan.Zero, ValidRequest()));
    }
}
