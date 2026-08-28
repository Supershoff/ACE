namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The table-driven partial-success corpus required by issue #13's Red section: duplicate profiles,
/// invalid/mismatched quantities, a stale sell window, delegation to the full DEP-004 eligibility
/// corpus, and both whole-item and stack deposit success shapes (DEP-002, ADR-0002).
/// </summary>
[TestClass]
public sealed class CloudCustodianDepositRowPolicyTests
{
    private static readonly CloudItemId ItemId = new(555_000);

    private static readonly CloudAggregateVersion V1 = CloudAggregateVersion.Initial;
    private static readonly CloudAggregateVersion V2 = CloudAggregateVersion.Initial.Next();

    private static CloudItemEligibilitySnapshot EligibleSnapshot() => new(
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

    private static CloudCustodianDepositRowRequest WholeItemRequest(
        CloudItemEligibilitySnapshot? snapshot = null, int submittedAmount = 1, bool duplicate = false) =>
        new(ItemId, submittedAmount, currentStackSize: 1, isStackable: false, duplicate, snapshot ?? EligibleSnapshot());

    private static readonly CloudCustodianSaleWindowValidation Current = CloudCustodianSaleWindowPolicy.Validate(
        isLocationCurrentlyEnabled: true, V1, V1);

    private static readonly CloudCustodianSaleWindowValidation Stale = CloudCustodianSaleWindowPolicy.Validate(
        isLocationCurrentlyEnabled: true, V1, V2);

    [TestMethod]
    public void Decide_AnEligibleWholeItem_DepositsTheWholeItem()
    {
        var decision = CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(), Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.DepositWhole, decision.Kind);
        Assert.AreEqual(ItemId, decision.ItemId);
        Assert.IsNull(decision.PlayerMessage);
        Assert.IsNull(decision.RejectionCode);
    }

    [TestMethod]
    public void Decide_AnEligibleStackWithAMatchingAmount_DepositsTheWholeCurrentStackSize()
    {
        var request = new CloudCustodianDepositRowRequest(
            ItemId, submittedAmount: 20, currentStackSize: 20, isStackable: true, isDuplicateInSubmission: false, EligibleSnapshot());

        var decision = CloudCustodianDepositRowPolicy.Decide(request, Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.DepositStack, decision.Kind);
        Assert.AreEqual(20, decision.Quantity);
    }

    [TestMethod]
    public void Decide_APartialStackAmount_IsRejectedRatherThanSilentlySplit()
    {
        // ADR-0002: CloudCustodyBoundary never partially deposits a fraction of a live biota's stack
        // size; the player must split first via ordinary ACE stack-split.
        var request = new CloudCustodianDepositRowRequest(
            ItemId, submittedAmount: 5, currentStackSize: 20, isStackable: true, isDuplicateInSubmission: false, EligibleSnapshot());

        var decision = CloudCustodianDepositRowPolicy.Decide(request, Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decision.Kind);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.PlayerMessage));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Decide_AnInvalidAmount_IsRejected(int submittedAmount)
    {
        var decision = CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(submittedAmount: submittedAmount), Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decision.Kind);
    }

    [TestMethod]
    public void Decide_ADuplicateRowInTheSameSubmission_IsRejected()
    {
        var decision = CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(duplicate: true), Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decision.Kind);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.PlayerMessage));
    }

    [TestMethod]
    public void Decide_AStaleSellWindow_RejectsEveryRowBeforeAnyOtherCheck()
    {
        // Transaction rule 10: a stale open Custodian window must never bypass current
        // configuration, even for an otherwise perfectly valid row.
        var decision = CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(), Stale);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decision.Kind);
        Assert.AreEqual(Stale.StaleReason, decision.PlayerMessage);
    }

    public static IEnumerable<object[]> EligibilityRejectionCorpus()
    {
        yield return [EligibleSnapshot() with { IsEquipped = true }, CloudEligibilityRejectionCode.MustBeInOrdinaryInventory];
        yield return [EligibleSnapshot() with { IsContainer = true }, CloudEligibilityRejectionCode.Container];
        yield return [EligibleSnapshot() with { IsAttunedOrContainsAttuned = true }, CloudEligibilityRejectionCode.AttunedOrSticky];
        yield return [EligibleSnapshot() with { HasActivePetAttached = true }, CloudEligibilityRejectionCode.ActivePetAttached];
        yield return
        [
            EligibleSnapshot() with { IsCharacterBoundOrUnsafeStateful = true },
            CloudEligibilityRejectionCode.CharacterBoundOrUnsafeStateful,
        ];
        yield return [EligibleSnapshot() with { HasFiniteLifespan = true }, CloudEligibilityRejectionCode.FiniteLifespan];
        yield return
        [
            EligibleSnapshot() with { HasActiveCooldownOrAttachment = true },
            CloudEligibilityRejectionCode.ActiveCooldownOrAttachment,
        ];
        yield return
        [
            EligibleSnapshot() with { IsCurrentlyTradedOrReserved = true },
            CloudEligibilityRejectionCode.AlreadyTradedOrReserved,
        ];
        yield return [EligibleSnapshot() with { IsLegalForPlayerToPlayerTrade = false }, CloudEligibilityRejectionCode.NotLegalForPlayerTrade];
    }

    [TestMethod]
    [DynamicData(nameof(EligibilityRejectionCorpus))]
    public void Decide_EveryDEP004Exclusion_IsRejectedWithItsStableCodeAndAnActionableMessage(
        CloudItemEligibilitySnapshot snapshot, CloudEligibilityRejectionCode expectedCode)
    {
        var decision = CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(snapshot), Current);

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decision.Kind);
        Assert.AreEqual(expectedCode, decision.RejectionCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.PlayerMessage));
    }

    [TestMethod]
    public void Decide_MixedValidAndInvalidRowsInOneBatch_EachRowSucceedsOrFailsIndependently()
    {
        // DEP-002: "Valid rows deposit even when other rows fail."
        var eligibleWhole = WholeItemRequest();
        var equipped = WholeItemRequest(EligibleSnapshot() with { IsEquipped = true });
        var eligibleStack = new CloudCustodianDepositRowRequest(
            new CloudItemId(555_001), submittedAmount: 3, currentStackSize: 3, isStackable: true, isDuplicateInSubmission: false, EligibleSnapshot());

        var decisions = new[]
        {
            CloudCustodianDepositRowPolicy.Decide(eligibleWhole, Current),
            CloudCustodianDepositRowPolicy.Decide(equipped, Current),
            CloudCustodianDepositRowPolicy.Decide(eligibleStack, Current),
        };

        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.DepositWhole, decisions[0].Kind);
        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.Reject, decisions[1].Kind);
        Assert.AreEqual(CloudEligibilityRejectionCode.MustBeInOrdinaryInventory, decisions[1].RejectionCode);
        Assert.AreEqual(CloudCustodianDepositRowDecisionKind.DepositStack, decisions[2].Kind);
    }

    [TestMethod]
    public void Decide_RejectsNullRequest()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudCustodianDepositRowPolicy.Decide(null!, Current));
    }

    [TestMethod]
    public void Decide_RejectsNullSaleWindow()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudCustodianDepositRowPolicy.Decide(WholeItemRequest(), null!));
    }
}
