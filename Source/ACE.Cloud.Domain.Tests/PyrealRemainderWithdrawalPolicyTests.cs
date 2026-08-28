namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The corpus required by issue #14's Red section for raw Pyreal Remainder withdrawal: capacity
/// failure (insufficient remainder), retry (a refused request leaves the remainder untouched and
/// retryable), and maintenance/gate behavior (ADM-004).
/// </summary>
[TestClass]
public sealed class PyrealRemainderWithdrawalPolicyTests
{
    [TestMethod]
    public void Decide_RequestingExactlyTheAvailableRemainder_IsApprovedAndLeavesNoRemainder()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 12_345, requestedAmount: 12_345, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.Approved, decision.Kind);
        Assert.AreEqual(0, decision.NewRemainder);
    }

    [TestMethod]
    public void Decide_RequestingPartOfTheAvailableRemainder_IsApprovedAndLeavesTheExactRest()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 12_345, requestedAmount: 1_000, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.Approved, decision.Kind);
        Assert.AreEqual(11_345, decision.NewRemainder);
    }

    [TestMethod]
    public void Decide_RequestingMoreThanTheAvailableRemainder_IsInsufficientAndLeavesTheRemainderUnchanged()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 500, requestedAmount: 501, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, decision.Kind);
        Assert.AreEqual(500, decision.AvailableRemainder);
    }

    [TestMethod]
    public void Decide_RequestingFromAnEmptyRemainder_IsInsufficient()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 0, requestedAmount: 1, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, decision.Kind);
        Assert.AreEqual(0, decision.AvailableRemainder);
    }

    [TestMethod]
    public void Decide_RequestingZero_IsInsufficientRatherThanASilentNoOpSuccess()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 1_000, requestedAmount: 0, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, decision.Kind);
    }

    [TestMethod]
    public void Decide_RequestingANegativeAmount_IsInsufficient()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 1_000, requestedAmount: -1, CloudMutationGateState.Open);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, decision.Kind);
    }

    [TestMethod]
    public void Decide_WhileFrozen_RefusesEvenAnOtherwisePayableRequest()
    {
        var decision = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 12_345, requestedAmount: 1, CloudMutationGateState.Frozen);

        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.Frozen, decision.Kind);
    }

    [TestMethod]
    public void Decide_ANegativeCurrentRemainder_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: -1, requestedAmount: 1, CloudMutationGateState.Open));
    }

    [TestMethod]
    public void Decide_TheSameInsufficientRequestRetriedAfterTheRemainderGrows_BecomesApproved()
    {
        // Models "retry": the exact same requested amount that was refused becomes approved once a
        // later deposit grows the remainder, without ever having mutated the refused attempt's state.
        var firstAttempt = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 100, requestedAmount: 200, CloudMutationGateState.Open);
        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, firstAttempt.Kind);

        var afterGrowth = PyrealRemainderWithdrawalPolicy.Decide(currentRemainder: 300, requestedAmount: 200, CloudMutationGateState.Open);
        Assert.AreEqual(PyrealRemainderWithdrawalDecisionKind.Approved, afterGrowth.Kind);
        Assert.AreEqual(100, afterGrowth.NewRemainder);
    }
}
