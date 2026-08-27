namespace ACE.Cloud.TestKit;

/// <summary>
/// Reusable contract tests proving the Activity Ledger and Custody Outbox entries for one mutation
/// commit atomically together (EVT-001, ARCH-007, transaction rule 5). See
/// <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> for how an adapter adopts a suite
/// like this without copying its test logic.
/// </summary>
public abstract class CloudLedgerOutboxAtomicityInvariantSuite
{
    protected abstract ICloudLedgerOutboxAtomicityHarness CreateHarness();

    [TestMethod]
    public async Task CommittedMutation_WritesBothTheLedgerAndOutboxEvent()
    {
        var harness = CreateHarness();

        var correlationId = await harness.PerformCommittedMutationAsync();

        Assert.IsTrue(
            await harness.LedgerEventExistsAsync(correlationId),
            "A committed mutation must append its Activity Ledger event (EVT-001).");
        Assert.IsTrue(
            await harness.OutboxEventExistsAsync(correlationId),
            "A committed mutation must append its Custody Outbox event in the same transaction as the ledger event (ARCH-007).");
    }

    [TestMethod]
    public async Task CrashBeforeCommit_LeavesNeitherTheLedgerNorTheOutboxEvent()
    {
        var harness = CreateHarness();
        var threw = false;

        try
        {
            await harness.PerformMutationThatCrashesBeforeCommitAsync();
        }
        catch
        {
            threw = true;
        }

        Assert.IsTrue(threw, "The harness's simulated pre-commit crash must actually prevent a normal return; otherwise this test cannot prove anything about atomicity.");
        Assert.AreEqual(
            0,
            await harness.CountLedgerEventsAsync(),
            "A crash before commit must roll back the ledger append along with everything else in the transaction (transaction rule 5).");
        Assert.AreEqual(
            0,
            await harness.CountOutboxEventsAsync(),
            "A crash before commit must roll back the outbox append too -- the ledger and outbox must never be allowed to diverge.");
    }
}
