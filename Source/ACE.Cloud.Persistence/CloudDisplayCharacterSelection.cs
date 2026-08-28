using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The current Display Character pointer for one <see cref="CloudOwnershipGroup"/> (AUTH-003):
/// exactly one row per group, holding the immutable name snapshot ACE.Cloud.Contracts's public
/// projection exposes instead of the private ACE account name. Null <see cref="CharacterId"/>/
/// <see cref="CharacterName"/> is the "no-current-character" case: the group currently has no
/// current character anywhere in the Main/Linked roster.
/// </summary>
public sealed class CloudDisplayCharacterSelection
{
    private CloudDisplayCharacterSelection()
    {
    }

    public static CloudDisplayCharacterSelection Create(Guid ownershipGroupId, string shardId, CloudDisplayCharacterSelectionResult result, DateTime nowUtc)
    {
        if (ownershipGroupId == Guid.Empty)
        {
            throw new ArgumentException("A Display Character selection requires its ownership group ID.", nameof(ownershipGroupId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Display Character selection requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(result);

        return new CloudDisplayCharacterSelection
        {
            OwnershipGroupId = ownershipGroupId,
            ShardId = shardId,
            CharacterId = result.HasSelection ? result.CharacterId : null,
            CharacterName = result.HasSelection ? result.CharacterName : null,
            TotalLogins = result.HasSelection ? result.TotalLogins : null,
            Version = 1,
            SelectedAtUtc = nowUtc,
        };
    }

    public Guid OwnershipGroupId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint? CharacterId { get; private set; }

    public string? CharacterName { get; private set; }

    public int? TotalLogins { get; private set; }

    public int Version { get; private set; }

    public DateTime SelectedAtUtc { get; private set; }

    /// <summary>
    /// Replaces this pointer with a freshly computed selection (AUTH-003 reselection after a
    /// rename, deletion, link, or unlink). Callers must hold this row's lock; see
    /// <c>CloudDisplayCharacterGateway.ReselectAsync</c>.
    /// </summary>
    internal void ReplaceWith(CloudDisplayCharacterSelectionResult result, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(result);

        CharacterId = result.HasSelection ? result.CharacterId : null;
        CharacterName = result.HasSelection ? result.CharacterName : null;
        TotalLogins = result.HasSelection ? result.TotalLogins : null;
        Version++;
        SelectedAtUtc = nowUtc;
    }
}
