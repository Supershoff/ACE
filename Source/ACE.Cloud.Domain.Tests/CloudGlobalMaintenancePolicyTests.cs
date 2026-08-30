using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #23's ADM-004 section: "Test Global Cloud Maintenance entry/exit,
/// reason/confirmation, every mutation gate, nested/repeated commands, exact deadline shifting, and
/// commit-time revalidation."
/// </summary>
[TestClass]
public sealed class CloudGlobalMaintenancePolicyTests
{
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;
    private const uint AdminAccountId = 42;

    private static readonly DateTime NowUtc = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Enter_FromOpen_WithReasonAndConfirmation_Succeeds()
    {
        var current = CloudGlobalMaintenanceState.Default();

        var result = CloudGlobalMaintenancePolicy.Enter(current, "scheduled downtime", confirmed: true, AdminAccessLevel, AdminAccountId, NowUtc);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.State!.IsFrozen);
        Assert.AreEqual("scheduled downtime", result.State.Reason);
        Assert.AreEqual(NowUtc, result.State.EnteredAtUtc);
        Assert.AreEqual(AdminAccountId, result.State.EnteredByAccountId);
        Assert.AreEqual(current.Version.Next(), result.State.Version);
    }

    [TestMethod]
    public void Enter_WithoutReason_IsRejected()
    {
        var current = CloudGlobalMaintenanceState.Default();

        var result = CloudGlobalMaintenancePolicy.Enter(current, reason: "", confirmed: true, AdminAccessLevel, AdminAccountId, NowUtc);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(current.IsFrozen, "A rejected entry must not have mutated the caller's original state.");
    }

    [TestMethod]
    public void Enter_WithoutConfirmation_IsRejected()
    {
        var current = CloudGlobalMaintenanceState.Default();

        var result = CloudGlobalMaintenancePolicy.Enter(current, "scheduled downtime", confirmed: false, AdminAccessLevel, AdminAccountId, NowUtc);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Enter_ByNonAdmin_IsRejected()
    {
        var current = CloudGlobalMaintenanceState.Default();

        var result = CloudGlobalMaintenancePolicy.Enter(current, "scheduled downtime", confirmed: true, NonAdminAccessLevel, AdminAccountId, NowUtc);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Enter_WhenAlreadyFrozen_IsRejected_NestedEntryIsRefused()
    {
        var alreadyFrozen = CloudGlobalMaintenanceState.Default() with
        {
            IsFrozen = true,
            Reason = "first freeze",
            EnteredAtUtc = NowUtc,
            Version = CloudAggregateVersion.Initial,
        };

        var result = CloudGlobalMaintenancePolicy.Enter(alreadyFrozen, "second freeze", confirmed: true, AdminAccessLevel, AdminAccountId, NowUtc);

        Assert.IsFalse(result.IsSuccess, "A repeated/nested Enter while already frozen must be refused, not silently accepted or extended.");
    }

    [TestMethod]
    public void Exit_FromFrozen_WithConfirmation_Succeeds_AndComputesExactFrozenDuration()
    {
        var enteredAt = NowUtc;
        var frozen = CloudGlobalMaintenanceState.Default() with
        {
            IsFrozen = true,
            Reason = "scheduled downtime",
            EnteredAtUtc = enteredAt,
            EnteredByAccountId = AdminAccountId,
            Version = CloudAggregateVersion.Initial,
        };
        var exitAt = enteredAt.AddMinutes(37).AddSeconds(13);

        var result = CloudGlobalMaintenancePolicy.Exit(frozen, confirmed: true, AdminAccessLevel, exitAt);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.State!.IsFrozen);
        Assert.IsNull(result.State.EnteredAtUtc);
        Assert.IsNull(result.State.Reason);
        Assert.AreEqual(TimeSpan.FromMinutes(37) + TimeSpan.FromSeconds(13), result.FrozenDuration);
    }

    [TestMethod]
    public void Exit_WhenNotFrozen_IsRejected_RepeatedExitIsRefused()
    {
        var open = CloudGlobalMaintenanceState.Default();

        var result = CloudGlobalMaintenancePolicy.Exit(open, confirmed: true, AdminAccessLevel, NowUtc);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Exit_WithoutConfirmation_IsRejected()
    {
        var frozen = CloudGlobalMaintenanceState.Default() with { IsFrozen = true, Reason = "x", EnteredAtUtc = NowUtc };

        var result = CloudGlobalMaintenancePolicy.Exit(frozen, confirmed: false, AdminAccessLevel, NowUtc);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Exit_ByNonAdmin_IsRejected()
    {
        var frozen = CloudGlobalMaintenanceState.Default() with { IsFrozen = true, Reason = "x", EnteredAtUtc = NowUtc };

        var result = CloudGlobalMaintenancePolicy.Exit(frozen, confirmed: true, NonAdminAccessLevel, NowUtc);

        Assert.IsFalse(result.IsSuccess);
    }
}
