using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of the World Boundary Authority's deposit path (ARCH-002, ARCH-006).
/// Callers must be ACE world-boundary code holding a connection privileged to read ace_shard; the
/// narrowly privileged companion web identity (ARCH-004) must never be given this class.
///
/// This is a complementary, commit-time revalidation layer, not the only enforcement: the
/// ace_shard/ace_cloud triggers added by the AddCloudCustodyRecords migration already reject a
/// conflicting deposit at the database level (MariaDB CHECK constraints cannot express that
/// cross-schema lookup, so triggers are the primary database constraint). Revalidating here too
/// means a missing/misconfigured trigger cannot silently admit a conflict, and callers observe a
/// typed <see cref="CloudCustodyConflictException"/> instead of a raw provider exception.
/// </summary>
public sealed class CloudCustodyBoundary
{
    private readonly CloudDbContext _context;

    public CloudCustodyBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CloudCustodyRecord> DepositAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid ledgerCorrelationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await HasWorldPossessionAsync(biotaId, cancellationToken))
        {
            throw new CloudCustodyConflictException(
                $"Biota {biotaId} currently has world possession (Container, Wielder, or Location) and cannot enter Cloud custody.");
        }

        var record = new CloudCustodyRecord(biotaId, shardId, ownerId, ledgerCorrelationId);
        _context.CloudCustodyRecords.Add(record);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return record;
    }

    private async Task<bool> HasWorldPossessionAsync(uint biotaId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT
                EXISTS (
                    SELECT 1 FROM ace_shard.biota_properties_i_i_d
                    WHERE object_Id = @biotaId AND type IN (2, 3)
                    FOR UPDATE
                )
                OR EXISTS (
                    SELECT 1 FROM ace_shard.biota_properties_position
                    WHERE object_Id = @biotaId AND position_Type = 1
                    FOR UPDATE
                );
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@biotaId";
        parameter.Value = biotaId;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull && Convert.ToInt64(result) != 0;
    }
}
