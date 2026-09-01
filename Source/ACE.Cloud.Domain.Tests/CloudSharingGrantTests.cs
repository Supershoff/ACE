namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Aggregate-shape invariants for <see cref="CloudSharingGrant"/> (issue #36, SHARE-001..004).
/// </summary>
[TestClass]
public sealed class CloudSharingGrantTests
{
    private static readonly CloudSharingGrantId Id = new(Guid.NewGuid());
    private static readonly CloudAccountId OwnerId = new(Guid.NewGuid());
    private static readonly CloudAccountId GranteeId = new(Guid.NewGuid());
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [TestMethod]
    public void Constructor_ANewGrant_StartsAtInitialVersion()
    {
        var grant = new CloudSharingGrant(Id, OwnerId, GranteeId, CloudSharingGrantLevel.ViewOnly, CreatedAtUtc);

        Assert.AreEqual(CloudSharingGrantLevel.ViewOnly, grant.Level);
        Assert.AreEqual(CloudAggregateVersion.Initial, grant.Version);
        Assert.AreEqual(CreatedAtUtc, grant.UpdatedAtUtc);
    }

    [TestMethod]
    public void Constructor_WithTheSameOwnerAndGrantee_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CloudSharingGrant(Id, OwnerId, OwnerId, CloudSharingGrantLevel.ViewOnly, CreatedAtUtc));
    }

    [TestMethod]
    public void WithLevel_ADifferentLevel_BumpsVersionAndUpdatesTimestamp()
    {
        var grant = new CloudSharingGrant(Id, OwnerId, GranteeId, CloudSharingGrantLevel.ViewOnly, CreatedAtUtc);
        var updatedAtUtc = CreatedAtUtc.AddDays(1);

        var updated = grant.WithLevel(CloudSharingGrantLevel.ViewAndWithdraw, updatedAtUtc);

        Assert.AreEqual(CloudSharingGrantLevel.ViewAndWithdraw, updated.Level);
        Assert.AreEqual(grant.Version.Next(), updated.Version);
        Assert.AreEqual(updatedAtUtc, updated.UpdatedAtUtc);
        Assert.AreEqual(grant.CreatedAtUtc, updated.CreatedAtUtc);
    }

    [TestMethod]
    public void WithLevel_ExplicitNone_IsARealChangeThatBumpsVersion()
    {
        var grant = new CloudSharingGrant(Id, OwnerId, GranteeId, CloudSharingGrantLevel.ViewOnly, CreatedAtUtc);

        var revoked = grant.WithLevel(CloudSharingGrantLevel.None, CreatedAtUtc.AddDays(1));

        Assert.AreEqual(CloudSharingGrantLevel.None, revoked.Level);
        Assert.AreEqual(grant.Version.Next(), revoked.Version);
    }

    [TestMethod]
    public void WithLevel_TheSameLevelAgain_IsANoOpThatDoesNotBumpVersion()
    {
        var grant = new CloudSharingGrant(Id, OwnerId, GranteeId, CloudSharingGrantLevel.ViewOnly, CreatedAtUtc);

        var resent = grant.WithLevel(CloudSharingGrantLevel.ViewOnly, CreatedAtUtc.AddDays(1));

        Assert.AreEqual(grant.Version, resent.Version);
        Assert.AreEqual(grant.UpdatedAtUtc, resent.UpdatedAtUtc);
    }
}
