using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read-only diagnostics for the ACE world-boundary persistence gateway (ARCH-007, ARCH-009,
/// OPS-002): explicit database availability and component-version compatibility results, plus
/// Custody Outbox depth, for later health and recovery tooling to build on. Nothing here mutates
/// state or infers success/failure from anything other than an authoritative read, matching the
/// same "explicit result, never an inferred one" discipline <see cref="CloudCustodyBoundary"/>
/// already uses for mutations (transaction rule 8).
/// </summary>
public sealed class CloudGatewayDiagnostics
{
    private readonly CloudDbContext _context;

    public CloudGatewayDiagnostics(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Probes whether the Cloud schema database currently accepts a query (ARCH-009). A caller
    /// deciding whether to attempt a mutation should still handle
    /// <see cref="CloudBoundaryOutcomeKind.Unavailable"/> from the attempt itself -- this probe can
    /// go stale the instant after it returns -- but it lets health/recovery tooling report
    /// availability without waiting for a real mutation to fail first.
    /// </summary>
    public async Task<CloudGatewayAvailabilityResult> CheckDatabaseAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT 1;", cancellationToken);
            return CloudGatewayAvailabilityResult.Available();
        }
        catch (Exception ex) when (CloudBoundaryRetry.IsUnavailable(ex))
        {
            var mySqlException = CloudBoundaryRetry.UnwrapMySqlException(ex)!;
            return CloudGatewayAvailabilityResult.Unavailable($"The Cloud schema database is unavailable: {mySqlException.Message}");
        }
    }

    /// <summary>
    /// Compares this deployment's currently applied <see cref="CloudShardBinding"/> versions
    /// against <paramref name="expected"/> (OPS-002). Returns an incompatible result -- rather than
    /// throwing -- both when the versions genuinely differ and when no CloudShardBinding row exists
    /// yet (Operator Bootstrap has not run).
    /// </summary>
    public async Task<CloudCompatibilityResult> CheckProtocolCompatibilityAsync(
        CloudComponentVersions expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var binding = await _context.CloudShardBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (binding is null)
        {
            return CloudCompatibilityResult.Incompatible(
                CloudVersionComponent.CloudSchema, "This deployment has no CloudShardBinding row; Operator Bootstrap has not completed.");
        }

        var actual = new CloudComponentVersions(binding.AceExtensionVersion, binding.SchemaVersion, binding.ContractProtocolVersion);
        return CloudCompatibilityChecker.Evaluate(expected, actual);
    }

    /// <summary>
    /// How many Custody Outbox events remain after <paramref name="lastConsumedSequenceNumber"/>
    /// (pass 0 for "since the beginning"). Health/recovery tooling uses this to report how far a
    /// consumer (the companion web service or a background worker) has fallen behind, independent
    /// of whether that consumer is currently running at all (ARCH-007, ARCH-008).
    /// </summary>
    public async Task<int> GetPendingOutboxEventCountAsync(long lastConsumedSequenceNumber, CancellationToken cancellationToken = default)
    {
        if (lastConsumedSequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastConsumedSequenceNumber), "A sequence cursor cannot be negative.");
        }

        return await _context.CloudCustodyOutboxEvents
            .AsNoTracking()
            .CountAsync(e => e.SequenceNumber > lastConsumedSequenceNumber, cancellationToken);
    }
}
