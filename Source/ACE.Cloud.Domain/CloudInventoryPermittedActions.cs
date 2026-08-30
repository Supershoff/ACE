namespace ACE.Cloud.Domain;

/// <summary>
/// The mutation actions one Mule Page row currently permits its viewer (issue #30 Green: "permitted
/// actions"). Derived purely from state already known to the query -- never a second, independently
/// maintained authority -- so a permission never drifts from the reservation/ownership facts the row
/// itself reports.
/// </summary>
public sealed record CloudInventoryPermittedActions(bool CanWithdraw, bool CanList, bool CanTransfer, bool CanShare)
{
    /// <summary>
    /// A currently reserved item permits no new exclusive action (the custody state model: "one
    /// quantity may have at most one exclusive reservation at a time"). A viewer with only View Only
    /// Sharing Grant access (<paramref name="canMutate"/> false) may look but never start one either
    /// (SHARE-002: personal View Only grants no mutation authority) -- View & Withdraw callers pass
    /// true only for <see cref="CanWithdraw"/> via the two-argument overload below when that
    /// distinction matters; the owner themselves always passes true for every capability.
    /// </summary>
    public static CloudInventoryPermittedActions For(bool isReserved, bool canMutate) =>
        new(
            CanWithdraw: canMutate && !isReserved,
            CanList: canMutate && !isReserved,
            CanTransfer: canMutate && !isReserved,
            CanShare: canMutate);

    public static CloudInventoryPermittedActions None { get; } = new(false, false, false, false);
}
