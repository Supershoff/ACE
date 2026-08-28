namespace ACE.Cloud.Hosting;

/// <summary>
/// The precise startup/health precondition a companion service checks before allowing mutations
/// (OPS-002: "Expose health/version diagnostics" and "identify the incompatible or unavailable
/// component precisely" -- issue #18's acceptance criterion). Each value corresponds to one of the
/// five scenarios issue #18's Red section requires a startup test for.
/// </summary>
public enum CloudStartupComponent
{
    /// <summary>The Cloud (or Auth) schema database did not answer a query (ARCH-009).</summary>
    Database,

    /// <summary>No <c>CloudShardBinding</c> row exists yet; Operator Bootstrap has not run (ARCH-001).</summary>
    ShardIdentity,

    /// <summary>The applied Cloud schema version does not match what this component expects (OPS-002).</summary>
    SchemaMigration,

    /// <summary>The negotiated ACE extension or contract protocol version is outside what this component accepts (OPS-002).</summary>
    ContractProtocol,

    /// <summary>The private ACE world-boundary endpoint did not answer (ARCH-008).</summary>
    WorldBoundary,
}
