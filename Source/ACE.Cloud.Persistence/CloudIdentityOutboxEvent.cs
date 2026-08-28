using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One durable identity/allegiance Custody Outbox entry (ARCH-007 applied to AUTH-003/VAULT-001
/// rather than a custody handoff): a character rename/deletion or an allegiance swear/break/monarch
/// change, published from an authoritative ACE seam so the companion web can refresh its identity
/// and vault-eligibility projections idempotently, without depending on web availability at commit
/// time and without becoming the authority for either fact. Kept in a table separate from
/// <see cref="CloudCustodyOutboxEvent"/> because these events have no native biota/custody owner --
/// forcing them into that table's biota-shaped columns would misrepresent what they are.
/// </summary>
public sealed class CloudIdentityOutboxEvent
{
    private CloudIdentityOutboxEvent()
    {
    }

    /// <summary>Creates a character rename/deletion event (AUTH-003).</summary>
    public static CloudIdentityOutboxEvent ForCharacterEvent(
        Guid correlationId,
        string shardId,
        CloudIdentityEventType eventType,
        uint characterId,
        uint accountId,
        string characterName,
        int totalLogins,
        long sequenceNumber)
    {
        if (eventType != CloudIdentityEventType.CharacterRenamed && eventType != CloudIdentityEventType.CharacterDeleted)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not a character identity event type.");
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A character identity event requires a real account ID.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("A character identity event requires a character name snapshot.", nameof(characterName));
        }

        var evt = Create(correlationId, shardId, eventType, characterId, sequenceNumber);
        evt.AccountId = accountId;
        evt.CharacterName = characterName;
        evt.TotalLogins = totalLogins;
        return evt;
    }

    /// <summary>Creates an allegiance swear/break/monarch-change event (VAULT-001).</summary>
    public static CloudIdentityOutboxEvent ForAllegianceEvent(
        Guid correlationId,
        string shardId,
        CloudIdentityEventType eventType,
        uint characterId,
        uint? monarchId,
        uint? priorMonarchId,
        long sequenceNumber)
    {
        if (eventType != CloudIdentityEventType.AllegianceSworn
            && eventType != CloudIdentityEventType.AllegianceBroken
            && eventType != CloudIdentityEventType.AllegianceMonarchChanged)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not an allegiance event type.");
        }

        var evt = Create(correlationId, shardId, eventType, characterId, sequenceNumber);
        evt.MonarchId = monarchId;
        evt.PriorMonarchId = priorMonarchId;
        return evt;
    }

    private static CloudIdentityOutboxEvent Create(
        Guid correlationId, string shardId, CloudIdentityEventType eventType, uint characterId, long sequenceNumber)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An identity outbox event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An identity outbox event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "An identity outbox event requires a real character GUID.");
        }

        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "An identity outbox event requires a positive sequence number.");
        }

        return new CloudIdentityOutboxEvent
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            ShardId = shardId,
            EventType = eventType,
            CharacterId = characterId,
            SequenceNumber = sequenceNumber,
        };
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudIdentityEventType EventType { get; private set; }

    public uint CharacterId { get; private set; }

    /// <summary>Set only for a character identity event; null for an allegiance event.</summary>
    public uint? AccountId { get; private set; }

    /// <summary>Set only for a character identity event; null for an allegiance event.</summary>
    public string? CharacterName { get; private set; }

    /// <summary>Set only for a character identity event; null for an allegiance event.</summary>
    public int? TotalLogins { get; private set; }

    /// <summary>Set only for an allegiance event; null for a character identity event.</summary>
    public uint? MonarchId { get; private set; }

    /// <summary>Set only for an allegiance event; null for a character identity event.</summary>
    public uint? PriorMonarchId { get; private set; }

    /// <summary>
    /// This event's position in the durable total order the companion web replays this outbox in,
    /// assigned within the same transaction as its commit by <see cref="CloudIdentityOutboxSequence"/>
    /// (mirrors <see cref="CloudCustodyOutboxEvent.SequenceNumber"/>'s exact role and guarantee).
    /// </summary>
    public long SequenceNumber { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
