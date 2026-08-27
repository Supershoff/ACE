using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// Issue #10's Red section: "Add mutation-test or deliberate-fault cases proving the suites fail
/// when a version, constraint, ledger append, or quantity check is removed." Each test here wires
/// one of the shared invariant suites to a deliberately broken harness -- standing in for an
/// adapter that forgot the exact check the suite exists to catch -- and asserts the suite's own
/// <c>[TestMethod]</c> throws <see cref="AssertFailedException"/>. This is what proves the suites
/// are meaningful rather than vacuously green: a suite that could never fail would not be evidence
/// of anything.
/// </summary>
// The nested *Suite classes below deliberately omit [TestClass]: they exist only so this class's
// own tests can invoke their inherited [TestMethod]s directly and assert the failure, not so MSTest
// discovers and runs them as a second, always-broken copy of each suite.
#pragma warning disable MSTEST0030
[TestClass]
public sealed class CloudInvariantSuiteMutationCoverageTests
{
    [TestMethod]
    public async Task IdempotentCommandSuite_Fails_WhenTheHarnessIgnoresTheIdempotencyKey()
    {
        var suite = new BrokenIdempotentCommandSuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            suite.RepeatedIdempotencyKey_ReplaysTheSameCommittedEffect_WithoutDuplicating,
            "A harness that mints a new effect on every call, ignoring the idempotency key entirely, must fail this suite -- otherwise the suite is not actually proving ARCH-006.");
    }

    [TestMethod]
    public async Task OptimisticConflictSuite_Fails_WhenTheHarnessNeverChecksTheExpectedVersion()
    {
        var suite = new BrokenOptimisticConflictSuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            suite.StaleExpectedVersion_IsRejectedAsAConflict,
            "A harness that commits every mutation regardless of expected version must fail this suite -- otherwise the suite is not actually proving transaction rule 3.");
    }

    [TestMethod]
    public async Task OptimisticConflictSuite_Fails_WhenTheHarnessHasNoDeterministicLocking()
    {
        var suite = new BrokenOptimisticConflictSuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            suite.ConcurrentMutationsAgainstTheSameExpectedVersion_ExactlyOneCommits,
            "A harness with no locking lets two concurrent callers both commit against the same expected version -- this suite must catch that (transaction rule 2).");
    }

    [TestMethod]
    public async Task LedgerOutboxAtomicitySuite_Fails_WhenAPreCommitCrashStillLeavesTheOutboxEventBehind()
    {
        var suite = new BrokenLedgerOutboxAtomicitySuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            suite.CrashBeforeCommit_LeavesNeitherTheLedgerNorTheOutboxEvent,
            "A harness whose outbox append is not part of the same rolled-back transaction as the ledger append must fail this suite -- otherwise the suite is not actually proving transaction rule 5.");
    }

    [TestMethod]
    public async Task LotConservationSuite_Fails_WhenASplitDoesNotReduceTheOriginalLotsQuantity()
    {
        var suite = new BrokenLotConservationSuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            () => suite.RandomizedSplitMergeTransferSequence_AlwaysConservesExactSumToBackingStack(seed: 1),
            "A harness whose split conjures quantity instead of moving it must fail this suite -- otherwise the suite is not actually proving ARCH-010/ARCH-011 conservation.");
    }

    [TestMethod]
    public async Task EventConsumptionSuite_Fails_WhenTheHarnessAppliesEveryDeliveryInsteadOfCheckingVersion()
    {
        var suite = new BrokenEventConsumptionSuite();

        await Assert.ThrowsExactlyAsync<AssertFailedException>(
            suite.OutOfOrderDelivery_ConvergesToTheHighestVersion_EvenWhenTheLaterEventArrivesFirst,
            "A harness that always applies whatever arrives last, instead of keeping the highest version seen, must fail this suite -- otherwise the suite is not actually proving ARCH-007/transaction rule 6.");
    }

    private sealed class BrokenIdempotentCommandSuite : CloudIdempotentCommandInvariantSuite<InMemoryEffect>
    {
        protected override ICloudIdempotentCommandHarness<InMemoryEffect> CreateHarness() => new BrokenIdempotentCommandHarness();
    }

    /// <summary>Ignores the idempotency key: every call mints a brand-new effect.</summary>
    private sealed class BrokenIdempotentCommandHarness : ICloudIdempotentCommandHarness<InMemoryEffect>
    {
        private int _count;

        public Task<InMemoryEffect> ExecuteAsync(Guid idempotencyKey)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new InMemoryEffect(Guid.NewGuid()));
        }

        public Task<int> CountCommittedEffectsAsync() => Task.FromResult(Volatile.Read(ref _count));

        public Guid IdentityOf(InMemoryEffect effect) => effect.Id;
    }

    private sealed class BrokenOptimisticConflictSuite : CloudOptimisticConflictInvariantSuite<InMemoryVersionedState>
    {
        protected override ICloudOptimisticConflictHarness<InMemoryVersionedState> CreateHarness() => new BrokenOptimisticConflictHarness();
    }

    /// <summary>Never checks the expected version and has no locking: every mutation just commits.</summary>
    private sealed class BrokenOptimisticConflictHarness : ICloudOptimisticConflictHarness<InMemoryVersionedState>
    {
        public Task<InMemoryVersionedState> ArrangeAsync() => Task.FromResult(new InMemoryVersionedState());

        public int VersionOf(InMemoryVersionedState state) => state.Version;

        public Task<bool> TryMutateAsync(InMemoryVersionedState state, int expectedVersion) => Task.FromResult(true);
    }

    private sealed class BrokenLedgerOutboxAtomicitySuite : CloudLedgerOutboxAtomicityInvariantSuite
    {
        protected override ICloudLedgerOutboxAtomicityHarness CreateHarness() => new BrokenLedgerOutboxAtomicityHarness();
    }

    /// <summary>Writes the outbox event durably before the ledger append and the simulated crash, so a
    /// pre-commit crash still leaves the outbox event behind -- the exact bug transaction rule 5 exists
    /// to prevent.</summary>
    private sealed class BrokenLedgerOutboxAtomicityHarness : ICloudLedgerOutboxAtomicityHarness
    {
        private readonly List<Guid> _ledgerEvents = [];
        private readonly List<Guid> _outboxEvents = [];

        public Task<Guid> PerformCommittedMutationAsync()
        {
            var correlationId = Guid.NewGuid();
            _outboxEvents.Add(correlationId);
            _ledgerEvents.Add(correlationId);
            return Task.FromResult(correlationId);
        }

        public Task PerformMutationThatCrashesBeforeCommitAsync()
        {
            var correlationId = Guid.NewGuid();
            _outboxEvents.Add(correlationId); // durable before the "crash" -- the bug.
            throw new InvalidOperationException("Simulated crash before commit.");
        }

        public Task<int> CountLedgerEventsAsync() => Task.FromResult(_ledgerEvents.Count);

        public Task<int> CountOutboxEventsAsync() => Task.FromResult(_outboxEvents.Count);

        public Task<bool> LedgerEventExistsAsync(Guid correlationId) => Task.FromResult(_ledgerEvents.Contains(correlationId));

        public Task<bool> OutboxEventExistsAsync(Guid correlationId) => Task.FromResult(_outboxEvents.Contains(correlationId));
    }

    private sealed class BrokenLotConservationSuite : CloudLotConservationInvariantSuite<Guid, Guid>
    {
        protected override ICloudLotConservationHarness<Guid, Guid> CreateHarness() => new BrokenLotConservationHarness(totalQuantity: 1_000);
    }

    /// <summary>Splits without reducing the original lot's quantity, conjuring extra quantity out of thin air.</summary>
    private sealed class BrokenLotConservationHarness : ICloudLotConservationHarness<Guid, Guid>
    {
        private readonly List<InMemoryLot> _lots;

        public BrokenLotConservationHarness(int totalQuantity)
        {
            TotalQuantity = totalQuantity;
            _lots = [new InMemoryLot(Guid.NewGuid(), totalQuantity)];
        }

        public int TotalQuantity { get; }

        public Task<IReadOnlyList<CloudLotSnapshot<Guid, Guid>>> GetLotsAsync()
        {
            IReadOnlyList<CloudLotSnapshot<Guid, Guid>> snapshot =
                _lots.Select(l => new CloudLotSnapshot<Guid, Guid>(l.Id, l.Version, l.OwnerId, l.Quantity)).ToList();
            return Task.FromResult(snapshot);
        }

        public Task<bool> SplitAsync(Guid lotId, int expectedVersion, Guid newOwnerId, int quantity)
        {
            var lot = _lots.SingleOrDefault(l => l.Id == lotId);
            if (lot is null || lot.Version != expectedVersion || quantity <= 0 || quantity >= lot.Quantity)
            {
                return Task.FromResult(false);
            }

            // The bug: the original lot's quantity is never reduced, so the new lot's quantity is
            // conjured rather than moved.
            lot.Version++;
            _lots.Add(new InMemoryLot(newOwnerId, quantity));
            return Task.FromResult(true);
        }

        public Task<bool> MergeAsync(Guid keepLotId, int expectedKeepVersion, Guid mergeLotId, int expectedMergeVersion)
        {
            var keep = _lots.SingleOrDefault(l => l.Id == keepLotId);
            var merge = _lots.SingleOrDefault(l => l.Id == mergeLotId);
            if (keep is null || merge is null || keep.Version != expectedKeepVersion || merge.Version != expectedMergeVersion || keep.OwnerId != merge.OwnerId)
            {
                return Task.FromResult(false);
            }

            keep.Quantity += merge.Quantity;
            keep.Version++;
            _lots.Remove(merge);
            return Task.FromResult(true);
        }

        public Task<bool> TransferAsync(Guid lotId, int expectedVersion, Guid newOwnerId)
        {
            var lot = _lots.SingleOrDefault(l => l.Id == lotId);
            if (lot is null || lot.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            lot.OwnerId = newOwnerId;
            lot.Version++;
            return Task.FromResult(true);
        }

        public Guid NewOwnerId() => Guid.NewGuid();
    }

    private sealed class BrokenEventConsumptionSuite : CloudEventConsumptionInvariantSuite<string>
    {
        protected override ICloudEventConsumptionHarness<string> CreateHarness() => new BrokenEventConsumptionHarness();

        protected override string CreatePayload(int step) => $"payload-{step}";
    }

    /// <summary>Applies whatever arrives, in delivery order, instead of comparing versions -- so a
    /// late, older-version redelivery incorrectly regresses the projection.</summary>
    private sealed class BrokenEventConsumptionHarness : ICloudEventConsumptionHarness<string>
    {
        private static readonly CloudShardId ShardId = new("us1");

        private CloudAggregateVersion? _appliedVersion;

        public Task ApplyAsync(CloudEventEnvelope<string> envelope)
        {
            _appliedVersion = envelope.Version;
            return Task.CompletedTask;
        }

        public Task<CloudAggregateVersion?> GetAppliedVersionAsync() => Task.FromResult(_appliedVersion);

        public CloudEventEnvelope<string> CreateEnvelope(CloudAggregateVersion version, string payload) =>
            new(ShardId, version, new CloudIdempotencyKey(Guid.NewGuid()), DateTimeOffset.UtcNow, payload);
    }
}
#pragma warning restore MSTEST0030
