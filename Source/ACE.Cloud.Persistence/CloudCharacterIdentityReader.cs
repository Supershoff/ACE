using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>See <see cref="ICloudCharacterIdentityReader"/>.</summary>
public sealed class CloudCharacterIdentityReader : ICloudCharacterIdentityReader
{
    private readonly CloudDbContext _context;

    public CloudCharacterIdentityReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<CloudDisplayCharacterCandidate>> GetCandidatesAsync(
        string shardId, IReadOnlyCollection<uint> accountIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Gathering Display Character candidates requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(accountIds);

        if (accountIds.Count == 0)
        {
            return [];
        }

        var rows = await _context.CloudCharacterIdentityReadProjections.AsNoTracking()
            .Where(row => row.ShardId == shardId && row.AccountId != null && accountIds.Contains(row.AccountId!.Value) && row.CharacterName != null)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CloudDisplayCharacterCandidate(row.CharacterId, row.CharacterName!, row.TotalLogins ?? 0))
            .ToList();
    }
}
