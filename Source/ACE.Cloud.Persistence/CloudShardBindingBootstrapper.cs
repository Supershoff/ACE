using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Idempotently creates or strictly validates the singleton <see cref="CloudShardBinding"/> row
/// (ARCH-001) for the disposable local acceptance stack (issue #34's blocking defect #2):
/// <c>ACE.Cloud.LocalAcceptanceMigrator</c> applies schema migrations but never seeded this row,
/// leaving <c>CloudCustodianManager</c> and every companion service's startup checks permanently
/// reporting "Operator Bootstrap has not completed" even immediately after a fresh migrate. The first
/// call inserts the row; every later call with identical values is a no-op success
/// (<see cref="CloudShardBindingBootstrapResult.AlreadyMatches"/>); a call whose ShardId or versions
/// genuinely differ from the existing row never overwrites it -- it throws
/// <see cref="CloudShardBindingMismatchException"/> instead, the same "never silently rewrite a
/// different shard's identity" discipline ARCH-001 requires of the real production Operator Bootstrap
/// command this stands in for locally.
/// </summary>
public static class CloudShardBindingBootstrapper
{
    public static async Task<CloudShardBindingBootstrapResult> BootstrapAsync(
        string connectionString,
        string shardId,
        string schemaVersion,
        string aceExtensionVersion,
        string contractProtocolVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Cloud Shard ID is required.", nameof(shardId));
        }

        if (string.IsNullOrWhiteSpace(schemaVersion) || string.IsNullOrWhiteSpace(aceExtensionVersion) || string.IsNullOrWhiteSpace(contractProtocolVersion))
        {
            throw new ArgumentException("SchemaVersion, AceExtensionVersion, and ContractProtocolVersion are all required.");
        }

        var options = CloudDbContextOptionsFactory.Create(connectionString);
        await using var context = new CloudDbContext(options);

        var existing = await context.CloudShardBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Validate(existing, shardId, schemaVersion, aceExtensionVersion, contractProtocolVersion);
        }

        var binding = new CloudShardBinding(shardId, schemaVersion, aceExtensionVersion, contractProtocolVersion);
        context.CloudShardBindings.Add(binding);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CloudShardBindingBootstrapResult.Created();
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Race-safe the same way CloudCustodianConfigurationBoundary.GetCurrentAsync is: a losing
            // concurrent bootstrap validates against the winner's committed row instead of erroring.
            context.ChangeTracker.Clear();
            var winner = await context.CloudShardBindings.AsNoTracking().SingleAsync(cancellationToken);
            return Validate(winner, shardId, schemaVersion, aceExtensionVersion, contractProtocolVersion);
        }
    }

    private static CloudShardBindingBootstrapResult Validate(
        CloudShardBinding existing,
        string shardId,
        string schemaVersion,
        string aceExtensionVersion,
        string contractProtocolVersion)
    {
        if (existing.ShardId == shardId
            && existing.SchemaVersion == schemaVersion
            && existing.AceExtensionVersion == aceExtensionVersion
            && existing.ContractProtocolVersion == contractProtocolVersion)
        {
            return CloudShardBindingBootstrapResult.AlreadyMatches();
        }

        throw new CloudShardBindingMismatchException(
            "An existing CloudShardBinding row does not match the requested identity/versions. " +
            $"Existing: ShardId={existing.ShardId}, SchemaVersion={existing.SchemaVersion}, " +
            $"AceExtensionVersion={existing.AceExtensionVersion}, ContractProtocolVersion={existing.ContractProtocolVersion}. " +
            $"Requested: ShardId={shardId}, SchemaVersion={schemaVersion}, " +
            $"AceExtensionVersion={aceExtensionVersion}, ContractProtocolVersion={contractProtocolVersion}. " +
            "This tool never rewrites an existing shard binding -- point this acceptance stack at a fresh " +
            "disposable ace_cloud database, or fix the mismatched acceptance.settings.json value.");
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };
}
