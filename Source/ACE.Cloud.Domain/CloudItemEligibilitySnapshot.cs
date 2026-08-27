namespace ACE.Cloud.Domain;

/// <summary>
/// The immutable, ACE-derived facts about one inventory item that
/// <see cref="CloudItemEligibilityPolicy.Evaluate"/> needs to decide whether it is a Cloud-eligible
/// Item (CONTEXT.md: "An inventory item that ACE permits in player-to-player trade and that is
/// neither a container nor otherwise unsafe or stateful to transfer"). Producing this snapshot from a
/// live ACE <c>WorldObject</c> is the responsibility of ACE's own world-boundary code (ARCH-002); this
/// project stays pure and never loads ACE.Server world objects (ARCH-012).
/// </summary>
public sealed record CloudItemEligibilitySnapshot
{
    public CloudItemId ItemId { get; init; }

    /// <summary>
    /// Whether ACE's own player-to-player trade rules otherwise permit this item (DEP-003's "Reuse
    /// ACE player-to-player trade semantics explicitly"), independent of any Cloud-specific exclusion
    /// modeled by the other flags on this snapshot.
    /// </summary>
    public bool IsLegalForPlayerToPlayerTrade { get; init; }

    /// <summary>Equipped items must be moved to ordinary inventory before they can become Cloud Items (DEP-003).</summary>
    public bool IsEquipped { get; init; }

    /// <summary>Containers are never Cloud-eligible in the first release, even when empty (DEP-004).</summary>
    public bool IsContainer { get; init; }

    /// <summary>
    /// Reuses ACE's own <c>IsAttunedOrContainsAttuned</c> semantics: true when <c>PropertyInt.Attuned</c>
    /// is Attuned or Sticky (value 1 or higher), including recursively through any container ACE already
    /// inspected to produce this snapshot (DEP-004).
    /// </summary>
    public bool IsAttunedOrContainsAttuned { get; init; }

    /// <summary>The item is a pet device with an actively summoned pet (DEP-004).</summary>
    public bool HasActivePetAttached { get; init; }

    /// <summary>The item is character-bound or otherwise unsafe stateful (DEP-004).</summary>
    public bool IsCharacterBoundOrUnsafeStateful { get; init; }

    /// <summary>The item has a finite remaining lifespan (DEP-004).</summary>
    public bool HasFiniteLifespan { get; init; }

    /// <summary>The item has active cooldown or summoned-attachment runtime state (DEP-004).</summary>
    public bool HasActiveCooldownOrAttachment { get; init; }

    /// <summary>The item is already part of an in-progress trade or another exclusive reservation (DEP-004).</summary>
    public bool IsCurrentlyTradedOrReserved { get; init; }

    /// <summary>
    /// Accepted runtime (temporary) enchantments requiring Frozen Enchantment preservation on a
    /// successful deposit (DEP-005). Never includes permanent built-in spells.
    /// </summary>
    public IReadOnlyList<CloudRuntimeEnchantmentSnapshot> RuntimeEnchantments { get; init; }

    public CloudItemEligibilitySnapshot(
        CloudItemId itemId,
        bool isLegalForPlayerToPlayerTrade,
        bool isEquipped,
        bool isContainer,
        bool isAttunedOrContainsAttuned,
        bool hasActivePetAttached,
        bool isCharacterBoundOrUnsafeStateful,
        bool hasFiniteLifespan,
        bool hasActiveCooldownOrAttachment,
        bool isCurrentlyTradedOrReserved,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? runtimeEnchantments = null)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        ItemId = itemId;
        IsLegalForPlayerToPlayerTrade = isLegalForPlayerToPlayerTrade;
        IsEquipped = isEquipped;
        IsContainer = isContainer;
        IsAttunedOrContainsAttuned = isAttunedOrContainsAttuned;
        HasActivePetAttached = hasActivePetAttached;
        IsCharacterBoundOrUnsafeStateful = isCharacterBoundOrUnsafeStateful;
        HasFiniteLifespan = hasFiniteLifespan;
        HasActiveCooldownOrAttachment = hasActiveCooldownOrAttachment;
        IsCurrentlyTradedOrReserved = isCurrentlyTradedOrReserved;
        RuntimeEnchantments = runtimeEnchantments ?? [];
    }
}
