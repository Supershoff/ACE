using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudAllegianceVaultTransactionService"/> substitute exercising the same
/// "the acting character must currently belong to an allegiance" shape as the real gateway, so
/// Allegiance Vault endpoint tests exercise real routing/authorization without requiring a real
/// MariaDB (the full live-revalidation, quota, and equal-privilege behavior is proven separately by
/// <c>ACE.Cloud.PersistenceIntegrationTests.CloudAllegianceVaultTransactionGatewayTests</c>).
/// </summary>
internal sealed class FakeCloudAllegianceVaultTransactionService : ICloudAllegianceVaultTransactionService
{
    private readonly Dictionary<uint, uint> _monarchIdByCharacterId = [];
    private readonly Dictionary<uint, Guid> _personalOwnerByBiotaId = [];
    private readonly Dictionary<uint, uint> _vaultMonarchByBiotaId = [];

    public void SetCharacterMonarch(uint characterId, uint monarchId) => _monarchIdByCharacterId[characterId] = monarchId;

    public void SeedPersonalItem(uint biotaId, Guid ownerId) => _personalOwnerByBiotaId[biotaId] = ownerId;

    public void SeedVaultItem(uint biotaId, uint monarchId) => _vaultMonarchByBiotaId[biotaId] = monarchId;

    public Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> ContributeAsync(
        string shardId, uint callerAccountId, uint actingCharacterId, CloudReservationTarget target, Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!_monarchIdByCharacterId.TryGetValue(actingCharacterId, out var monarchId))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Character {actingCharacterId} does not currently belong to an allegiance."));
        }

        var biotaId = target.ItemId!.Value;
        var callerOwnerId = CloudOwnerIdentity.ForAccount(shardId, callerAccountId);
        if (!_personalOwnerByBiotaId.TryGetValue(biotaId, out var currentOwnerId) || currentOwnerId != callerOwnerId)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict("The caller does not own this item."));
        }

        _personalOwnerByBiotaId.Remove(biotaId);
        _vaultMonarchByBiotaId[biotaId] = monarchId;

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(shardId, monarchId);
        return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Committed(
            new CloudAllegianceVaultTransferResult(biotaId, callerOwnerId, vaultOwnerId)));
    }

    public Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> TakeAsync(
        string shardId, uint callerAccountId, uint actingCharacterId, CloudReservationTarget target, Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!_monarchIdByCharacterId.TryGetValue(actingCharacterId, out var monarchId))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Character {actingCharacterId} does not currently belong to an allegiance."));
        }

        var biotaId = target.ItemId!.Value;
        if (!_vaultMonarchByBiotaId.TryGetValue(biotaId, out var currentMonarchId) || currentMonarchId != monarchId)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict("This item is not in the acting character's Allegiance Vault."));
        }

        var callerOwnerId = CloudOwnerIdentity.ForAccount(shardId, callerAccountId);
        _vaultMonarchByBiotaId.Remove(biotaId);
        _personalOwnerByBiotaId[biotaId] = callerOwnerId;

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(shardId, monarchId);
        return Task.FromResult(CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Committed(
            new CloudAllegianceVaultTransferResult(biotaId, callerOwnerId, vaultOwnerId)));
    }
}
