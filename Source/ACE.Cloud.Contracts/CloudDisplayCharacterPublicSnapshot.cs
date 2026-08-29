using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The privacy-safe public projection of one ownership group's current Display Character
/// (AUTH-001: "Account names never appear publicly"). Carries no ACE account name, account ID, or
/// character ID -- only the name snapshot a public page or event may show, proven so by the
/// public-contract privacy sweep, not merely by convention.
/// </summary>
public sealed record CloudDisplayCharacterPublicSnapshot : ICloudPublicContract
{
    public CloudShardId ShardId { get; }

    public string DisplayCharacterName { get; }

    public CloudDisplayCharacterPublicSnapshot(CloudShardId shardId, string displayCharacterName)
    {
        ArgumentNullException.ThrowIfNull(shardId);

        if (string.IsNullOrWhiteSpace(displayCharacterName))
        {
            throw new ArgumentException("A public Display Character snapshot requires a character name.", nameof(displayCharacterName));
        }

        ShardId = shardId;
        DisplayCharacterName = displayCharacterName;
    }
}
