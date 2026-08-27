namespace ACE.Cloud.Domain;

/// <summary>
/// The single pure evaluator for whether an inventory item is a Cloud-eligible Item (DEP-003,
/// DEP-004, DEP-005, WDR-004, WDR-005): it reuses ACE's own player-to-player trade legality
/// (<see cref="CloudItemEligibilitySnapshot.IsLegalForPlayerToPlayerTrade"/>) and then applies every
/// Cloud-specific exclusion as one ordered rule table. Vendor (Cloud Custodian), withdrawal, and web
/// callers all call <see cref="Evaluate"/> and switch on the returned
/// <see cref="CloudEligibilityRejectionCode"/>; a new exclusion is added here once, as one more rule,
/// without touching any of those call sites (this issue's acceptance criterion: "New rules can be
/// added without branching vendor, withdrawal, and web logic independently").
/// </summary>
public static class CloudItemEligibilityPolicy
{
    private sealed record Rule(
        Func<CloudItemEligibilitySnapshot, bool> IsViolated,
        CloudEligibilityRejectionCode Code,
        string PlayerMessage,
        string WebMessage);

    /// <summary>
    /// Ordered so a container or an equipped item is reported for that reason first, even when it is
    /// also Attuned or otherwise disqualified for another reason: no single flag "labels every merely
    /// non-Attuned item as safe" (this issue's acceptance criterion), because every rule below is
    /// evaluated independently against the same snapshot.
    /// </summary>
    private static readonly IReadOnlyList<Rule> Rules =
    [
        new Rule(
            s => s.IsEquipped,
            CloudEligibilityRejectionCode.MustBeInOrdinaryInventory,
            "You must remove and carry that item before the Cloud Custodian can accept it.",
            "The item must be moved to ordinary inventory before it can be deposited."),
        new Rule(
            s => s.IsContainer,
            CloudEligibilityRejectionCode.Container,
            "The Cloud Custodian cannot accept containers, even empty ones.",
            "Containers cannot be deposited, even when empty."),
        new Rule(
            s => s.IsAttunedOrContainsAttuned,
            CloudEligibilityRejectionCode.AttunedOrSticky,
            "That item is Attuned or Sticky and cannot leave your possession.",
            "Attuned and Sticky items cannot be deposited."),
        new Rule(
            s => s.HasActivePetAttached,
            CloudEligibilityRejectionCode.ActivePetAttached,
            "You must unsummon your pet before the Cloud Custodian can accept that item.",
            "Devices with an actively summoned pet cannot be deposited."),
        new Rule(
            s => s.IsCharacterBoundOrUnsafeStateful,
            CloudEligibilityRejectionCode.CharacterBoundOrUnsafeStateful,
            "That item is bound to your character and cannot be deposited.",
            "Character-bound or otherwise unsafe stateful items cannot be deposited."),
        new Rule(
            s => s.HasFiniteLifespan,
            CloudEligibilityRejectionCode.FiniteLifespan,
            "That item's remaining lifespan cannot be preserved off-world.",
            "Items with a finite remaining lifespan cannot be deposited."),
        new Rule(
            s => s.HasActiveCooldownOrAttachment,
            CloudEligibilityRejectionCode.ActiveCooldownOrAttachment,
            "That item currently has an active cooldown or attachment and cannot be deposited yet.",
            "Items with active cooldown or attachment state cannot be deposited."),
        new Rule(
            s => s.IsCurrentlyTradedOrReserved,
            CloudEligibilityRejectionCode.AlreadyTradedOrReserved,
            "That item is already part of a pending trade or reservation.",
            "Items already reserved or in an active trade cannot be deposited."),
        new Rule(
            s => !s.IsLegalForPlayerToPlayerTrade,
            CloudEligibilityRejectionCode.NotLegalForPlayerTrade,
            "That item cannot be traded to another player and cannot be deposited.",
            "Items that are not legal under ordinary player-to-player trade rules cannot be deposited."),
    ];

    public static CloudEligibilityResult Evaluate(CloudItemEligibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var rule in Rules)
        {
            if (rule.IsViolated(snapshot))
            {
                return CloudEligibilityResult.Ineligible(rule.Code, rule.PlayerMessage, rule.WebMessage);
            }
        }

        return CloudEligibilityResult.Eligible(snapshot.RuntimeEnchantments);
    }
}
