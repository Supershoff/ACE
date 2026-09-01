namespace ACE.Cloud.Domain;

/// <summary>
/// The exact mutation capabilities one <see cref="CloudSharingAccessLevel"/> grants a non-owner
/// viewer over the owner's personal Cloud Inventory (SHARE-003: "View & Withdraw permits Withdrawal
/// Tokens for the grantee's own Main/Linked account group but does not permit marketplace, bidding,
/// account, settings, transfer-offer, or permission actions"). Every non-view field is always false
/// for a non-owner -- a Sharing Grant, at its most permissive, only ever adds
/// <see cref="CanCreateWithdrawalToken"/> to <see cref="CanView"/> -- named explicitly (rather than
/// left implicit) so every forbidden capability issue #36's Red tests enumerate (deposit, listing,
/// bidding, settings, linking, offers, permission management) has one exact assertion target.
/// </summary>
public sealed record CloudSharingCapabilities(
    bool CanView,
    bool CanCreateWithdrawalToken,
    bool CanDeposit,
    bool CanCreateListing,
    bool CanBid,
    bool CanChangeSettings,
    bool CanLinkAccounts,
    bool CanCreateTransferOffers,
    bool CanManagePermissions)
{
    /// <summary>The asset owner's own unmediated authority. Not itself granted by any Sharing Grant.</summary>
    public static CloudSharingCapabilities Owner { get; } =
        new(CanView: true, CanCreateWithdrawalToken: true, CanDeposit: true, CanCreateListing: true,
            CanBid: true, CanChangeSettings: true, CanLinkAccounts: true, CanCreateTransferOffers: true, CanManagePermissions: true);

    public static CloudSharingCapabilities ViewAndWithdraw { get; } =
        new(CanView: true, CanCreateWithdrawalToken: true, CanDeposit: false, CanCreateListing: false,
            CanBid: false, CanChangeSettings: false, CanLinkAccounts: false, CanCreateTransferOffers: false, CanManagePermissions: false);

    public static CloudSharingCapabilities ViewOnly { get; } =
        new(CanView: true, CanCreateWithdrawalToken: false, CanDeposit: false, CanCreateListing: false,
            CanBid: false, CanChangeSettings: false, CanLinkAccounts: false, CanCreateTransferOffers: false, CanManagePermissions: false);

    public static CloudSharingCapabilities None { get; } =
        new(CanView: false, CanCreateWithdrawalToken: false, CanDeposit: false, CanCreateListing: false,
            CanBid: false, CanChangeSettings: false, CanLinkAccounts: false, CanCreateTransferOffers: false, CanManagePermissions: false);
}
