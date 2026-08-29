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
                    (Id, ShardId, DedupeKey, LayerKind, Did, Reason, OccurrenceCount, FirstSeenAtUtc, LastSeenAtUtc, LastSeenManifestVersion)
                VALUES
                    (@id, @shardId, @dedupeKey, @layerKind, @did, @reason, 1, @nowUtc, @nowUtc, @manifestVersion)
                ON DUPLICATE KEY UPDATE
                    OccurrenceCount = OccurrenceCount + 1,
                    LastSeenAtUtc = @nowUtc,
                    LastSeenManifestVersion = @manifestVersion;
                """;
            AddParameter(command, "@id", Guid.NewGuid().ToString());
            AddParameter(command, "@shardId", shardId);
            AddParameter(command, "@dedupeKey", diagnostic.DedupeKey);
            AddParameter(command, "@layerKind", diagnostic.Layer.Kind.ToString());
            AddParameter(command, "@did", diagnostic.Layer.Did);
            AddParameter(command, "@reason", diagnostic.Reason.ToString());
            AddParameter(command, "@nowUtc", nowUtc);
            AddParameter(command, "@manifestVersion", diagnostic.ManifestVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Lists diagnostics for exactly one shard, most-recently-seen first (issue #28's Red requirement:
    /// "diagnostics access control"). The only access-control boundary this persistence-layer gateway
    /// can enforce is shard scoping -- it is structurally impossible to leak another shard's
    /// diagnostics through this query, regardless of what a caller (e.g. a future admin web endpoint)
    /// passes for every other filter. That caller remains responsible for its own ADM-001 accessLevel
    /// revalidation before it ever reaches this gateway.
    /// </summary>
    public async Task<IReadOnlyList<CloudIconDiagnostic>> GetForShardAsync(
        string shardId, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Listing icon diagnostics requires a Cloud Shard ID.", nameof(shardId));
        }

        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        return await _context.Set<CloudIconDiagnostic>().AsNoTracking()
            .Where(d => d.ShardId == shardId)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }
}
