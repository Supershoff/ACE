using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green property tests for issue #5 (ARCH-010, ARCH-011, INV-001,
/// docs/adr/0002-defer-native-materialization-for-partial-stacks.md): randomized sequences of
/// split/merge/transfer must never create, lose, duplicate, or over-allocate a Cloud Stack Lot's
/// quantity, and concurrent reservations against the same lot must serialize instead of racing.
///
/// These are hand-rolled seeded-random sequences rather than a property-testing library (no such
/// dependency exists anywhere else in this repository -- AGENTS.md's "search for an existing
/// helper before accepting duplication" -- and issue #4 established the same hand-rolled style for
/// its own crash/concurrency proofs). A fixed seed keeps every run identical, satisfying the
/// acceptance criterion that new tests stay stable under repetition.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotConservationPropertyTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 800_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    [DataRow(1, DisplayName = "seed 1")]
    [DataRow(42, DisplayName = "seed 42")]
    [DataRow(2026, DisplayName = "seed 2026")]
    public async Task RandomizedSplitMergeTransferSequence_AlwaysConservesExactSumToBackingStack(int seed)
    {
        const int totalQuantity = 1_000;
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var depositOwnerId = Guid.NewGuid();

        Guid custodyRecordId;
        await using (var depositContext = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(depositContext);
            var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, depositOwnerId, totalQuantity, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
            custodyRecordId = depositOutcome.Value!.CustodyRecord.Id;
        }

        var random = new Random(seed);

        for (var step = 0; step < 200; step++)
        {
            await using var context = new CloudDbContext(options);
            var authority = new CloudStackLotTransactionAuthority(context);

            var lots = await context.CloudStackLots
                .Where(l => l.CustodyRecordId == custodyRecordId)
                .OrderBy(l => l.Id)
                .ToListAsync();

            Assert.IsNotEmpty(lots, "At least one lot must always exist while the stack has quantity.");
            Assert.IsTrue(lots.All(l => l.Quantity > 0), "Every lot's quantity must remain positive (no lot may reach zero).");
            Assert.AreEqual(totalQuantity, lots.Sum(l => l.Quantity), "The sum of every lot must always equal the backing stack's total quantity.");

            var operation = random.Next(3);

            // Reusing an existing owner some of the time (instead of always minting a fresh Guid)
            // makes same-owner lot pairs -- and therefore merges -- actually occur during the
            // random walk, rather than requiring an astronomically unlikely Guid collision.
            var targetOwnerId = random.Next(3) == 0
                ? lots[random.Next(lots.Count)].OwnerId
                : Guid.NewGuid();

            if (operation == 0 || lots.Count == 1)
            {
                // Split: pick any lot with more than one unit and carve off a random smaller piece.
                var splittable = lots.Where(l => l.Quantity > 1).ToList();
                if (splittable.Count == 0)
                {
                    continue;
                }

                var lot = splittable[random.Next(splittable.Count)];
                var quantityToSplit = random.Next(1, lot.Quantity);
                var outcome = await authority.SplitLotAsync(lot.Id, lot.Version, targetOwnerId, quantityToSplit);
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
            else if (operation == 1 && lots.Count >= 2)
            {
                // Merge: only lots sharing an owner may merge off-world; find such a pair if one exists.
                var mergeable = FindMergeablePair(lots);
                if (mergeable is null)
                {
                    continue;
                }

                var (keep, merge) = mergeable.Value;
                var outcome = await authority.MergeLotsAsync(keep.Id, keep.Version, merge.Id, merge.Version);
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
            else
            {
                // Transfer: reassign a random lot to another owner; quantity is untouched.
                var lot = lots[random.Next(lots.Count)];
                var outcome = await authority.TransferLotAsync(lot.Id, lot.Version, targetOwnerId);
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
        }

        await using var finalContext = new CloudDbContext(options);
        var finalLots = await finalContext.CloudStackLots.Where(l => l.CustodyRecordId == custodyRecordId).ToListAsync();
        Assert.AreEqual(totalQuantity, finalLots.Sum(l => l.Quantity), "Conservation must hold after 200 randomized operations.");
        Assert.IsTrue(finalLots.All(l => l.Quantity > 0));
    }

    [TestMethod]
    public async Task ConcurrentSplitReservations_AgainstTheSameLot_OnlyOneSucceeds_AndSumStaysExact()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid custodyRecordId;
        Guid lotId;
        int lotVersion;
        await using (var depositContext = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(depositContext);
            var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 10, Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.CustodyRecord.Id;
            lotId = depositOutcome.Value!.Lot.Id;
            lotVersion = depositOutcome.Value!.Lot.Version;
        }

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var authorityA = new CloudStackLotTransactionAuthority(contextA);
        var authorityB = new CloudStackLotTransactionAuthority(contextB);

        // Two concurrent reservations each try to carve off 7 of the 10 available units from the
        // same lot/version; both cannot succeed (7 + 7 > 10), and both cannot possibly observe the
        // same pre-split version since only one write can win.
        var taskA = authorityA.SplitLotAsync(lotId, lotVersion, Guid.NewGuid(), 7);
        var taskB = authorityB.SplitLotAsync(lotId, lotVersion, Guid.NewGuid(), 7);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Exactly one concurrent reservation may win the same lot version.");
        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Conflict));

        await using var verifyContext = new CloudDbContext(options);
        var lots = await verifyContext.CloudStackLots.Where(l => l.CustodyRecordId == custodyRecordId).ToListAsync();
        Assert.AreEqual(10, lots.Sum(l => l.Quantity), "Conservation must hold even when a concurrent reservation is rejected.");
        Assert.IsTrue(lots.All(l => l.Quantity > 0));
    }

    [TestMethod]
    public async Task ConcurrentSplitReservations_AgainstDifferentLotsOfTheSameStack_BothSucceed_AndSumStaysExact()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid custodyRecordId;
        Guid lotAId, lotBId;
        int lotAVersion, lotBVersion;
        await using (var depositContext = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(depositContext);
            var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 20, Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.CustodyRecord.Id;

            var authority = new CloudStackLotTransactionAuthority(depositContext);
            var splitOutcome = await authority.SplitLotAsync(depositOutcome.Value!.Lot.Id, depositOutcome.Value!.Lot.Version, Guid.NewGuid(), 10);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind);
            lotAId = splitOutcome.Value!.RemainingLot.Id;
            lotAVersion = splitOutcome.Value!.RemainingLot.Version;
            lotBId = splitOutcome.Value!.NewLot.Id;
            lotBVersion = splitOutcome.Value!.NewLot.Version;
        }

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var authorityA = new CloudStackLotTransactionAuthority(contextA);
        var authorityB = new CloudStackLotTransactionAuthority(contextB);

        // Two concurrent reservations against two different lots of the same backing stack must
        // both be able to proceed: they only conflict if they target the same lot.
        var taskA = authorityA.SplitLotAsync(lotAId, lotAVersion, Guid.NewGuid(), 3);
        var taskB = authorityB.SplitLotAsync(lotBId, lotBVersion, Guid.NewGuid(), 3);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.IsTrue(results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Reservations against independent lots of the same stack must not block each other's success.");

        await using var verifyContext = new CloudDbContext(options);
        var lots = await verifyContext.CloudStackLots.Where(l => l.CustodyRecordId == custodyRecordId).ToListAsync();
        Assert.AreEqual(20, lots.Sum(l => l.Quantity));
        Assert.IsTrue(lots.All(l => l.Quantity > 0));
    }

    private static (CloudStackLot Keep, CloudStackLot Merge)? FindMergeablePair(List<CloudStackLot> lots)
    {
        for (var i = 0; i < lots.Count; i++)
        {
            for (var j = i + 1; j < lots.Count; j++)
            {
                if (lots[i].OwnerId == lots[j].OwnerId)
                {
                    return (lots[i], lots[j]);
                }
            }
        }

        return null;
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
