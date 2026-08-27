namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudBidPriorityPolicy"/> (MKT-105, MKT-110; issue #9 Red section: "equal
/// maxima" and "cross-shard/self-dealing identity inputs" -- self-dealing here is the resolved
/// Main/Linked ownership-group identity check; shard scoping itself remains
/// <c>CloudCommandGuard</c>'s already-covered concern and is not duplicated here, matching
/// <see cref="CloudReservationPolicy"/>'s precedent).
/// </summary>
[TestClass]
public sealed class CloudBidPriorityPolicyTests
{
    private static CloudAccountId NewAccount() => new(Guid.NewGuid());

    [TestMethod]
    public void DetermineLeader_HigherMaximumWins_RegardlessOfCommitOrder()
    {
        var earlyLowBid = new CloudBidCommitment(NewAccount(), 100, commitSequence: 1);
        var laterHighBid = new CloudBidCommitment(NewAccount(), 200, commitSequence: 2);

        var leader = CloudBidPriorityPolicy.DetermineLeader([earlyLowBid, laterHighBid]);

        Assert.AreEqual(laterHighBid, leader);
    }

    [TestMethod]
    public void DetermineLeader_EqualMaxima_FavorsTheEarliestCommittedBid()
    {
        var earlierBid = new CloudBidCommitment(NewAccount(), 150, commitSequence: 5);
        var laterBid = new CloudBidCommitment(NewAccount(), 150, commitSequence: 6);

        var leader = CloudBidPriorityPolicy.DetermineLeader([laterBid, earlierBid]);

        Assert.AreEqual(earlierBid, leader);
    }

    [TestMethod]
    public void DetermineLeader_NoBids_ReturnsNull()
    {
        Assert.IsNull(CloudBidPriorityPolicy.DetermineLeader([]));
    }

    [TestMethod]
    public void OrderByPriority_ProducesDeterministicFullRanking()
    {
        var a = new CloudBidCommitment(NewAccount(), 300, commitSequence: 3);
        var b = new CloudBidCommitment(NewAccount(), 300, commitSequence: 1);
        var c = new CloudBidCommitment(NewAccount(), 250, commitSequence: 0);

        var ordered = CloudBidPriorityPolicy.OrderByPriority([a, b, c]);

        CollectionAssert.AreEqual(new[] { b, a, c }, ordered.ToList());
    }

    [TestMethod]
    public void IsSelfDealing_SameResolvedOwnershipGroup_IsTrue()
    {
        var ownerGroup = NewAccount();
        Assert.IsTrue(CloudBidPriorityPolicy.IsSelfDealing(ownerGroup, ownerGroup));
    }

    [TestMethod]
    public void IsSelfDealing_DifferentOwnershipGroups_IsFalse()
    {
        Assert.IsFalse(CloudBidPriorityPolicy.IsSelfDealing(NewAccount(), NewAccount()));
    }

    [TestMethod]
    public void BidCommitment_NonPositiveMaximum_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudBidCommitment(NewAccount(), 0, commitSequence: 0));
    }

    [TestMethod]
    public void BidCommitment_NegativeCommitSequence_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudBidCommitment(NewAccount(), 100, commitSequence: -1));
    }
}
