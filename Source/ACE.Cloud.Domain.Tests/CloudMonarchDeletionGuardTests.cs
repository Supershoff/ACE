namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #17, VAULT-005: "ACE blocks deletion of a monarch character while that
/// monarch's Allegiance Vault is nonempty" (CONTEXT.md line 406), and only in that exact
/// combination -- deleting a non-monarch, or a monarch whose vault has already been emptied by its
/// members, must always be allowed.
/// </summary>
[TestClass]
public sealed class CloudMonarchDeletionGuardTests
{
    [TestMethod]
    public void Evaluate_MonarchWithNonemptyVault_IsBlocked()
    {
        var decision = CloudMonarchDeletionGuard.Evaluate(isMonarch: true, vaultIsEmpty: false);

        Assert.IsFalse(decision.IsAllowed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [TestMethod]
    public void Evaluate_MonarchWithEmptyVault_IsAllowed()
    {
        var decision = CloudMonarchDeletionGuard.Evaluate(isMonarch: true, vaultIsEmpty: true);

        Assert.IsTrue(decision.IsAllowed);
        Assert.IsNull(decision.Reason);
    }

    [TestMethod]
    public void Evaluate_NonMonarchWithNonemptyVaultOwnerRow_IsStillAllowed()
    {
        // A non-monarch character can never own an Allegiance Vault identity in the first place
        // (CloudOwnerIdentity.ForAllegianceVault is keyed by monarch), but the guard itself must not
        // block ordinary character deletion just because some unrelated vault happens to be
        // nonempty -- it only ever cares about *this* character's own vault.
        var decision = CloudMonarchDeletionGuard.Evaluate(isMonarch: false, vaultIsEmpty: false);

        Assert.IsTrue(decision.IsAllowed);
    }

    [TestMethod]
    public void Evaluate_NonMonarchWithEmptyVault_IsAllowed()
    {
        var decision = CloudMonarchDeletionGuard.Evaluate(isMonarch: false, vaultIsEmpty: true);

        Assert.IsTrue(decision.IsAllowed);
    }
}
