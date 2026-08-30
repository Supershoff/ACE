using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The read-only, versioned identity/allegiance cache CONTEXT.md permits: "A cache is permitted only
/// when it is versioned/refreshed from ACE and every sensitive action revalidates the current Acting
/// Character." Built exclusively by replaying <see cref="CloudIdentityOutboxEvent"/> rows (AUTH-003's
/// character rename/deletion, VAULT-001's allegiance swear/break/monarch-change), never treated as
/// authority in its own right -- a sensitive action still revalidates against ACE directly. Kept
/// disposable and rebuildable from the outbox alone, the same discipline
/// <see cref="CloudInventoryReadProjection"/> applies to custody state.
/// </summary>
public sealed class CloudCharacterIdentityReadProjection
{
    private CloudCharacterIdentityReadProjection()
    {
    }

    private CloudCharacterIdentityReadProjection(uint characterId, string shardId)
    {
        CharacterId = characterId;
        ShardId = shardId;
    }

    public uint CharacterId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint? AccountId { get; private set; }

    public string? CharacterName { get; private set; }

    public int? TotalLogins { get; private set; }

    public uint? MonarchId { get; private set; }

    /// <summary>The outbox <see cref="CloudIdentityOutboxEvent.SequenceNumber"/> this row last applied.</summary>
    public long LastAppliedSequenceNumber { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Applies one identity/allegiance outbox event, following the same
    /// <see cref="CloudProjectionSequenceGuard"/> rule <see cref="CloudInventoryReadProjection.TryApply"/>
    /// uses. A character event updates the name/login snapshot; an allegiance event updates only the
    /// monarch pointer, leaving whatever name/login snapshot is already cached untouched.
    /// </summary>
    public static (CloudCharacterIdentityReadProjection Row, bool Applied) TryApply(
        CloudCharacterIdentityReadProjection? current,
        CloudIdentityOutboxEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var row = current ?? new CloudCharacterIdentityReadProjection(evt.CharacterId, evt.ShardId);

        if (!CloudProjectionSequenceGuard.ShouldApply(current?.LastAppliedSequenceNumber, evt.SequenceNumber))
        {
            return (row, Applied: false);
        }

        if (evt.EventType is CloudIdentityEventType.CharacterRenamed or CloudIdentityEventType.CharacterDeleted)
        {
            row.AccountId = evt.AccountId;
            row.CharacterName = evt.CharacterName;
            row.TotalLogins = evt.TotalLogins;
        }
        else
        {
            row.MonarchId = evt.MonarchId;
        }

        row.LastAppliedSequenceNumber = evt.SequenceNumber;
        return (row, Applied: true);
    }
}
