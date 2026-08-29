namespace ACE.Cloud.Persistence;

/// <summary>
/// Informational, unlocked preview of one <see cref="CloudWithdrawalReservationTarget"/> (issue
/// #122), used solely by an ACE-side caller to decide, before ever calling
/// <see cref="CloudCustodyBoundary.RedeemWithdrawalReservationAsync(string, uint, System.Collections.Generic.IReadOnlyDictionary{Guid, uint}, Guid, System.Threading.CancellationToken)"/>,
/// which targets need a freshly ACE-allocated materialized child GUID (ARCH-010) and what the
/// prospective delivered item looks like for a combined native-receive capacity check (WDR-005).
/// Not itself a commit-time revalidation: redemption re-derives every one of these facts fresh under
/// its own row locks and refuses the request if a stale preview turns out wrong.
/// </summary>
public sealed record CloudWithdrawalReservationTargetPreview(
    Guid TargetId,
    CloudWithdrawalReservationTargetKind Kind,
    uint BackingBiotaId,
    int? Quantity,
    bool RequiresMaterialization);
