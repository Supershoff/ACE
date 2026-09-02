using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The <see cref="CloudAllegianceVaultTransactionGateway"/> mutation capabilities issue #39's
/// Allegiance Vault web endpoints need (VAULT-001..003). Interface-extracted for the same reason as
/// <see cref="ICloudTransferOfferService"/>: so <c>ACE.Cloud.Backend.Tests</c> can substitute an
/// in-memory fake instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudAllegianceVaultTransactionService
{
    Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> ContributeAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> TakeAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);
}
