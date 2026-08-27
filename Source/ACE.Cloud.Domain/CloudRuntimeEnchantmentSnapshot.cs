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

    public double RemainingDurationSeconds { get; init; }

    public CloudRuntimeEnchantmentSnapshot(int spellId, double remainingDurationSeconds)
    {
        if (remainingDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingDurationSeconds), "A Frozen Enchantment's preserved remaining duration cannot be negative.");
        }

        SpellId = spellId;
        RemainingDurationSeconds = remainingDurationSeconds;
    }
}
