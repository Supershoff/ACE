namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #22's Red requirement: "Test duplicate, delayed, out-of-order, and poison
/// events; empty rebuild; checkpoint loss; consumer restart." This covers the pure apply decision
/// (ARCH-007, transaction rule 6); <see cref="CloudProjectionSequenceGuard"/>'s doc comment explains
/// why a projection's own last-applied outbox sequence number is sufficient without a separate
/// per-aggregate version counter.
/// </summary>
[TestClass]
public sealed class CloudProjectionSequenceGuardTests
{
    [TestMethod]
    public void ShouldApply_FirstEventEverSeenForARow_IsTrue()
    {
        Assert.IsTrue(CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: null, incomingSequenceNumber: 1));
    }

    [TestMethod]
    public void ShouldApply_StrictlyNewerThanLastApplied_IsTrue()
    {
        Assert.IsTrue(CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: 5, incomingSequenceNumber: 6));
    }

    [TestMethod]
    public void ShouldApply_DuplicateOfLastApplied_IsFalse()
    {
        Assert.IsFalse(
            CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: 5, incomingSequenceNumber: 5),
            "Redelivering an already-applied event must be an idempotent no-op.");
    }

    [TestMethod]
    public void ShouldApply_OlderThanLastApplied_IsFalse()
    {
        Assert.IsFalse(
            CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: 5, incomingSequenceNumber: 3),
            "A stale/delayed redelivery arriving after a newer event was already applied must never regress the projection.");
    }

    [TestMethod]
    public void ShouldApply_NewerVersionBeforeOlderVersionDelivery_StillConverges()
    {
        // Simulates the newer event arriving first (e.g. two consumer batches processed out of the
        // outbox's own order due to a retry race), then the older one catching up.
        Assert.IsTrue(CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: null, incomingSequenceNumber: 9));
        Assert.IsFalse(
            CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: 9, incomingSequenceNumber: 4),
            "Once the newer event has been applied, the older event arriving late must not roll state back.");
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void ShouldApply_RejectsNonPositiveIncomingSequenceNumbers(long incoming)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CloudProjectionSequenceGuard.ShouldApply(lastAppliedSequenceNumber: null, incomingSequenceNumber: incoming));
    }
}
