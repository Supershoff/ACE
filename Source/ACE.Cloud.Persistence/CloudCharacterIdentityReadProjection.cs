using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The read-only, versioned identity/allegiance cache CONTEXT.md permits: "A cache is permitted only
/// when it is versioned/refreshed from ACE and every sensitive action revalidates the current Acting
/// Character." Built exclusively by replaying <see cref="CloudIdentityOutboxEvent"/> rows (AUTH-003's
/// character rename/deletion, VAULT-001's allegiance swear/break/monarch-change, and issue #39's
/// self-heal character-login-observed snapshot), never treated as authority in its own right -- a
/// sensitive action still revalidates against ACE directly. Kept disposable and rebuildable from the
/// outbox alone, the same discipline <see cref="CloudInventoryReadProjection"/> applies to custody
/// state.
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
    /// uses. Issue #39's oath-first fix: every event type now carries the character's authoritative
    /// account/name/login snapshot (see <see cref="CloudIdentityOutboxEvent.AccountId"/>), so this
    /// always refreshes that snapshot regardless of event type -- an allegiance event can be the very
    /// first event this row ever sees and still leave the row account-associated. An allegiance event
    /// or a character-login-observed event (issue #39's self-heal fix, VAULT-001) additionally updates
    /// the monarch pointer to whatever it carries; a rename/deletion event leaves whatever monarch is
    /// already cached untouched. The self-heal fix is exactly this rule applied to a login: publishing
    /// a login-observed event with the character's live monarch lets a row a pre-oath-first-fix
    /// allegiance event left degraded (null account/name, or a stale monarch) get fully repaired the
    /// next time that character logs in, without their allegiance needing to change first.
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

        row.AccountId = evt.AccountId;
        row.CharacterName = evt.CharacterName;
        row.TotalLogins = evt.TotalLogins;

        if (evt.EventType is CloudIdentityEventType.AllegianceSworn or CloudIdentityEventType.AllegianceBroken
            or CloudIdentityEventType.AllegianceMonarchChanged or CloudIdentityEventType.CharacterLoginObserved)
        {
            row.MonarchId = evt.MonarchId;
        }

        row.LastAppliedSequenceNumber = evt.SequenceNumber;
        row.UpdatedAtUtc = DateTime.UtcNow;
        return (row, Applied: true);
    }
}
