using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudOwnershipTransferAuthority.TransferAsync(uint, Guid, int, Guid, CancellationToken)"/>
/// to <see cref="ICloudIdempotentCommandHarness{TEffect}"/>, proving the shared idempotent-command
/// suite (issue #10, adopted for issue #21's "ownership transfers" and "repeated idempotency keys"
/// Red section categories) is usable by a real MariaDB-backed adapter for a whole-item Cloud
/// ownership transfer, not only Deposit (<see cref="PersistenceIdempotentCommandHarness"/>). Each
/// distinct idempotency key transfers a freshly arranged Cloud Custody Record, since repeating an
/// already-seen key transfers that same key's record again -- exactly what proves the replay
/// behavior -- while transferring one record under two different keys would spuriously conflict on
/// the second key's stale expected version instead of exercising independent effects.
///
/// A concurrent pair of calls sharing one idempotency key must observe the very same arranged
/// record, not race each other to create two: <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> guarantees exactly one arrangement runs
/// per key and every caller -- including a racing one -- awaits that same completed arrangement
/// before calling <see cref="CloudOwnershipTransferAuthority.TransferAsync(uint, Guid, int, Guid, CancellationToken)"/>.
/// </summary>
internal sealed class PersistenceOwnershipTransferIdempotentCommandHarness : ICloudIdempotentCommandHarness<CloudCustodyRecord>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Lazy<Task<uint>>> _biotaIdByIdempotencyKey = [];

    public PersistenceOwnershipTransferIdempotentCommandHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<CloudCustodyRecord> ExecuteAsync(Guid idempotencyKey)
    {
        var biotaId = await GetOrArrangeCustodyRecordBiotaIdAsync(idempotencyKey);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, idempotencyKey);
        if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            throw new InvalidOperationException($"Expected a committed ownership transfer but observed {outcome.Kind}: {outcome.Reason}");
        }

        return outcome.Value!;
    }

    public async Task<int> CountCommittedEffectsAsync()
    {
        Lazy<Task<uint>>[] arrangements;
        lock (_gate)
        {
            arrangements = [.. _biotaIdByIdempotencyKey.Values];
        }

        var biotaIds = await Task.WhenAll(arrangements.Select(arrangement => arrangement.Value));

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudCustodyRecords.CountAsync(r => biotaIds.Contains(r.BiotaId));
    }

    public Guid IdentityOf(CloudCustodyRecord effect) => effect.Id;

    private Task<uint> GetOrArrangeCustodyRecordBiotaIdAsync(Guid idempotencyKey)
    {
        Lazy<Task<uint>> arrangement;
        lock (_gate)
        {
            if (!_biotaIdByIdempotencyKey.TryGetValue(idempotencyKey, out arrangement!))
            {
                arrangement = new Lazy<Task<uint>>(ArrangeCustodyRecordAsync, LazyThreadSafetyMode.ExecutionAndPublication);
                _biotaIdByIdempotencyKey[idempotencyKey] = arrangement;
            }
        }

        return arrangement.Value;
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
}
