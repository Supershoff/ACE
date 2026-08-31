using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// Human-acceptance regression (issue #34): after a Cloud Custodian deposit submission -- success,
/// all-rows-rejected, or an unexpected exception -- the client's outstanding vendor-sell transaction
/// must always be completed by the same <c>ApproachVendor</c> call
/// <see cref="ACE.Server.WorldObjects.Vendor.ProcessItemsForPurchase"/> sends for an ordinary sale.
/// Without it, the client believes a sell is still pending and refuses to reopen the Custodian with
/// "You can only move or use one item at a time." Exercised directly against
/// <see cref="Player.RunWithGuaranteedVendorCompletion"/> (no live WorldObject/database needed).
/// </summary>
[TestClass]
public sealed class CloudCustodianVendorCompletionTests
{
    [TestMethod]
    public void RunWithGuaranteedVendorCompletion_ActionSucceeds_CompletesVendorTransactionExactlyOnce()
    {
        var completions = 0;
        var actionRan = false;

        Player.RunWithGuaranteedVendorCompletion(
            () => actionRan = true,
            () => completions++,
            _ => Assert.Fail("onException should not run when action succeeds."));

        Assert.IsTrue(actionRan);
        Assert.AreEqual(1, completions);
    }

    [TestMethod]
    public void RunWithGuaranteedVendorCompletion_ActionThrows_StillCompletesVendorTransactionExactlyOnce()
    {
        var completions = 0;
        Exception observed = null!;

        Player.RunWithGuaranteedVendorCompletion(
            () => throw new InvalidOperationException("row processing blew up"),
            () => completions++,
            ex => observed = ex);

        Assert.AreEqual(1, completions);
        Assert.IsInstanceOfType<InvalidOperationException>(observed);
    }

    [TestMethod]
    public void RunWithGuaranteedVendorCompletion_CompletesAfterTheActionRuns()
    {
        var order = new List<string>();

        Player.RunWithGuaranteedVendorCompletion(
            () => order.Add("action"),
            () => order.Add("complete"),
            _ => { });

        CollectionAssert.AreEqual(new List<string> { "action", "complete" }, order);
    }

    [TestMethod]
    public void RunWithGuaranteedVendorCompletion_CompletionItselfThrows_IsReportedInsteadOfCrashingTheCaller()
    {
        var observedExceptions = new List<Exception>();

        Player.RunWithGuaranteedVendorCompletion(
            () => { },
            () => throw new NotSupportedException("ApproachVendor should not normally throw"),
            observedExceptions.Add);

        Assert.HasCount(1, observedExceptions);
        Assert.IsInstanceOfType<NotSupportedException>(observedExceptions[0]);
    }
}
