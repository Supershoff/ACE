namespace ACE.Cloud.Hosting;

/// <summary>
/// The aggregate operating mode a companion service derives from its <see cref="CloudStartupDiagnosticsReport"/>.
/// Distinguishes ARCH-009's "database unavailable makes the web read-only" from ARCH-008's "world
/// process offline makes only the world-boundary operations unavailable" -- the two failure modes
/// issue #18 requires the Green implementation to tell apart, rather than collapsing everything into
/// one generic "unhealthy" state.
/// </summary>
public enum CloudServiceAvailabilityMode
{
    /// <summary>Every checked component is healthy; ordinary mutations are permitted.</summary>
    Operational,

    /// <summary>ARCH-009: the database is unavailable. The service may serve safe cached reads but must queue no mutations.</summary>
    ReadOnly,

    /// <summary>OPS-002: shard identity, schema, or protocol versions are incompatible. Mutations must be refused until the mismatch is resolved.</summary>
    VersionIncompatible,

    /// <summary>ARCH-008: the database is healthy but the ACE world process is not reachable. Only withdrawal creation/redemption are unavailable; every other off-world operation continues.</summary>
    WorldBoundaryUnavailable,
}
