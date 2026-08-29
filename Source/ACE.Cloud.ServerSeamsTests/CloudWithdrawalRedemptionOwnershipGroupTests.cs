using ACE.Cloud.Domain;
using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// AC Cloud Mule review of PR #120, finding [P1]: <c>Player_CloudWithdrawal.RedeemAsync</c> used to
/// compare a Withdrawal Reservation's owner identity directly against the redeeming account's own
/// identity, ignoring the Main/Linked ownership group this same PR introduced. Once two accounts are
/// linked, a reservation opened under one identity (Main or Linked) became permanently unredeemable
/// by a character logged into the other, contradicting CONTEXT.md's "redeemed by any character
/// currently belonging to the Main Account or one of its Linked Accounts." Exercised directly against
/// <see cref="Player.BelongsToRedeemersOwnershipGroup"/> (no live Session/Player/database needed) so
/// the ownership-group comparison itself is covered without requiring ACE's world/database bootstrap.
/// </summary>
[TestClass]
public class CloudWithdrawalRedemptionOwnershipGroupTests
{
    private const string ShardId = "us1";
    private const uint MainAccountId = 100;
    private const uint LinkedAccountId = 200;
    private const uint UnrelatedAccountId = 300;

    [TestMethod]
    public void BelongsToRedeemersOwnershipGroup_ReservationOwnedByTheRedeemersOwnAccount_ReturnsTrue()
    {
        var reservationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, MainAccountId);

        var belongs = Player.BelongsToRedeemersOwnershipGroup(ShardId, reservationOwnerId, new[] { MainAccountId });

        Assert.IsTrue(belongs);
    }

    [TestMethod]
    public void BelongsToRedeemersOwnershipGroup_LinkedAccountRedeemingAReservationOpenedUnderTheMainAccountsIdentity_ReturnsTrue()
    {
        // The reservation was opened after the two accounts linked, so its OwnerId is the Main
        // Account's identity; the redeeming character is on the Linked Account.
        var reservationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, MainAccountId);

        var belongs = Player.BelongsToRedeemersOwnershipGroup(ShardId, reservationOwnerId, new[] { MainAccountId, LinkedAccountId });

        Assert.IsTrue(belongs, "A Linked Account's character must be able to redeem a Withdrawal Token opened under the Main Account's identity.");
    }

    [TestMethod]
    public void BelongsToRedeemersOwnershipGroup_MainAccountRedeemingAReservationOpenedUnderASinceLinkedSourcesIdentity_ReturnsTrue()
    {
        // The reservation was opened before the source account linked, so its OwnerId is still the
        // source's own (pre-link) identity; the redeeming character is on the Main Account.
        var reservationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, LinkedAccountId);

        var belongs = Player.BelongsToRedeemersOwnershipGroup(ShardId, reservationOwnerId, new[] { MainAccountId, LinkedAccountId });

        Assert.IsTrue(belongs, "The Main Account must be able to redeem a Withdrawal Token opened under a since-linked source account's own identity.");
    }

    [TestMethod]
    public void BelongsToRedeemersOwnershipGroup_ReservationOwnedByAnUnrelatedAccount_ReturnsFalse()
    {
        var reservationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, UnrelatedAccountId);

        var belongs = Player.BelongsToRedeemersOwnershipGroup(ShardId, reservationOwnerId, new[] { MainAccountId, LinkedAccountId });

        Assert.IsFalse(belongs);
    }
}
