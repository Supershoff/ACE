namespace ACE.Cloud.Persistence;

/// <summary>
/// One immutable, append-only snapshot of a <see cref="CloudDisplayCharacterSelection"/> change
/// (AUTH-003: "audit records retain immutable IDs and name snapshots"). Written in the same
/// transaction as the pointer update it describes (transaction rule 5); there is no update or
/// delete path.
/// </summary>
public sealed class CloudDisplayCharacterSelectionHistoryEvent
{
    private CloudDisplayCharacterSelectionHistoryEvent()
    {
    }

    public CloudDisplayCharacterSelectionHistoryEvent(
        Guid correlationId,
        string shardId,
        Guid ownershipGroupId,
        CloudDisplayCharacterSelectionReason reason,
        uint? characterId,
        string? characterName,
        int? totalLogins)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A Display Character selection history event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Display Character selection history event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownershipGroupId == Guid.Empty)
        {
            throw new ArgumentException("A Display Character selection history event requires its ownership group ID.", nameof(ownershipGroupId));
        }

        if (characterId is null != characterName is null)
        {
            throw new ArgumentException(
                "A Display Character selection history event's character ID and name snapshot must both be present or both be absent.");
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        OwnershipGroupId = ownershipGroupId;
        Reason = reason;
        CharacterId = characterId;
        CharacterName = characterName;
        TotalLogins = totalLogins;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnershipGroupId { get; private set; }

    public CloudDisplayCharacterSelectionReason Reason { get; private set; }

    /// <summary>Null for a "no current character" selection.</summary>
    public uint? CharacterId { get; private set; }

    /// <summary>Null for a "no current character" selection.</summary>
    public string? CharacterName { get; private set; }

    public int? TotalLogins { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
