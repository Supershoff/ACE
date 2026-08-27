namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Transaction rule 2 ("Lock custody/lot rows in deterministic order for multi-item transactions to
/// avoid deadlocks"): <see cref="CloudReservationTargetOrdering.Order"/> must return the same row
/// order for the same set of targets no matter what order the caller originally listed them in. A
/// fixed seed keeps the randomized permutations reproducible across CI runs.
/// </summary>
[TestClass]
public sealed class CloudReservationTargetOrderingTests
{
    [TestMethod]
    public void Order_RejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudReservationTargetOrdering.Order(null!));
    }

    [TestMethod]
    public void Order_EveryShuffleOfTheSameTargetSet_ProducesTheIdenticalOrder()
    {
        var targets = new List<CloudReservationTarget>
        {
            CloudReservationTarget.ForItem(new CloudItemId(500)),
            CloudReservationTarget.ForItem(new CloudItemId(1)),
            CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid())),
            CloudReservationTarget.ForItem(new CloudItemId(4_000_000_000)),
            CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid())),
        };

        var expectedOrder = CloudReservationTargetOrdering.Order(targets);

        var random = new Random(Seed: 1337);
        for (var trial = 0; trial < 50; trial++)
        {
            var shuffled = Shuffle(targets, random);

            var actualOrder = CloudReservationTargetOrdering.Order(shuffled);

            CollectionAssert.AreEqual(expectedOrder.ToList(), actualOrder.ToList(), $"trial {trial} produced a different row order.");
        }
    }

    [TestMethod]
    public void Order_IsStableAcrossIndependentInvocationsWithNoSharedState()
    {
        var targets = new List<CloudReservationTarget>
        {
            CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid())),
            CloudReservationTarget.ForItem(new CloudItemId(7)),
        };

        var first = CloudReservationTargetOrdering.Order(targets);
        var second = CloudReservationTargetOrdering.Order(new List<CloudReservationTarget>(targets));

        CollectionAssert.AreEqual(first.ToList(), second.ToList());
    }

    [TestMethod]
    public void Order_ReturnsEveryInputTargetExactlyOnce()
    {
        var targets = new List<CloudReservationTarget>
        {
            CloudReservationTarget.ForItem(new CloudItemId(2)),
            CloudReservationTarget.ForItem(new CloudItemId(1)),
            CloudReservationTarget.ForStackLot(new CloudStackLotId(Guid.NewGuid())),
        };

        var ordered = CloudReservationTargetOrdering.Order(targets);

        CollectionAssert.AreEquivalent(targets, ordered.ToList());
    }

    private static List<CloudReservationTarget> Shuffle(IReadOnlyList<CloudReservationTarget> source, Random random)
    {
        var copy = new List<CloudReservationTarget>(source);
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }
}
