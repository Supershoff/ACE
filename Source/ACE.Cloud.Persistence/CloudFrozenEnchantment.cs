namespace ACE.Cloud.Persistence;

/// <summary>
/// One Frozen Enchantment preserved for a native biota while it is out of world possession
/// (DEP-005, CONTEXT.md's "Frozen Enchantment"): the runtime (temporary) enchantment's remaining
/// duration at deposit time, tied to the <see cref="CloudCustodyRecord"/> that took the biota into
/// Cloud custody. ACE's own ace_shard registry rows never tick while a biota has no world
/// possession (nothing calls <c>EnchantmentManager.HeartBeat</c> on an object outside a container
/// or landblock), but that is an incidental consequence of ACE's tick scheduling, not a fact the
/// Cloud domain can rely on -- a future withdrawal (potentially onto a different shard, or after a
/// biota reload) must be able to resume heartbeat processing from the exact preserved remaining
/// duration without re-deriving it from ace_shard. This record only carries that preserved fact
/// forward; resuming heartbeat processing at withdrawal is a later issue's responsibility.
/// </summary>
public sealed class CloudFrozenEnchantment
{
    private CloudFrozenEnchantment()
    {
    }

    public CloudFrozenEnchantment(Guid custodyRecordId, string shardId, int spellId, double remainingDurationSeconds, ushort layerId = 0)
    {
        if (custodyRecordId == Guid.Empty)
        {
            throw new ArgumentException("A Frozen Enchantment requires the backing Cloud Custody Record's ID.", nameof(custodyRecordId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Frozen Enchantment requires a Cloud Shard ID.", nameof(shardId));
        }

        if (remainingDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingDurationSeconds), "A Frozen Enchantment's preserved remaining duration cannot be negative.");
        }

        Id = Guid.NewGuid();
        CustodyRecordId = custodyRecordId;
        ShardId = shardId;
        SpellId = spellId;
        LayerId = layerId;
        RemainingDurationSeconds = remainingDurationSeconds;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The backing <see cref="CloudCustodyRecord"/> this Frozen Enchantment was preserved for.
    /// </summary>
    public Guid CustodyRecordId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public int SpellId { get; private set; }

    /// <summary>
    /// The registry row's real per-spell identity alongside <see cref="SpellId"/> --
    /// <c>biota_properties_enchantment_registry</c>'s composite key and unique index both key on
    /// (object_Id, spell_Id, layer_Id), so two Frozen Enchantments can share a <see cref="SpellId"/>
    /// (multiple layers of the same spell, e.g. two casters' independent DoTs) and must resume
    /// independently by <see cref="LayerId"/> rather than colliding on <see cref="SpellId"/> alone.
    /// </summary>
    public ushort LayerId { get; private set; }

    public double RemainingDurationSeconds { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
