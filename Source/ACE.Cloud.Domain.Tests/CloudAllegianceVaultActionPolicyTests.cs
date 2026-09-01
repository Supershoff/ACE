namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red section coverage for <see cref="CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter"/>
/// (issue #37: VAULT-001, VAULT-002, VAULT-003, INV-004..006): unknown Acting Character, a character
/// currently in no allegiance, equal privileges for any live current member (no rank check), the
/// resolved vault always being the Acting Character's own live monarch, Storage Quota, and the
/// mutation gate.
/// </summary>
[TestClass]
public sealed class CloudAllegianceVaultActionPolicyTests
{
    private static CloudAllegianceVaultActionRequest ValidRequest(
        bool actingCharacterFound = true,
        uint? actingCharacterCurrentMonarchId = 42,
        int destinationCurrentProjectedCount = 0,
        int? destinationQuotaLimit = null,
        CloudMutationGateState gateState = CloudMutationGateState.Open) =>
        new(actingCharacterFound, actingCharacterCurrentMonarchId, destinationCurrentProjectedCount, destinationQuotaLimit, gateState);

    [TestMethod]
    public void AuthorizeActingCharacter_ACurrentMemberInAnAllegiance_Succeeds()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(ValidRequest(actingCharacterCurrentMonarchId: 42));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(42u, result.VaultMonarchId);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_AMonarchActingForTheirOwnVault_ResolvesToThemselves()
    {
        // A monarch's own live current monarch is themselves (VAULT-001's own vault owner derivation).
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(ValidRequest(actingCharacterCurrentMonarchId: 7));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(7u, result.VaultMonarchId);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_TwoDifferentAllianceMembers_ResolveEqualPrivileges()
    {
        // VAULT-002: "no rank ACLs or configurable tiers" -- any two current members of the same
        // allegiance resolve identically successful, independent of any notion of rank.
        var vassal = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(ValidRequest(actingCharacterCurrentMonarchId: 42));
        var anotherVassal = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(ValidRequest(actingCharacterCurrentMonarchId: 42));

        Assert.IsTrue(vassal.IsSuccess);
        Assert.IsTrue(anotherVassal.IsSuccess);
        Assert.AreEqual(vassal.VaultMonarchId, anotherVassal.VaultMonarchId);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_AnUnknownActingCharacter_IsRejected()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(actingCharacterFound: false, actingCharacterCurrentMonarchId: null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudAllegianceVaultActionRejectionCode.ActingCharacterNotFound, result.RejectionCode);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_ACharacterCurrentlyInNoAllegiance_IsRejected()
    {
        // VAULT-001: an alt with no current allegiance membership has no vault to act for, regardless
        // of what any other character on the same account belongs to.
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(actingCharacterCurrentMonarchId: null));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudAllegianceVaultActionRejectionCode.ActingCharacterNotInAllegiance, result.RejectionCode);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_WhenDestinationIsAtItsQuota_IsRejected()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(destinationCurrentProjectedCount: 5, destinationQuotaLimit: 5));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudAllegianceVaultActionRejectionCode.DestinationOverQuota, result.RejectionCode);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_WhenDestinationIsUnderItsQuota_Succeeds()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(destinationCurrentProjectedCount: 4, destinationQuotaLimit: 5));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_WithNoQuotaLimit_NeverRejectsForQuota()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(destinationCurrentProjectedCount: 1_000_000, destinationQuotaLimit: null));

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_WhileFrozen_IsRejectedBeforeAnyOtherCheck()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            ValidRequest(actingCharacterFound: false, gateState: CloudMutationGateState.Frozen));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudAllegianceVaultActionRejectionCode.MutationsFrozen, result.RejectionCode);
    }

    [TestMethod]
    public void AuthorizeActingCharacter_ANotFoundActingCharacter_NeverReportsAnAllegianceMonarchId()
    {
        var result = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(
            new CloudAllegianceVaultActionRequest(
                actingCharacterFound: false,
                actingCharacterCurrentMonarchId: 999, // a caller bug should never leak through as a resolved vault
                destinationCurrentProjectedCount: 0,
                destinationQuotaLimit: null,
                CloudMutationGateState.Open));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudAllegianceVaultActionRejectionCode.ActingCharacterNotFound, result.RejectionCode);
    }
}
