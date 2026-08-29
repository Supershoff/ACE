using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static ACE.Cloud.Persistence.CloudRawSqlHelpers;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Records deduplicated, administrator-visible Icon Reconstruction diagnostics (UI-006). Uses
/// MariaDB's <c>INSERT ... ON DUPLICATE KEY UPDATE</c> upsert idiom, the same pattern
/// <c>CloudCustodyBoundary.EnsureAndLockPyrealRemainderAsync</c> and
/// <c>UpsertStackSizeAsync</c> already use, so two concurrent renders reporting the exact same
/// broken reference (same <see cref="CloudIconCompositionDiagnostic.DedupeKey"/>) always converge on
/// one row with an incremented count rather than racing to insert two.
/// </summary>
public sealed class CloudIconDiagnosticGateway
{
    private readonly CloudDbContext _context;

    public CloudIconDiagnosticGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task RecordAsync(
        string shardId, CloudIconCompositionDiagnostic diagnostic, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Recording an icon diagnostic requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(diagnostic);

        var connection = _context.Database.GetDbConnection();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO CloudIconDiagnostic
                    (Id, ShardId, DedupeKey, LayerKind, Did, Reason, OccurrenceCount, FirstSeenAtUtc, LastSeenAtUtc)
                VALUES
                    (@id, @shardId, @dedupeKey, @layerKind, @did, @reason, 1, @nowUtc, @nowUtc)
                ON DUPLICATE KEY UPDATE
                    OccurrenceCount = OccurrenceCount + 1,
                    LastSeenAtUtc = @nowUtc;
                """;
            AddParameter(command, "@id", Guid.NewGuid().ToString());
            AddParameter(command, "@shardId", shardId);
            AddParameter(command, "@dedupeKey", diagnostic.DedupeKey);
            AddParameter(command, "@layerKind", diagnostic.Layer.Kind.ToString());
            AddParameter(command, "@did", diagnostic.Layer.Did);
            AddParameter(command, "@reason", diagnostic.Reason.ToString());
            AddParameter(command, "@nowUtc", nowUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}
