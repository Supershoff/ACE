using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #23's INV-004/INV-005/INV-006 section: "Test unlimited defaults,
/// personal/vault projected-lot counts, lowered limits, reduce-only actions, incoming obligations, and
/// binding settlement above a new quota."
/// </summary>
[TestClass]
public sealed class CloudStorageQuotaPolicyTests
{
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

    [TestMethod]
    public void Default_IsUnlimitedForBothScopes()
    {
        var limits = CloudStorageQuotaLimits.Default();

        Assert.IsNull(limits.PersonalLimit);
        Assert.IsNull(limits.VaultLimit);
    }

    [TestMethod]
    public void CheckNewObligation_WithNoLimit_AlwaysSucceeds_RegardlessOfCount()
    {
        var result = CloudStorageQuotaPolicy.CheckNewObligation(limit: null, currentProjectedCount: 1_000_000);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void CheckNewObligation_BelowLimit_Succeeds()
    {
        var result = CloudStorageQuotaPolicy.CheckNewObligation(limit: 10, currentProjectedCount: 9);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void CheckNewObligation_AtLimit_Refuses_NewDepositOrIncomingObligation()
    {
        var result = CloudStorageQuotaPolicy.CheckNewObligation(limit: 10, currentProjectedCount: 10);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void CheckNewObligation_AboveLimit_AfterALoweredQuota_StaysReduceOnly()
    {
        // INV-005: lowering a quota below an owner's current count never deletes/transfers anything,
        // but every further count-increasing action must be refused (reduce-only) until the owner
        // withdraws below the limit or the limit is raised.
        var result = CloudStorageQuotaPolicy.CheckNewObligation(limit: 5, currentProjectedCount: 12);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void CheckNewObligation_WithNegativeCount_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudStorageQuotaPolicy.CheckNewObligation(limit: 10, currentProjectedCount: -1));
    }

    [TestMethod]
    public void SetPersonalLimit_ByAdmin_ToAPositiveValue_Succeeds_AndBumpsVersion()
    {
        var current = CloudStorageQuotaLimits.Default();

        var result = CloudStorageQuotaPolicy.SetPersonalLimit(current, 250, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(250, result.Limits!.PersonalLimit);
        Assert.AreEqual(current.Version.Next(), result.Limits.Version);
    }

    [TestMethod]
    public void SetPersonalLimit_ToNull_RemovesTheLimit_AndSucceeds()
    {
        var current = CloudStorageQuotaLimits.Default() with { PersonalLimit = 250 };

        var result = CloudStorageQuotaPolicy.SetPersonalLimit(current, null, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Limits!.PersonalLimit);
    }

    [TestMethod]
    public void SetPersonalLimit_LoweringBelowAnExistingCount_StillSucceeds_NeverBlockedByCurrentOccupancy()
    {
        // INV-005: "Lowering a quota never deletes or forcibly transfers assets" -- the limit change
        // itself is never refused because someone is currently over it; only their future
        // count-increasing actions are (proven by CheckNewObligation_AboveLimit above).
        var current = CloudStorageQuotaLimits.Default() with { PersonalLimit = 100 };

        var result = CloudStorageQuotaPolicy.SetPersonalLimit(current, 5, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(5, result.Limits!.PersonalLimit);
    }

    [TestMethod]
    public void SetPersonalLimit_ToZeroOrNegative_IsRejected()
    {
        var current = CloudStorageQuotaLimits.Default();

        Assert.IsFalse(CloudStorageQuotaPolicy.SetPersonalLimit(current, 0, AdminAccessLevel).IsSuccess);
        Assert.IsFalse(CloudStorageQuotaPolicy.SetPersonalLimit(current, -1, AdminAccessLevel).IsSuccess);
    }

    [TestMethod]
    public void SetPersonalLimit_ByNonAdmin_IsRejected()
    {
        var current = CloudStorageQuotaLimits.Default();

        var result = CloudStorageQuotaPolicy.SetPersonalLimit(current, 100, NonAdminAccessLevel);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void SetVaultLimit_ByAdmin_ToAPositiveValue_Succeeds_IndependentlyOfPersonalLimit()
    {
        var current = CloudStorageQuotaLimits.Default() with { PersonalLimit = 100 };

        var result = CloudStorageQuotaPolicy.SetVaultLimit(current, 40, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(40, result.Limits!.VaultLimit);
        Assert.AreEqual(100, result.Limits.PersonalLimit);
    }

    [TestMethod]
    public void LimitFor_ReturnsTheScopedLimit()
    {
        var limits = new CloudStorageQuotaLimits(PersonalLimit: 250, VaultLimit: 40, CloudAggregateVersion.Initial);

        Assert.AreEqual(250, limits.LimitFor(CloudStorageQuotaScope.Personal));
        Assert.AreEqual(40, limits.LimitFor(CloudStorageQuotaScope.AllegianceVault));
    }
}
