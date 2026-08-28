namespace ACE.Cloud.Persistence;

/// <summary>
/// Reverse-lookup row from an Allegiance Vault's opaque owner identity
/// (<see cref="ACE.Cloud.Domain.CloudOwnerIdentity.ForAllegianceVault"/>, a deterministic one-way
/// hash) back to the monarch character it belongs to. The owner identity itself cannot be reversed,
/// so this binding is what lets an integrity check enumerate "every currently known Allegiance
/// Vault" to look for one whose monarch no longer exists (VAULT-005's out-of-band recovery case)
/// instead of only being able to check one already-known monarch at a time. Created lazily,
/// idempotently, the first time a vault identity is actually used (an emptiness check or a Vault
/// Absorption) -- never guessed or backfilled for a monarch who has never had vault activity.
/// </summary>
public sealed class CloudAllegianceVaultBinding
{
    private CloudAllegianceVaultBinding()
    {
    }

    public CloudAllegianceVaultBinding(Guid ownerId, string shardId, uint monarchCharacterId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An Allegiance Vault binding requires an owner ID.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Allegiance Vault binding requires a Cloud Shard ID.", nameof(shardId));
        }

        if (monarchCharacterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monarchCharacterId), "An Allegiance Vault binding requires a real monarch character GUID.");
        }

        OwnerId = ownerId;
        ShardId = shardId;
        MonarchCharacterId = monarchCharacterId;
    }

    public Guid OwnerId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint MonarchCharacterId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
