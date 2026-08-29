using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudOwnershipTransferAuthority"/> to <see cref="ICloudLedgerOutboxAtomicityHarness"/>,
/// proving the shared ledger/outbox atomicity suite (issue #10, adopted for issue #21's "ownership
/// transfers" adapter category) is usable by a real MariaDB-backed adapter for the core custody
/// state model's "immediate cloud transfer" edge, not only Deposit
/// (<see cref="PersistenceLedgerOutboxAtomicityHarness"/>). The arranged Cloud Custody Record is
/// inserted directly (not through <see cref="CloudCustodyBoundary.DepositAsync"/>) so arranging never
/// itself appends a ledger/outbox row, keeping this suite's absolute counts meaningful.
/// </summary>
internal sealed class PersistenceOwnershipTransferLedgerOutboxAtomicityHarness : ICloudLedgerOutboxAtomicityHarness
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;

    public PersistenceOwnershipTransferLedgerOutboxAtomicityHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<Guid> PerformCommittedMutationAsync()
    {
        var biotaId = await ArrangeCustodyRecordAsync();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());
        if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            throw new InvalidOperationException($"Expected a committed ownership transfer but observed {outcome.Kind}: {outcome.Reason}");
        }

        return await GetTransferCorrelationIdAsync(biotaId);
    }

    public async Task PerformMutationThatCrashesBeforeCommitAsync()
    {
        var biotaId = await ArrangeCustodyRecordAsync();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        Func<CloudBoundaryFaultPoint, Task> crashBeforeCommit = point =>
            point == CloudBoundaryFaultPoint.BeforeCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid(), crashBeforeCommit, CancellationToken.None);
    }

    public async Task<int> CountLedgerEventsAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudActivityLedgerEvents.CountAsync();
    }

    public async Task<int> CountOutboxEventsAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudCustodyOutboxEvents.CountAsync();
    }

    public async Task<bool> LedgerEventExistsAsync(Guid correlationId)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudActivityLedgerEvents.AnyAsync(e => e.CorrelationId == correlationId);
    }

    public async Task<bool> OutboxEventExistsAsync(Guid correlationId)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudCustodyOutboxEvents.AnyAsync(e => e.CorrelationId == correlationId);
    }

    private async Task<uint> ArrangeCustodyRecordAsync()
    {
        var biotaId = _nextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        context.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, _shardId, Guid.NewGuid(), Guid.NewGuid()));
        await context.SaveChangesAsync();

        return biotaId;
    }

    private async Task<Guid> GetTransferCorrelationIdAsync(uint biotaId)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudActivityLedgerEvents.AsNoTracking()
            .Where(e => e.EventType == CloudBoundaryOperationType.OwnershipTransfer && e.BiotaId == biotaId)
            .Select(e => e.CorrelationId)
            .SingleAsync();
    }
}
