namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Table-driven coverage for WDR-004's "alive, fully loaded, non-combat player who is not trading,
/// portaling, recalling, logging out, or performing another inventory transfer."
/// </summary>
[TestClass]
public sealed class CloudWithdrawalSafeStatePolicyTests
{
    private static CloudWithdrawalSafeStateSnapshot Safe() => new(
        IsAlive: true,
        IsFullyLoaded: true,
        IsInCombatMode: false,
        IsTrading: false,
        IsTeleporting: false,
        IsLoggingOut: false,
        IsPerformingAnotherTransfer: false);

    [TestMethod]
    public void Evaluate_EverySafeFlag_IsSafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe());

        Assert.IsTrue(result.IsSafe);
        Assert.IsNull(result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_NotAlive_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsAlive = false });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.NotAlive, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_NotFullyLoaded_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsFullyLoaded = false });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.NotFullyLoaded, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_InCombatMode_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsInCombatMode = true });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.InCombatMode, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_Trading_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsTrading = true });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.Trading, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_Teleporting_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsTeleporting = true });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.Teleporting, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_LoggingOut_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsLoggingOut = true });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.LoggingOut, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_PerformingAnotherTransfer_IsUnsafe()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsPerformingAnotherTransfer = true });

        Assert.IsFalse(result.IsSafe);
        Assert.AreEqual(CloudWithdrawalSafeStateRejectionCode.PerformingAnotherTransfer, result.RejectionCode);
    }

    [TestMethod]
    public void Evaluate_EveryRejectionReturnsAnActionableReason()
    {
        var result = CloudWithdrawalSafeStatePolicy.Evaluate(Safe() with { IsAlive = false });

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reason));
    }
}
