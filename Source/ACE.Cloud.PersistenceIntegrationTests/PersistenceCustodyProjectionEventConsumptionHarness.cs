using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudInventoryReadProjection.TryApply"/> to
/// <see cref="ICloudEventConsumptionHarness{TPayload}"/>, proving issue #22's real MariaDB-backed
/// custody projection row obeys the exact same duplicate/out-of-order idempotency contract already
/// proven abstractly by <see cref="CloudEventConsumptionInvariantSuite{TPayload}"/> and by the
/// in-memory reference implementation (see <see cref="ACE.Cloud.TestKit.Tests.InMemoryEventConsumptionHarness{TPayload}"/>).
/// Applies directly against one fixed <see cref="CloudInventoryReadProjection"/> row rather than
/// through <see cref="CloudCustodyProjectionConsumer"/>'s outbox-ordered batch reader, so the suite
/// can exercise delivery orders (newer arriving before older) the reader itself would never produce
/// on its own.
/// </summary>
public sealed class PersistenceCustodyProjectionEventConsumptionHarness : ICloudEventConsumptionHarness<CloudCustodyOutboxEventPayload>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly CloudShardId _shardId;
    private readonly uint _biotaId;

    public PersistenceCustodyProjectionEventConsumptionHarness(CloudDatabaseFixture fixture, string shardId, uint biotaId)
    {
        _fixture = fixture;
        _shardId = new CloudShardId(shardId);
        _biotaId = biotaId;
    }

    public async Task ApplyAsync(CloudEventEnvelope<CloudCustodyOutboxEventPayload> envelope)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var current = await context.CloudInventoryReadProjections.SingleOrDefaultAsync(row => row.BiotaId == _biotaId);
        var (row, applied) = CloudInventoryReadProjection.TryApply(
            current,
            _biotaId,
            _shardId.Value,
            envelope.Payload.OwnerId.Value,
            CloudBoundaryOperationType.Deposit,
            envelope.Version.Value);

        if (applied && current is null)
        {
            context.CloudInventoryReadProjections.Add(row);
        }

        await context.SaveChangesAsync();
    }

    public async Task<CloudAggregateVersion?> GetAppliedVersionAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var row = await context.CloudInventoryReadProjections.AsNoTracking().SingleOrDefaultAsync(row => row.BiotaId == _biotaId);
        return row is null ? null : new CloudAggregateVersion((int)row.LastAppliedSequenceNumber);
    }

    public CloudEventEnvelope<CloudCustodyOutboxEventPayload> CreateEnvelope(CloudAggregateVersion version, CloudCustodyOutboxEventPayload payload) =>
        new(_shardId, version, new CloudIdempotencyKey(Guid.NewGuid()), DateTimeOffset.UtcNow, payload);
}
