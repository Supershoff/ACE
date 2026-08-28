namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #17, VAULT-004: Vault Absorption preconditions. The item-by-item transfer
/// itself is proved at the persistence layer (it needs real rows to enumerate); this covers only the
/// pure gate/identity preconditions every absorption attempt must satisfy first.
/// </summary>
[TestClass]
public sealed class CloudAllegianceVaultAbsorptionPolicyTests
{
    private static readonly CloudAccountId SourceVault = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly CloudAccountId DestinationVault = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [TestMethod]
    public void Absorb_DifferentVaults_OpenGate_Succeeds()
    {
        var result = CloudAllegianceVaultAbsorptionPolicy.Absorb(SourceVault, DestinationVault, CloudMutationGateState.Open);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Absorb_WhenFrozen_Fails()
    {
        var result = CloudAllegianceVaultAbsorptionPolicy.Absorb(SourceVault, DestinationVault, CloudMutationGateState.Frozen);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.MutationsFrozen, result.ErrorKind);
    }

    [TestMethod]
    public void Absorb_SameSourceAndDestination_Fails()
    {
        var result = CloudAllegianceVaultAbsorptionPolicy.Absorb(SourceVault, SourceVault, CloudMutationGateState.Open);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CloudCustodyTransitionErrorKind.InvalidRequest, result.ErrorKind);
    }
}
