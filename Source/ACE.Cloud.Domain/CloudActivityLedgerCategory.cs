namespace ACE.Cloud.Domain;

/// <summary>
/// Which physical ledger table (EVT-001) a <see cref="CloudActivityLedgerEntry"/> was normalized
/// from. A later Cloud Transaction Authority ledger unification (issue #21, referenced by
/// <c>ACE.Cloud.Persistence.CloudAccountLinkLedgerEvent</c>'s own doc comment) may fold these
/// physical tables into one; this enum is the seam a unified table's own discriminator column would
/// slot into without changing any query-engine/API/UI contract that already speaks in terms of it.
/// </summary>
public enum CloudActivityLedgerCategory
{
    /// <summary>A world-boundary custody handoff: deposit, withdrawal, ownership transfer, and similar (owner-scoped).</summary>
    CustodyBoundary,

    /// <summary>An account link/unlink attempt (admin-scoped only: linking has no single "owner" the way custody does).</summary>
    AccountLink,

    /// <summary>A Global Cloud Maintenance entry/exit (admin-scoped only: it is a shard-wide fact, not owned by any account).</summary>
    GlobalMaintenance,

    /// <summary>An Asset Import outcome (admin-scoped only: DAT import has no per-account owner).</summary>
    AssetImport,

    /// <summary>
    /// A personal Sharing Grant lifecycle event (admin-scoped only, mirroring <see cref="AccountLink"/>'s
    /// own rationale: a grant has two parties -- owner and grantee -- rather than one biota-scoped
    /// owner). See <c>ACE.Cloud.Persistence.CloudSharingGrantLedgerEvent</c>'s own doc comment.
    /// </summary>
    SharingGrant,
}
