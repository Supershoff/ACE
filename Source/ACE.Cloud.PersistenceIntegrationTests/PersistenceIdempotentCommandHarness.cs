using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudCustodyBoundary.DepositAsync(uint, string, Guid, Guid, CancellationToken)"/>
/// to <see cref="ICloudIdempotentCommandHarness{TEffect}"/>, proving the shared idempotent-command
/// suite (issue #10) is usable by a real MariaDB-backed adapter -- not only the in-memory adopter in
/// ACE.Cloud.TestKit.Tests. Each distinct idempotency key deposits a fresh native biota, since a
/// Cloud Custody Record's uniqueness is scoped per biota (INV-001), not per idempotency key;
/// repeating an already-seen key deposits that same key's biota again, which is exactly what proves
/// the replay behavior.
/// </summary>
internal sealed class PersistenceIdempotentCommandHarness : ICloudIdempotentCommandHarness<CloudCustodyRecord>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;
    private readonly Guid _ownerId = Guid.NewGuid();

    private readonly object _gate = new();
    private readonly Dictionary<Guid, uint> _biotaIdByIdempotencyKey = [];

    public PersistenceIdempotentCommandHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<CloudCustodyRecord> ExecuteAsync(Guid idempotencyKey)
    {
        bool isNewKey;
        uint biotaId;
        lock (_gate)
        {
            isNewKey = !_biotaIdByIdempotencyKey.TryGetValue(idempotencyKey, out biotaId);
            if (isNewKey)
            {
                biotaId = _nextBiotaId();
                _biotaIdByIdempotencyKey[idempotencyKey] = biotaId;
            }
        }

        if (isNewKey)
        {
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        }

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, _shardId, _ownerId, idempotencyKey);
        if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            throw new InvalidOperationException($"Expected a committed deposit but observed {outcome.Kind}: {outcome.Reason}");
        }

        return outcome.Value!;
    }

    public async Task<int> CountCommittedEffectsAsync()
    {
        uint[] biotaIds;
        lock (_gate)
        {
            biotaIds = [.. _biotaIdByIdempotencyKey.Values];
        }

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudCustodyRecords.CountAsync(r => biotaIds.Contains(r.BiotaId));
    }

    public Guid IdentityOf(CloudCustodyRecord effect) => effect.Id;
}
