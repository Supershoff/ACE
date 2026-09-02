using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One durable identity/allegiance Custody Outbox entry (ARCH-007 applied to AUTH-003/VAULT-001
/// rather than a custody handoff): a character rename/deletion, an allegiance swear/break/monarch
/// change, or (issue #39's self-heal fix) a character-login-observed snapshot, published from an
/// authoritative ACE seam so the companion web can refresh its identity and vault-eligibility
/// projections idempotently, without depending on web availability at commit time and without
/// becoming the authority for either fact. Kept in a table separate from
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

    /// <summary>
    /// Creates an allegiance swear/break/monarch-change event (VAULT-001). Issue #39's oath-first fix:
    /// also carries the authoritative account/name/login snapshot ACE already holds for the character
    /// at publish time, the same snapshot a character rename/deletion event carries, so an allegiance
    /// event can be the very first event a fresh/rebuilt Cloud database ever sees for a character and
    /// still produce a projection row visible to its own account.
    /// </summary>
    public static CloudIdentityOutboxEvent ForAllegianceEvent(
        Guid correlationId,
        string shardId,
        CloudIdentityEventType eventType,
        uint characterId,
        uint? monarchId,
        uint? priorMonarchId,
        uint accountId,
        string characterName,
        int totalLogins,
        long sequenceNumber)
    {
        if (eventType != CloudIdentityEventType.AllegianceSworn
            && eventType != CloudIdentityEventType.AllegianceBroken
            && eventType != CloudIdentityEventType.AllegianceMonarchChanged)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not an allegiance event type.");
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An allegiance event requires a real account ID.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("An allegiance event requires a character name snapshot.", nameof(characterName));
        }

        var evt = Create(correlationId, shardId, eventType, characterId, sequenceNumber);
        evt.MonarchId = monarchId;
        evt.PriorMonarchId = priorMonarchId;
        evt.AccountId = accountId;
        evt.CharacterName = characterName;
        evt.TotalLogins = totalLogins;
        return evt;
    }

    /// <summary>
    /// Creates a character-login-observed snapshot event (issue #39's self-heal fix): published on
    /// every successful world login while AC Cloud Mule is enabled, carrying the same authoritative
    /// account/name/login snapshot the other event shapes carry, plus the character's current monarch.
    /// This is not a swear/break/monarch-change -- <paramref name="monarchId"/> is simply whatever the
    /// character's monarch happens to be right now, observed rather than changed -- which is exactly
    /// why it can repair a projection row left behind by a pre-oath-first-fix allegiance event (null
    /// account/name) or by any other stale monarch pointer, without an allegiance mutation needing to
    /// happen first. There is no "prior" monarch for an observation, so unlike an allegiance event this
    /// never sets <see cref="PriorMonarchId"/>.
    /// </summary>
    public static CloudIdentityOutboxEvent ForCharacterLoginObservedEvent(
        Guid correlationId,
        string shardId,
        uint characterId,
        uint? monarchId,
        uint accountId,
        string characterName,
        int totalLogins,
        long sequenceNumber)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A character-login-observed event requires a real account ID.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("A character-login-observed event requires a character name snapshot.", nameof(characterName));
        }

        var evt = Create(correlationId, shardId, CloudIdentityEventType.CharacterLoginObserved, characterId, sequenceNumber);
        evt.MonarchId = monarchId;
        evt.AccountId = accountId;
        evt.CharacterName = characterName;
        evt.TotalLogins = totalLogins;
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

    /// <summary>
    /// The authoritative account/name/login snapshot ACE held for this character at publish time. Set
    /// for every event this outbox carries -- character identity events, allegiance events (since
    /// issue #39's oath-first fix), and character-login-observed events alike.
    /// </summary>
    public uint? AccountId { get; private set; }

    /// <summary>See <see cref="AccountId"/>.</summary>
    public string? CharacterName { get; private set; }

    /// <summary>See <see cref="AccountId"/>.</summary>
    public int? TotalLogins { get; private set; }

    /// <summary>
    /// Set for an allegiance event or a character-login-observed event (issue #39's self-heal fix);
    /// null for a character rename/deletion event.
    /// </summary>
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
