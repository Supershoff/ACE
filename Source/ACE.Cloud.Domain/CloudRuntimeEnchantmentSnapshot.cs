namespace ACE.Cloud.Domain;

/// <summary>
/// One accepted runtime (temporary) item enchantment that must survive as a Frozen Enchantment: its
/// persisted remaining duration is preserved without ticking while the item is in Cloud custody, and
/// ACE resumes heartbeat processing from the same remaining duration at withdrawal (DEP-005,
/// CONTEXT.md's "Frozen Enchantment"). Permanent built-in item spells are ordinary static properties
/// and are never represented here (DEP-005: "Permanent built-in spells remain ordinary static
/// properties").
/// </summary>
public sealed record CloudRuntimeEnchantmentSnapshot
{
    public int SpellId { get; init; }

    /// <summary>
    /// The registry row's real per-spell identity alongside <see cref="SpellId"/>
    /// (<c>biota_properties_enchantment_registry</c>'s composite key and its
    /// <c>wcid_enchantmentregistry_objectId_spellId_layerId_uidx</c> unique index both include it):
    /// multiple layers of the same spell on the same object are an explicit, supported
    /// <c>EnchantmentManager.Add</c> case (e.g. multiple casters' independent DoTs), so this must be
    /// preserved and threaded back through to resume the correct layer's remaining duration rather
    /// than every layer sharing that <see cref="SpellId"/>.
    /// </summary>
    public ushort LayerId { get; init; }

    public double RemainingDurationSeconds { get; init; }

    public CloudRuntimeEnchantmentSnapshot(int spellId, double remainingDurationSeconds, ushort layerId = 0)
    {
        if (remainingDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingDurationSeconds), "A Frozen Enchantment's preserved remaining duration cannot be negative.");
        }

        SpellId = spellId;
        LayerId = layerId;
        RemainingDurationSeconds = remainingDurationSeconds;
    }
}
