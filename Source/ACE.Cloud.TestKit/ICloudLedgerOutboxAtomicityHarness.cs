namespace ACE.Cloud.TestKit;

/// <summary>
/// The minimal surface an adapter exposes so
/// <see cref="CloudLedgerOutboxAtomicityInvariantSuite"/> can prove EVT-001, ARCH-007, and
/// transaction rule 5: the Activity Ledger entry and Custody Outbox entry for one mutation commit
/// together in the same transaction, and a crash before commit must roll back both together, never
/// leaving one without the other.
/// </summary>
public interface ICloudLedgerOutboxAtomicityHarness
{
    /// <summary>Performs one mutation that commits normally and returns its correlation ID.</summary>
    Task<Guid> PerformCommittedMutationAsync();

    /// <summary>
    /// Performs the same kind of mutation but simulates a crash before the transaction commits.
    /// Must throw rather than return normally.
    /// </summary>
    Task PerformMutationThatCrashesBeforeCommitAsync();

    Task<int> CountLedgerEventsAsync();

    Task<int> CountOutboxEventsAsync();

    Task<bool> LedgerEventExistsAsync(Guid correlationId);

    Task<bool> OutboxEventExistsAsync(Guid correlationId);
}
