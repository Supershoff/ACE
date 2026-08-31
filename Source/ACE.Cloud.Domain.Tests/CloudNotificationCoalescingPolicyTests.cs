namespace ACE.Cloud.Domain.Tests;

/// <summary>Issue #34's Red: "Test notification ... coalescing ... unread badge."</summary>
[TestClass]
public sealed class CloudNotificationCoalescingPolicyTests
{
    [TestMethod]
    public void AnUnreadNotificationOfTheSameKind_Coalesces()
    {
        Assert.IsTrue(CloudNotificationCoalescingPolicy.ShouldCoalesce(
            CloudNotificationKind.OwnershipReceived, existingIsRead: false, CloudNotificationKind.OwnershipReceived));
    }

    [TestMethod]
    public void AnAlreadyReadNotification_NeverCoalesces()
    {
        Assert.IsFalse(CloudNotificationCoalescingPolicy.ShouldCoalesce(
            CloudNotificationKind.OwnershipReceived, existingIsRead: true, CloudNotificationKind.OwnershipReceived));
    }
}
