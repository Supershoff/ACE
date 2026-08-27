using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudCustodyBoundary"/>'s crash-safe deposit handoff and its existing test-only
/// fault injector to <see cref="ICloudLedgerOutboxAtomicityHarness"/>, proving the shared
/// ledger/outbox atomicity suite (issue #10) is usable by a real MariaDB-backed adapter. Callers
/// must reset the Cloud schema between test methods (see the adopting <c>[TestInitialize]</c>) since
/// this harness's counts are unscoped, matching every other atomicity assertion in this project.
/// </summary>
internal sealed class PersistenceLedgerOutboxAtomicityHarness : ICloudLedgerOutboxAtomicityHarness
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;

    public PersistenceLedgerOutboxAtomicityHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<Guid> PerformCommittedMutationAsync()
    {
        var biotaId = _nextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, _shardId, Guid.NewGuid(), Guid.NewGuid());
        if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            throw new InvalidOperationException($"Expected a committed deposit but observed {outcome.Kind}: {outcome.Reason}");
        }

        return outcome.Value!.LedgerCorrelationId;
    }

    public async Task PerformMutationThatCrashesBeforeCommitAsync()
    {
        var biotaId = _nextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> crashBeforeCommit = point =>
            point == CloudBoundaryFaultPoint.BeforeCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await boundary.DepositAsync(biotaId, _shardId, Guid.NewGuid(), Guid.NewGuid(), crashBeforeCommit, CancellationToken.None);
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
}
