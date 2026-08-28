namespace ACE.Cloud.Domain;

/// <summary>
/// The exact live facts <see cref="CloudWithdrawalLocationPolicy"/> needs to decide whether a
/// player's current landblock is a valid Withdrawal Token redemption location (WDR-006). ACE.Server
/// resolves each flag from the player's live position, this shard's Marketplace/housing content, and
/// the administrator-managed <see cref="CloudWithdrawalLocationConfiguration"/>.
/// </summary>
public sealed record CloudWithdrawalLocationSnapshot(
    bool IsMarketplace,
    bool IsHousingLandblock,
    bool IsNamedWithdrawalLandblock,
    bool WithdrawAnywhereEnabled);
