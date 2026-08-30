namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// AC Cloud Mule issue #22's Red requirement: "Test public/private authorization scope, revoked
/// access, cross-tab reconnection, missed-event replay, and stale optimistic updates" -- the
/// authorization-scope half of that list (EVT-007, MKT-201, and the security baseline's "Search
/// indexes and live streams must be scoped before data leaves the server").
/// </summary>
[TestClass]
public sealed class CloudLiveStreamAuthorizationPolicyTests
{
    [TestMethod]
    public void PublicEvent_IsVisibleToAnonymousViewer()
    {
        Assert.IsTrue(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(
            isPublic: true, scopeOwnerId: null, CloudLiveStreamViewer.Anonymous()));
    }

    [TestMethod]
    public void PublicEvent_IsVisibleEvenWhenAnOwnerScopeIsAlsoPresent()
    {
        var owner = Guid.NewGuid();
        Assert.IsTrue(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(
            isPublic: true, scopeOwnerId: owner, CloudLiveStreamViewer.Anonymous()));
    }

    [TestMethod]
    public void PrivateEvent_IsVisibleToItsOwner()
    {
        var owner = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([owner]);

        Assert.IsTrue(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, viewer));
    }

    [TestMethod]
    public void PrivateEvent_IsNotVisibleToAnUnrelatedOwner()
    {
        var owner = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]);

        Assert.IsFalse(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, viewer));
    }

    [TestMethod]
    public void PrivateEvent_IsNotVisibleToAnAnonymousViewer()
    {
        var owner = Guid.NewGuid();

        Assert.IsFalse(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, CloudLiveStreamViewer.Anonymous()));
    }

    [TestMethod]
    public void PrivateEvent_IsVisibleToARevalidatedAdministrator()
    {
        var owner = Guid.NewGuid();

        Assert.IsTrue(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, CloudLiveStreamViewer.ForAdmin()));
    }

    [TestMethod]
    public void PrivateEvent_RevokedAccess_NoLongerVisibleOnceTheAuthorizedOwnerSetNoLongerIncludesIt()
    {
        // Simulates a Sharing Grant revocation or an account unlink (SHARE-004, AUTH-005): the caller
        // rebuilds the authorized-owner set on every request, so a revoked grant simply stops
        // appearing in the set passed to the very next read.
        var owner = Guid.NewGuid();
        var beforeRevocation = CloudLiveStreamViewer.ForOwners([owner, Guid.NewGuid()]);
        var afterRevocation = CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]);

        Assert.IsTrue(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, beforeRevocation));
        Assert.IsFalse(CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, owner, afterRevocation));
    }

    [TestMethod]
    public void PrivateEvent_WithoutAScopeOwner_ThrowsRatherThanSilentlyDenyingOrAllowing()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudLiveStreamAuthorizationPolicy.IsVisibleTo(
            isPublic: false, scopeOwnerId: null, CloudLiveStreamViewer.ForAdmin()));
    }
}
