namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #20's Red section: "Test standalone-source rules, trees/group merges,
/// pending obligations, self-dealing conflicts, source Sharing Grant revocation [tracked separately
/// -- see CloudAccountLinkGateway's doc comment], wrong password [web-layer, not this policy],
/// delayed confirmation [web-layer], concurrent link/unlink/deposit, and retry [gateway-level,
/// see ACE.Cloud.PersistenceIntegrationTests]" (AUTH-005, AUTH-006, AUTH-009).
/// </summary>
[TestClass]
public sealed class CloudAccountLinkPolicyTests
{
    private static CloudAccountLinkRequest ValidRequest(
        bool mainAccountIsLinkedElsewhere = false,
        bool sourceIsAlreadyLinked = false,
        bool sourceHasLinkedAccounts = false,
        bool sourceHasPendingObligations = false,
        bool wouldCreateActiveAuctionConflict = false,
        CloudMutationGateState mutationGateState = CloudMutationGateState.Open,
        uint mainAccountId = 1,
        uint sourceAccountId = 2) =>
        new(
            mainAccountId,
            sourceAccountId,
            mainAccountIsLinkedElsewhere,
            sourceIsAlreadyLinked,
            sourceHasLinkedAccounts,
            sourceHasPendingObligations,
            wouldCreateActiveAuctionConflict,
            mutationGateState);

    [TestMethod]
    public void EvaluateLink_EveryConditionClear_IsApproved()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest());

        Assert.IsTrue(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.None, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_SameAccountAsSourceAndMain_IsRejected()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(mainAccountId: 5, sourceAccountId: 5));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SameAccount, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_MainAccountIsItselfLinkedElsewhere_IsRejected()
    {
        // AUTH-006: "Linked-account trees and whole-group merges are prohibited" -- a Linked Account
        // may never become a Main Account for someone else.
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(mainAccountIsLinkedElsewhere: true));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MainAccountIsLinkedElsewhere, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_SourceAlreadyLinkedToAnotherMain_IsRejected()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(sourceIsAlreadyLinked: true));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceAlreadyLinked, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_SourceIsItselfAMainWithChildren_IsRejected()
    {
        // AUTH-006: only a standalone account -- no children of its own -- may become a Linked
        // Account (a whole-group merge is prohibited).
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(sourceHasLinkedAccounts: true));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasLinkedAccounts, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_SourceHasAPendingObligation_IsRejected()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(sourceHasPendingObligations: true));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceHasPendingObligations, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_WouldCreateAnActiveAuctionSelfDealingConflict_IsRejected()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(wouldCreateActiveAuctionConflict: true));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.WouldCreateAuctionConflict, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_MutationsFrozen_IsRejectedEvenWhenOtherwiseLegal()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(mutationGateState: CloudMutationGateState.Frozen));

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MutationsFrozen, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_MutationsFrozenTakesPrecedenceOverEveryOtherViolation()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(
            mutationGateState: CloudMutationGateState.Frozen,
            sourceIsAlreadyLinked: true,
            sourceHasPendingObligations: true));

        Assert.AreEqual(CloudAccountLinkRejectionCode.MutationsFrozen, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_StandaloneCheckTakesPrecedenceOverPendingObligations()
    {
        var decision = CloudAccountLinkPolicy.EvaluateLink(ValidRequest(
            sourceIsAlreadyLinked: true,
            sourceHasPendingObligations: true));

        Assert.AreEqual(CloudAccountLinkRejectionCode.SourceAlreadyLinked, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateLink_NullRequest_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudAccountLinkPolicy.EvaluateLink(null!));
    }

    [TestMethod]
    public void EvaluateUnlink_ActiveLinkAndOpenGate_IsApproved()
    {
        var decision = CloudAccountLinkPolicy.EvaluateUnlink(linkIsActive: true, CloudMutationGateState.Open);

        Assert.IsTrue(decision.IsApproved);
    }

    [TestMethod]
    public void EvaluateUnlink_LinkNotActive_IsRejected()
    {
        var decision = CloudAccountLinkPolicy.EvaluateUnlink(linkIsActive: false, CloudMutationGateState.Open);

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.LinkNotActive, decision.RejectionCode);
    }

    [TestMethod]
    public void EvaluateUnlink_MutationsFrozen_IsRejectedEvenForAnActiveLink()
    {
        var decision = CloudAccountLinkPolicy.EvaluateUnlink(linkIsActive: true, CloudMutationGateState.Frozen);

        Assert.IsFalse(decision.IsApproved);
        Assert.AreEqual(CloudAccountLinkRejectionCode.MutationsFrozen, decision.RejectionCode);
    }

    [TestMethod]
    public void Decision_RejectedWithNoneCode_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.None));
    }

    [TestMethod]
    public void Request_ZeroMainAccountId_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ValidRequest(mainAccountId: 0));
    }

    [TestMethod]
    public void Request_ZeroSourceAccountId_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ValidRequest(sourceAccountId: 0));
    }
}
