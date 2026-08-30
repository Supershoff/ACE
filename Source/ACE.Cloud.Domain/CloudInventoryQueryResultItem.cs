namespace ACE.Cloud.Domain;

/// <summary>
/// One authorized, categorized, paged Mule Page row (issue #30 Green: "Return stable item/custody/lot
/// identity, quantities, reservation state, appraisal/icon references, authoritative versions, and
/// permitted actions without leaking raw unauthorized data"). Deliberately narrower than
/// <see cref="CloudInventoryQueryCandidate"/>: it carries no owner ID, matching the security
/// baseline's "never leak an unauthorized private event" -- a viewer who is authorized to see this
/// row at all already knows whose inventory they are viewing from the request they made, so the
/// response itself need not repeat the opaque owner ID (still less any ACE account name).
/// </summary>
public sealed record CloudInventoryQueryResultItem(
    CloudItemId ItemId,
    CloudStackLotId? StackLotId,
    string Name,
    CloudInventoryCategory Category,
    int Quantity,
    int? Value,
    int? Burden,
    bool IsReserved,
    CloudAggregateVersion Version,
    CloudInventoryPermittedActions PermittedActions,
    string? IconCacheKeyHex);
