using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The ACE-side gateway that publishes character/allegiance identity events into the durable
/// identity/allegiance outbox (issue #17: AUTH-003, VAULT-001, ARCH-007). Kept separate from
/// <see cref="CloudCustodyBoundary"/> -- which
/// <see cref="ACE.Cloud.RepositoryPolicyTests.CloudWorldBoundaryAuthoritySurfaceTests"/> proves
/// exposes only ARCH-002's native-biota deposit/withdrawal boundary operations -- because publishing
/// a character rename/deletion or allegiance change touches no native biota or Cloud Custody Record
/// at all; it is authoritative-fact reporting, not a world-boundary handoff. Callers must be ACE-side
/// code (an authoritative character/allegiance seam), exactly like <see cref="CloudCustodyBoundary"/>.
/// </summary>
public sealed class CloudIdentityEventGateway
{
    private readonly CloudDbContext _context;

    public CloudIdentityEventGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Publishes a character rename/deletion event (AUTH-003).</summary>
    public async Task<CloudIdentityOutboxEvent> PublishCharacterIdentityEventAsync(
        string shardId,
        CloudIdentityEventType eventType,
        uint characterId,
        uint accountId,
        string characterName,
        int totalLogins,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var sequenceNumber = await ReserveNextSequenceNumberAsync(cancellationToken);
        var evt = CloudIdentityOutboxEvent.ForCharacterEvent(
            correlationId, shardId, eventType, characterId, accountId, characterName, totalLogins, sequenceNumber);
        _context.CloudIdentityOutboxEvents.Add(evt);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return evt;
    }

    /// <summary>Publishes an allegiance swear/break/monarch-change event (VAULT-001).</summary>
    public async Task<CloudIdentityOutboxEvent> PublishAllegianceEventAsync(
        string shardId,
        CloudIdentityEventType eventType,
        uint characterId,
        uint? monarchId,
        uint? priorMonarchId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var sequenceNumber = await ReserveNextSequenceNumberAsync(cancellationToken);
        var evt = CloudIdentityOutboxEvent.ForAllegianceEvent(
            correlationId, shardId, eventType, characterId, monarchId, priorMonarchId, sequenceNumber);
        _context.CloudIdentityOutboxEvents.Add(evt);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return evt;
    }

    /// <summary>
    /// Locks <see cref="CloudIdentityOutboxSequence"/>'s single row and returns the next durable
    /// order position, the identity-outbox analog of the same locking approach
    /// <see cref="CloudCustodyOutboxSequence"/> uses for the custody outbox.
    /// </summary>
    private async Task<long> ReserveNextSequenceNumberAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        long reserved;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT NextValue FROM CloudIdentityOutboxSequence WHERE Id = 1 FOR UPDATE;";
            reserved = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE CloudIdentityOutboxSequence SET NextValue = @nextValue WHERE Id = 1;";
            CloudRawSqlHelpers.AddParameter(update, "@nextValue", reserved + 1);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }
}
