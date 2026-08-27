namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive, stable set of reasons <see cref="CloudItemEligibilityPolicy.Evaluate"/> can reject
/// an item (this issue's outcome: "one explainable policy with stable rejection codes"). Vendor,
/// withdrawal, and web callers switch on this code rather than parsing free-text messages, so a
/// caller-visible reason never changes shape when its wording is edited.
/// </summary>
public enum CloudEligibilityRejectionCode
{
    /// <summary>The item is equipped; DEP-003 requires it to be moved to ordinary inventory first.</summary>
    MustBeInOrdinaryInventory,

    /// <summary>The item is a container. Containers are never Cloud-eligible, even when empty (DEP-004).</summary>
    Container,

    /// <summary>The item is Attuned or Sticky (<c>PropertyInt.Attuned</c> 114 at value 1 or higher, DEP-004).</summary>
    AttunedOrSticky,

    /// <summary>The item is a pet device with an actively summoned pet (DEP-004).</summary>
    ActivePetAttached,

    /// <summary>The item is character-bound or otherwise unsafe stateful (DEP-004).</summary>
    CharacterBoundOrUnsafeStateful,

    /// <summary>The item has a finite remaining lifespan (DEP-004).</summary>
    FiniteLifespan,

    /// <summary>The item has active cooldown or summoned-attachment runtime state (DEP-004).</summary>
    ActiveCooldownOrAttachment,

    /// <summary>The item is already part of an in-progress trade or another exclusive reservation (DEP-004).</summary>
    AlreadyTradedOrReserved,

    /// <summary>
    /// The item fails ACE's own player-to-player trade legality for a reason not independently
    /// modeled above (DEP-003: "legal under ACE player-to-player trade rules").
    /// </summary>
    NotLegalForPlayerTrade,
}
