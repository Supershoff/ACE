namespace ACE.Cloud.Domain.Tests;

/// <summary>Issue #34's Red: "Test notification creation ... contextual destination."</summary>
[TestClass]
public sealed class CloudNotificationClassifierTests
{
    [TestMethod]
    public void OwnershipTransfer_IsNotificationWorthyWithAContextualDestination()
    {
        var classified = CloudNotificationClassifier.TryClassify("OwnershipTransfer", out var kind, out var destination);

        Assert.IsTrue(classified);
        Assert.AreEqual(CloudNotificationKind.OwnershipReceived, kind);
        Assert.IsFalse(string.IsNullOrWhiteSpace(destination));
    }

    [TestMethod]
    public void ASelfInitiatedDeposit_IsNotNotificationWorthy()
    {
        var classified = CloudNotificationClassifier.TryClassify("Deposit", out _, out _);

        Assert.IsFalse(classified);
    }

    [TestMethod]
    public void ASelfInitiatedWithdrawal_IsNotNotificationWorthy()
    {
        var classified = CloudNotificationClassifier.TryClassify("Withdrawal", out _, out _);

        Assert.IsFalse(classified);
    }

    [TestMethod]
    public void AnUnrecognizedEventType_IsNotNotificationWorthy()
    {
        var classified = CloudNotificationClassifier.TryClassify("SomethingFutureIssuesWillAdd", out _, out _);

        Assert.IsFalse(classified);
    }
}
