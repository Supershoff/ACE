namespace ACE.Cloud.TestKit;

/// <summary>
/// Reusable randomized property tests proving Cloud Stack Lot conservation (ARCH-010, ARCH-011,
/// INV-001, docs/adr/0002-defer-native-materialization-for-partial-stacks.md). Hand-rolled seeded
/// randomness rather than a property-testing library, matching the style established by issue #5's
/// original persistence-only version of this test -- a fixed seed keeps every run identical and
/// stable under repetition. See <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> for how
/// an adapter adopts a suite like this without copying its test logic.
/// </summary>
public abstract class CloudLotConservationInvariantSuite<TLotId, TOwnerId>
    where TLotId : notnull
{
    protected abstract ICloudLotConservationHarness<TLotId, TOwnerId> CreateHarness();

    [TestMethod]
    [DataRow(1, DisplayName = "seed 1")]
    [DataRow(42, DisplayName = "seed 42")]
    [DataRow(2026, DisplayName = "seed 2026")]
    public async Task RandomizedSplitMergeTransferSequence_AlwaysConservesExactSumToBackingStack(int seed)
    {
        var harness = CreateHarness();
        var random = new Random(seed);

        for (var step = 0; step < 60; step++)
        {
            var lots = await harness.GetLotsAsync();

            Assert.IsNotEmpty(lots, "At least one lot must always exist while the stack has quantity.");
            Assert.IsTrue(lots.All(l => l.Quantity > 0), "Every lot's quantity must remain positive (no lot may reach zero).");
            Assert.AreEqual(harness.TotalQuantity, lots.Sum(l => l.Quantity), "The sum of every lot must always equal the backing stack's total quantity.");

            var operation = random.Next(3);

            // Reusing an existing owner some of the time (instead of always minting a fresh owner)
            // makes same-owner lot pairs -- and therefore merges -- actually occur during the
            // random walk, rather than requiring an astronomically unlikely identity collision.
            var targetOwnerId = random.Next(3) == 0
                ? lots[random.Next(lots.Count)].OwnerId
                : harness.NewOwnerId();

            if (operation == 0 || lots.Count == 1)
            {
                var splittable = lots.Where(l => l.Quantity > 1).ToList();
                if (splittable.Count == 0)
                {
                    continue;
                }

                var lot = splittable[random.Next(splittable.Count)];
                var quantityToSplit = random.Next(1, lot.Quantity);
                Assert.IsTrue(await harness.SplitAsync(lot.Id, lot.Version, targetOwnerId, quantityToSplit), "A split against the lot's current version must succeed.");
            }
            else if (operation == 1 && lots.Count >= 2)
            {
                var mergeable = FindMergeablePair(lots);
                if (mergeable is null)
                {
                    continue;
                }

                var (keep, merge) = mergeable.Value;
                Assert.IsTrue(await harness.MergeAsync(keep.Id, keep.Version, merge.Id, merge.Version), "A merge of two same-owner lots at their current versions must succeed.");
            }
            else
            {
                var lot = lots[random.Next(lots.Count)];
                Assert.IsTrue(await harness.TransferAsync(lot.Id, lot.Version, targetOwnerId), "A transfer against the lot's current version must succeed.");
            }
        }

        var finalLots = await harness.GetLotsAsync();
        Assert.AreEqual(harness.TotalQuantity, finalLots.Sum(l => l.Quantity), "Conservation must hold after a randomized sequence of split/merge/transfer operations.");
        Assert.IsTrue(finalLots.All(l => l.Quantity > 0));
    }

    private static (CloudLotSnapshot<TLotId, TOwnerId> Keep, CloudLotSnapshot<TLotId, TOwnerId> Merge)? FindMergeablePair(
        IReadOnlyList<CloudLotSnapshot<TLotId, TOwnerId>> lots)
    {
        for (var i = 0; i < lots.Count; i++)
        {
            for (var j = i + 1; j < lots.Count; j++)
            {
                if (EqualityComparer<TOwnerId>.Default.Equals(lots[i].OwnerId, lots[j].OwnerId))
                {
                    return (lots[i], lots[j]);
                }
            }
        }

        return null;
    }
}
