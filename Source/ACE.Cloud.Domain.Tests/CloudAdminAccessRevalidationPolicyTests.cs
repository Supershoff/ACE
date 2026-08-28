namespace ACE.Cloud.Domain.Tests;

/// <summary>Red -> Green coverage for ADM-001: "Admin means ACE ace_auth.account.accessLevel == 5."</summary>
[TestClass]
public sealed class CloudAdminAccessRevalidationPolicyTests
{
    [TestMethod]
    public void Evaluate_AccessLevelFive_IsAuthorized()
    {
        Assert.IsTrue(CloudAdminAccessRevalidationPolicy.Evaluate(5).IsAuthorized);
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    [DataRow(4u)]
    [DataRow(6u)]
    public void Evaluate_AnyOtherAccessLevel_IsDenied(uint accessLevel)
    {
        Assert.IsFalse(CloudAdminAccessRevalidationPolicy.Evaluate(accessLevel).IsAuthorized);
    }
}
