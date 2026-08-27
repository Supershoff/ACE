using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// A minimal, storage-agnostic reference implementation of
/// <see cref="ICloudLedgerOutboxAtomicityHarness"/>: an in-process "transaction" is just a staged
/// list that is only copied into the durable stores when <c>commit</c> is reached, and never copied
/// at all when the simulated crash throws first -- modeling exactly what a real database
/// transaction rollback guarantees.
/// </summary>
public sealed class InMemoryLedgerOutboxAtomicityHarness : ICloudLedgerOutboxAtomicityHarness
{
    private readonly List<Guid> _ledgerEvents = [];
    private readonly List<Guid> _outboxEvents = [];

    public Task<Guid> PerformCommittedMutationAsync()
    {
        var correlationId = Guid.NewGuid();

        // Both writes are staged, then committed together -- there is no window in which only one
        // of them is durable.
        var stagedLedger = new List<Guid>(_ledgerEvents) { correlationId };
        var stagedOutbox = new List<Guid>(_outboxEvents) { correlationId };

        Commit(stagedLedger, stagedOutbox);

        return Task.FromResult(correlationId);
    }

    public Task PerformMutationThatCrashesBeforeCommitAsync()
    {
        // Staging happens, exactly like a real transaction's writes, but the process "crashes"
        // before the commit step that would have made either staged write durable.
        var correlationId = Guid.NewGuid();
        var stagedLedger = new List<Guid>(_ledgerEvents) { correlationId };
        var stagedOutbox = new List<Guid>(_outboxEvents) { correlationId };
        _ = stagedLedger;
        _ = stagedOutbox;

        throw new InvalidOperationException("Simulated crash before commit.");
    }

    public Task<int> CountLedgerEventsAsync() => Task.FromResult(_ledgerEvents.Count);

    public Task<int> CountOutboxEventsAsync() => Task.FromResult(_outboxEvents.Count);

    public Task<bool> LedgerEventExistsAsync(Guid correlationId) => Task.FromResult(_ledgerEvents.Contains(correlationId));

    public Task<bool> OutboxEventExistsAsync(Guid correlationId) => Task.FromResult(_outboxEvents.Contains(correlationId));

    private void Commit(List<Guid> stagedLedger, List<Guid> stagedOutbox)
    {
        _ledgerEvents.Clear();
        _ledgerEvents.AddRange(stagedLedger);
        _outboxEvents.Clear();
        _outboxEvents.AddRange(stagedOutbox);
    }
}
