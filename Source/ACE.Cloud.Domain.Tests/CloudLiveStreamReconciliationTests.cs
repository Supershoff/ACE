namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// EVT-007: "Optimistic UI must reconcile to server versions and visibly reverse when the committed
/// result differs" -- part of issue #22's Red "stale optimistic updates" requirement.
/// </summary>
[TestClass]
public sealed class CloudLiveStreamReconciliationTests
{
    [TestMethod]
    public void ConfirmedOptimisticUpdate_MatchingTheAuthoritativeVersion_NeedsNoReversal()
    {
        Assert.IsFalse(CloudLiveStreamReconciliation.ShouldReverseOptimisticUpdate(optimisticSequenceNumber: 7, authoritativeSequenceNumber: 7));
    }

    [TestMethod]
    public void RejectedOptimisticUpdate_AuthoritativeVersionStayedBehind_MustBeVisiblyReversed()
    {
        Assert.IsTrue(CloudLiveStreamReconciliation.ShouldReverseOptimisticUpdate(optimisticSequenceNumber: 7, authoritativeSequenceNumber: 6));
    }

    [TestMethod]
    public void SupersededOptimisticUpdate_AnotherCommitAdvancedFurther_MustBeVisiblyReversed()
    {
        Assert.IsTrue(CloudLiveStreamReconciliation.ShouldReverseOptimisticUpdate(optimisticSequenceNumber: 7, authoritativeSequenceNumber: 9));
    }
}
