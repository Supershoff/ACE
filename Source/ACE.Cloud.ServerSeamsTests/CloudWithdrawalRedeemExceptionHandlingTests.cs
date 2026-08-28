using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// AC Cloud Mule review of issue #16 (PR #111), finding [P1]: an exception thrown by any of the
/// pre-Cloud-round-trip checks in <c>Player.HandleCloudWithdrawalRedeem</c> (safe-state, location,
/// token hashing, owner identity) used to propagate past that method's try/catch -- which only
/// wrapped the <c>RedeemAsync</c> call -- straight into <c>GameActionTalk.Handle</c>'s generic
/// command-exception handler, which logs the raw command text, including the plaintext Withdrawal
/// Token secret, to the server's Error log. Exercised directly against
/// <see cref="Player.TryRunCloudWithdrawalRedeem"/> (no live Session/Player/database needed) so the
/// exception-to-failure mapping is covered without requiring ACE's world/database bootstrap.
/// </summary>
[TestClass]
public class CloudWithdrawalRedeemExceptionHandlingTests
{
    [TestMethod]
    public void TryRunCloudWithdrawalRedeem_RedeemThrows_ReportsTheExceptionInsteadOfPropagating()
    {
        Exception observed = null;

        Player.TryRunCloudWithdrawalRedeem(
            () => throw new InvalidOperationException("simulated safe-state/location/hash/owner-identity failure"),
            ex => observed = ex);

        Assert.IsInstanceOfType(observed, typeof(InvalidOperationException));
    }

    [TestMethod]
    public void TryRunCloudWithdrawalRedeem_RedeemSucceeds_ReportsNoException()
    {
        var observedException = false;

        Player.TryRunCloudWithdrawalRedeem(() => { }, ex => observedException = true);

        Assert.IsFalse(observedException);
    }
}
