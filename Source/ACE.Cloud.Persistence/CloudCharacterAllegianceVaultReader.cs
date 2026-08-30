using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudInventoryQueryReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for the Vault-scoped Activity
/// Ledger endpoint test.
/// </summary>
public interface ICloudCharacterAllegianceVaultReader
{
    Task<IReadOnlyList<Guid>> GetCurrentAllegianceVaultOwnerIdsAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the Allegiance Vault owner ID(s) (VAULT-001, <see cref="CloudOwnerIdentity.ForAllegianceVault"/>)
/// for every allegiance one ACE account's characters currently belong to, from the versioned
/// character/allegiance cache CONTEXT.md permits for exactly this kind of read
/// ("a cache is permitted only when it is versioned/refreshed from ACE"). This is a read-scoping
/// convenience only -- it decides which vault owner IDs a ledger/notification query is allowed to
/// include, never who may contribute/take/absorb a vault, so relying on the cache here does not
/// weaken VAULT-001's own revalidation discipline for actual vault mutations.
/// </summary>
public sealed class CloudCharacterAllegianceVaultReader : ICloudCharacterAllegianceVaultReader
{
    private readonly CloudDbContext _context;

    public CloudCharacterAllegianceVaultReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<Guid>> GetCurrentAllegianceVaultOwnerIdsAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Resolving Allegiance Vault owner IDs requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Resolving Allegiance Vault owner IDs requires a real account ID.");
        }

        var monarchIds = await _context.CloudCharacterIdentityReadProjections.AsNoTracking()
            .Where(row => row.ShardId == shardId && row.AccountId == accountId && row.MonarchId != null)
            .Select(row => row.MonarchId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return monarchIds.ConvertAll(monarchId => CloudOwnerIdentity.ForAllegianceVault(shardId, monarchId));
    }
}
