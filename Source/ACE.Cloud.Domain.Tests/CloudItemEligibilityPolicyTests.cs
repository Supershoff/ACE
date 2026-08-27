namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The table-driven rejection corpus and success cases required by issue #8's Red section: every
/// DEP-004 exclusion category, DEP-003's equipped/ordinary-inventory rule, DEP-005's Frozen
/// Enchantment preservation, and the "no rule labels every merely non-Attuned item as safe"
/// acceptance criterion.
/// </summary>
[TestClass]
public sealed class CloudItemEligibilityPolicyTests
{
    private static readonly CloudItemId ItemId = new(12345);

    private static CloudItemEligibilitySnapshot ValidStaticItem() => new(
        ItemId,
        isLegalForPlayerToPlayerTrade: true,
        isEquipped: false,
        isContainer: false,
        isAttunedOrContainsAttuned: false,
        hasActivePetAttached: false,
        isCharacterBoundOrUnsafeStateful: false,
        hasFiniteLifespan: false,
        hasActiveCooldownOrAttachment: false,
        isCurrentlyTradedOrReserved: false);

    public static IEnumerable<object[]> RejectionCorpus()
    {
        yield return
        [
            ValidStaticItem() with { IsEquipped = true },
            CloudEligibilityRejectionCode.MustBeInOrdinaryInventory,
        ];
        yield return [ValidStaticItem() with { IsContainer = true }, CloudEligibilityRejectionCode.Container];
        yield return
        [
            // Containers and nesting: an empty container is rejected purely for being a container,
            // and a container that also contains an Attuned item is rejected the same way (CONTEXT.md:
            // "Containers are not Cloud-eligible Items in the first version, even when empty").
            ValidStaticItem() with { IsContainer = true, IsAttunedOrContainsAttuned = true },
            CloudEligibilityRejectionCode.Container,
        ];
        yield return [ValidStaticItem() with { IsAttunedOrContainsAttuned = true }, CloudEligibilityRejectionCode.AttunedOrSticky];
        yield return [ValidStaticItem() with { HasActivePetAttached = true }, CloudEligibilityRejectionCode.ActivePetAttached];
        yield return
        [
            ValidStaticItem() with { IsCharacterBoundOrUnsafeStateful = true },
            CloudEligibilityRejectionCode.CharacterBoundOrUnsafeStateful,
        ];
        yield return [ValidStaticItem() with { HasFiniteLifespan = true }, CloudEligibilityRejectionCode.FiniteLifespan];
        yield return
        [
            ValidStaticItem() with { HasActiveCooldownOrAttachment = true },
            CloudEligibilityRejectionCode.ActiveCooldownOrAttachment,
        ];
        yield return
        [
            ValidStaticItem() with { IsCurrentlyTradedOrReserved = true },
            CloudEligibilityRejectionCode.AlreadyTradedOrReserved,
        ];
        yield return
        [
            ValidStaticItem() with { IsLegalForPlayerToPlayerTrade = false },
            CloudEligibilityRejectionCode.NotLegalForPlayerTrade,
        ];
    }

    [TestMethod]
    [DynamicData(nameof(RejectionCorpus))]
    public void Evaluate_EachForbiddenCondition_IsRejectedWithAStableCodeAndActionableMessages(
        CloudItemEligibilitySnapshot snapshot, CloudEligibilityRejectionCode expectedCode)
    {
        var result = CloudItemEligibilityPolicy.Evaluate(snapshot);

        Assert.IsFalse(result.IsEligible);
        Assert.AreEqual(expectedCode, result.RejectionCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PlayerMessage), "A rejection requires an actionable in-game message.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.WebMessage), "A rejection requires a safe web message.");
        Assert.IsEmpty(result.PreservationRequirements);
    }

    [TestMethod]
    public void Evaluate_ANonAttunedItemThatIsAContainer_IsStillRejected()
    {
        // Guards against a rule that treats "not Attuned" as sufficient for safety
        // (CONTEXT.md: "'Any item that is not Attuned' was initially proposed ... superseded").
        var snapshot = ValidStaticItem() with { IsAttunedOrContainsAttuned = false, IsContainer = true };

        var result = CloudItemEligibilityPolicy.Evaluate(snapshot);

        Assert.IsFalse(result.IsEligible);
        Assert.AreEqual(CloudEligibilityRejectionCode.Container, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_AValidStaticItemWithNoRuntimeEnchantments_IsEligibleWithNoPreservationRequirements()
    {
        var result = CloudItemEligibilityPolicy.Evaluate(ValidStaticItem());

        Assert.IsTrue(result.IsEligible);
        Assert.IsNull(result.RejectionCode);
        Assert.IsNull(result.PlayerMessage);
        Assert.IsNull(result.WebMessage);
        Assert.IsEmpty(result.PreservationRequirements);
    }

    [TestMethod]
    public void Evaluate_AnItemWithAcceptedRuntimeEnchantments_IsEligibleAndCarriesFrozenEnchantmentPreservationRequirements()
    {
        // Runtime enchantments are accepted and frozen, not confused with forbidden active runtime
        // state such as cooldowns or finite lifespans (DEP-005).
        var enchantment = new CloudRuntimeEnchantmentSnapshot(spellId: 1337, remainingDurationSeconds: 42.5);
        var snapshot = ValidStaticItem() with { RuntimeEnchantments = [enchantment] };

        var result = CloudItemEligibilityPolicy.Evaluate(snapshot);

        Assert.IsTrue(result.IsEligible);
        CollectionAssert.AreEqual(new[] { enchantment }, result.PreservationRequirements.ToList());
    }

    [TestMethod]
    public void Evaluate_PermanentSpellsAreNotModeledAsRuntimeEnchantments_SoTheyNeverAffectEligibility()
    {
        // Permanent built-in item spells are ordinary static properties (DEP-005) and are therefore
        // simply absent from RuntimeEnchantments; an item with none is eligible with nothing to preserve.
        var snapshot = ValidStaticItem() with { RuntimeEnchantments = [] };

        var result = CloudItemEligibilityPolicy.Evaluate(snapshot);

        Assert.IsTrue(result.IsEligible);
        Assert.IsEmpty(result.PreservationRequirements);
    }

    [TestMethod]
    public void Evaluate_RejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudItemEligibilityPolicy.Evaluate(null!));
    }
}
