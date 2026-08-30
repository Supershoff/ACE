using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Locks <see cref="CloudLiveStreamSequence"/>'s single row and returns the next durable Live State
/// Stream order position, within the caller's already-open transaction. Extracted from
/// <see cref="CloudCustodyProjectionConsumer"/> when <see cref="CloudNotificationProjectionConsumer"/>
/// became a second caller needing the exact same reservation (AGENTS.md: search for an existing
/// helper before accepting duplication).
/// </summary>
internal static class CloudLiveStreamSequenceReserver
{
    public static async Task<long> ReserveNextAsync(CloudDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        long reserved;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT NextValue FROM CloudLiveStreamSequence WHERE Id = 1 FOR UPDATE;";
            reserved = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE CloudLiveStreamSequence SET NextValue = @nextValue WHERE Id = 1;";
            var parameter = update.CreateParameter();
            parameter.ParameterName = "@nextValue";
            parameter.Value = reserved + 1;
            update.Parameters.Add(parameter);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }
}
