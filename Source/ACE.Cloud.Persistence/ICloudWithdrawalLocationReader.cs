using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The read half of <see cref="CloudWithdrawalLocationConfigurationBoundary"/> issue #33's
/// withdrawal-creation UI needs for location guidance (WDR-006). Interface-extracted for the same
/// reason as <see cref="ICloudAccountOwnershipResolver"/>: so <c>ACE.Cloud.Backend.Tests</c> can
/// substitute an in-memory fake instead of standing up a real MariaDB-backed
/// <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudWithdrawalLocationReader
{
    Task<CloudWithdrawalLocationConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default);
}
