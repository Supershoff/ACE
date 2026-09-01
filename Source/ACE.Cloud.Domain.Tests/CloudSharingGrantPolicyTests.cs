namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #36's Red requirements (SHARE-001..004, AUTH-008, WDR-002): character
/// resolution, View Only, View & Withdraw, explicit None, guild(allegiance)-derived access,
/// conflicting grants, and every forbidden capability (deposit, listing, bidding, settings, linking,
/// offers, permission management).
/// </summary>
[TestClass]
public sealed class CloudSharingGrantPolicyTests
{
    private static readonly CloudAccountId OwnerId = new(Guid.NewGuid());
    private static readonly CloudAccountId GranteeId = new(Guid.NewGuid());

    private static CloudSharingGrantSetRequest Request(
        bool granteeCharacterFound = true,
        CloudAccountId? granteeAccountId = null,
        bool granteeIsCrossShard = false,
        CloudSharingGrantLevel requestedLevel = CloudSharingGrantLevel.ViewOnly,
        CloudMutationGateState mutationGateState = CloudMutationGateState.Open) =>
        new(OwnerId, granteeCharacterFound, granteeCharacterFound ? granteeAccountId ?? GranteeId : null, granteeIsCrossShard, requestedLevel, mutationGateState);

    [TestMethod]
    public void EvaluateSet_MutationsFrozen_IsRejectedRegardlessOfOtherFacts()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(mutationGateState: CloudMutationGateState.Frozen));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantRejectionCode.MutationsFrozen, result.RejectionCode);
    }

    [TestMethod]
    public void EvaluateSet_UnknownGranteeCharacter_IsRejected()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(granteeCharacterFound: false));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantRejectionCode.UnknownGranteeCharacter, result.RejectionCode);
    }

    [TestMethod]
    public void EvaluateSet_CrossShardGrantee_IsRejected()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(granteeIsCrossShard: true));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantRejectionCode.CrossShardGrantee, result.RejectionCode);
    }

    [TestMethod]
    public void EvaluateSet_SelfGrantee_IsRejected()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(granteeAccountId: OwnerId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantRejectionCode.SelfGrantee, result.RejectionCode);
    }

    [TestMethod]
    public void EvaluateSet_AValidViewOnlyRequest_Succeeds()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(requestedLevel: CloudSharingGrantLevel.ViewOnly));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(GranteeId, result.GranteeAccountId);
        Assert.AreEqual(CloudSharingGrantLevel.ViewOnly, result.Level);
    }

    [TestMethod]
    public void EvaluateSet_AValidViewAndWithdrawRequest_Succeeds()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(requestedLevel: CloudSharingGrantLevel.ViewAndWithdraw));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantLevel.ViewAndWithdraw, result.Level);
    }

    [TestMethod]
    public void EvaluateSet_AnExplicitNoneRequest_SucceedsAsARealRevocation()
    {
        var result = CloudSharingGrantPolicy.EvaluateSet(Request(requestedLevel: CloudSharingGrantLevel.None));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudSharingGrantLevel.None, result.Level);
    }

    // --- ResolveEffectiveAccess: owner / explicit / derived precedence (SHARE-004) ---

    [TestMethod]
    public void ResolveEffectiveAccess_TheOwner_AlwaysGetsOwnerAccessRegardlessOfAnyGrant()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: true, explicitLevel: CloudSharingGrantLevel.None, hasQualifyingDerivedAccess: false);

        Assert.AreEqual(CloudSharingAccessLevel.Owner, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_ExplicitViewOnly_ResolvesToViewOnly()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: CloudSharingGrantLevel.ViewOnly, hasQualifyingDerivedAccess: false);

        Assert.AreEqual(CloudSharingAccessLevel.ViewOnly, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_ExplicitViewAndWithdraw_ResolvesToViewAndWithdraw()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: CloudSharingGrantLevel.ViewAndWithdraw, hasQualifyingDerivedAccess: false);

        Assert.AreEqual(CloudSharingAccessLevel.ViewAndWithdraw, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_NoExplicitGrantButQualifyingDerivedAccess_ResolvesToViewOnly()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: null, hasQualifyingDerivedAccess: true);

        Assert.AreEqual(CloudSharingAccessLevel.ViewOnly, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_NoExplicitGrantAndNoDerivedAccess_ResolvesToNone()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: null, hasQualifyingDerivedAccess: false);

        Assert.AreEqual(CloudSharingAccessLevel.None, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_ExplicitNoneOverridesQualifyingDerivedAccess_ConflictingGrant()
    {
        // SHARE-004: "An explicit individual Sharing Grant, including None, overrides guild-derived
        // personal-inventory access" -- the classic "conflicting grants" case Red asks for.
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: CloudSharingGrantLevel.None, hasQualifyingDerivedAccess: true);

        Assert.AreEqual(CloudSharingAccessLevel.None, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_ExplicitViewOnlyWithQualifyingDerivedAccess_StillResolvesToTheExplicitLevel()
    {
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: CloudSharingGrantLevel.ViewOnly, hasQualifyingDerivedAccess: true);

        Assert.AreEqual(CloudSharingAccessLevel.ViewOnly, access);
    }

    [TestMethod]
    public void ResolveEffectiveAccess_DerivedAccessNeverReachesViewAndWithdraw()
    {
        // Guild-derived access is capped at View Only; only an explicit grant can reach View & Withdraw.
        var access = CloudSharingGrantPolicy.ResolveEffectiveAccess(
            isOwner: false, explicitLevel: null, hasQualifyingDerivedAccess: true);

        Assert.AreNotEqual(CloudSharingAccessLevel.ViewAndWithdraw, access);
    }

    // --- CapabilitiesFor: every forbidden capability (deposit, listing, bidding, settings, linking, offers, permission management) ---

    [TestMethod]
    public void CapabilitiesFor_Owner_HasEveryCapability()
    {
        var capabilities = CloudSharingGrantPolicy.CapabilitiesFor(CloudSharingAccessLevel.Owner);

        Assert.IsTrue(capabilities.CanView);
        Assert.IsTrue(capabilities.CanCreateWithdrawalToken);
        Assert.IsTrue(capabilities.CanDeposit);
        Assert.IsTrue(capabilities.CanCreateListing);
        Assert.IsTrue(capabilities.CanBid);
        Assert.IsTrue(capabilities.CanChangeSettings);
        Assert.IsTrue(capabilities.CanLinkAccounts);
        Assert.IsTrue(capabilities.CanCreateTransferOffers);
        Assert.IsTrue(capabilities.CanManagePermissions);
    }

    [TestMethod]
    public void CapabilitiesFor_ViewAndWithdraw_GrantsOnlyViewAndTokenCreation()
    {
        var capabilities = CloudSharingGrantPolicy.CapabilitiesFor(CloudSharingAccessLevel.ViewAndWithdraw);

        Assert.IsTrue(capabilities.CanView);
        Assert.IsTrue(capabilities.CanCreateWithdrawalToken);

        AssertEveryForbiddenCapabilityIsDenied(capabilities);
    }

    [TestMethod]
    public void CapabilitiesFor_ViewOnly_GrantsOnlyViewAndNeverTokenCreation()
    {
        var capabilities = CloudSharingGrantPolicy.CapabilitiesFor(CloudSharingAccessLevel.ViewOnly);

        Assert.IsTrue(capabilities.CanView);
        Assert.IsFalse(capabilities.CanCreateWithdrawalToken);

        AssertEveryForbiddenCapabilityIsDenied(capabilities);
    }

    [TestMethod]
    public void CapabilitiesFor_None_GrantsNothingAtAll()
    {
        var capabilities = CloudSharingGrantPolicy.CapabilitiesFor(CloudSharingAccessLevel.None);

        Assert.IsFalse(capabilities.CanView);
        Assert.IsFalse(capabilities.CanCreateWithdrawalToken);

        AssertEveryForbiddenCapabilityIsDenied(capabilities);
    }

    private static void AssertEveryForbiddenCapabilityIsDenied(CloudSharingCapabilities capabilities)
    {
        Assert.IsFalse(capabilities.CanDeposit, "A Sharing Grant never permits deposit.");
        Assert.IsFalse(capabilities.CanCreateListing, "A Sharing Grant never permits marketplace listing.");
        Assert.IsFalse(capabilities.CanBid, "A Sharing Grant never permits bidding.");
        Assert.IsFalse(capabilities.CanChangeSettings, "A Sharing Grant never permits account/settings changes.");
        Assert.IsFalse(capabilities.CanLinkAccounts, "A Sharing Grant never permits account linking.");
        Assert.IsFalse(capabilities.CanCreateTransferOffers, "A Sharing Grant never permits creating Transfer Offers.");
        Assert.IsFalse(capabilities.CanManagePermissions, "A Sharing Grant never permits managing another Sharing Grant.");
    }
}
