using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>One current character available to the caller for Acting Character selection (issue #39, VAULT-001), with its last-known monarch from the versioned identity/allegiance cache.</summary>
public sealed record CloudActingCharacterSummary(uint CharacterId, string CharacterName, uint? MonarchId);

/// <summary>
/// Lists the account's current characters for the Allegiance Vault's Acting Character selector
/// (VAULT-001: "Every action names one Acting Character"). This is a display/selection convenience
/// only, built from the same versioned, disposable <see cref="CloudCharacterIdentityReadProjection"/>
/// cache <see cref="CloudCharacterAllegianceVaultReader"/> already uses for read-scoping -- it never
/// substitutes for <see cref="CloudAllegianceVaultTransactionGateway"/>'s own live ace_shard
/// revalidation of the character actually chosen and submitted with a contribute/take command.
/// Interface-extracted (mirroring <see cref="ICloudCharacterAllegianceVaultReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake.
/// </summary>
public interface ICloudActingCharacterReader
{
    Task<IReadOnlyList<CloudActingCharacterSummary>> GetCurrentCharactersAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default);
}

/// <summary>See <see cref="ICloudActingCharacterReader"/>.</summary>
public sealed class CloudActingCharacterReader : ICloudActingCharacterReader
{
    private readonly CloudDbContext _context;

    public CloudActingCharacterReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<CloudActingCharacterSummary>> GetCurrentCharactersAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Listing Acting Characters requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Listing Acting Characters requires a real account ID.");
        }

        var rows = await _context.CloudCharacterIdentityReadProjections.AsNoTracking()
            .Where(row => row.ShardId == shardId && row.AccountId == accountId && row.CharacterName != null)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CloudActingCharacterSummary(row.CharacterId, row.CharacterName!, row.MonarchId))
            .OrderBy(row => row.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
