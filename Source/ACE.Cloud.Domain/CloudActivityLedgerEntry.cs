namespace ACE.Cloud.Domain;

/// <summary>
/// One normalized, read-only Activity Ledger row (EVT-001, EVT-002): "events include immutable
/// actor/owner IDs, ... item/GUID/lot identity and relevant snapshot, timestamp, outcome, reason,
/// correlation/idempotency ID, and shard ID." A C# <c>record</c> with only init-only properties, so
/// nothing downstream of <c>ACE.Cloud.Persistence.CloudActivityLedgerQueryReader</c> can mutate a row
/// after it left the database -- the same "no update/delete path" guarantee the underlying
/// append-only ledger tables already provide at the storage layer (see
/// <see cref="ACE.Cloud.RepositoryPolicyTests"/>'s immutability surface tests). <see cref="OwnerId"/>
/// is null for the four admin-only categories (<see cref="CloudActivityLedgerCategory.AccountLink"/>
/// and <see cref="CloudActivityLedgerCategory.SharingGrant"/> use raw ACE account IDs/opaque owner
/// GUIDs on the underlying row but expose neither party through this single-<see cref="OwnerId"/>
/// shape -- see each category's own doc comment for why; <see cref="CloudActivityLedgerCategory.GlobalMaintenance"/>/
/// <see cref="CloudActivityLedgerCategory.AssetImport"/> have no per-account owner at all), matching
/// why those categories are never included in an Owner/Shared/Vault-scoped query
/// (<see cref="CloudActivityLedgerQueryEngine.Authorize"/>).
/// </summary>
public sealed record CloudActivityLedgerEntry(
    Guid Id,
    Guid CorrelationId,
    string ShardId,
    CloudActivityLedgerCategory Category,
    string EventType,
    Guid? OwnerId,
    uint? ItemBiotaId,
    string? Outcome,
    string? Reason,
    DateTime OccurredAtUtc);
