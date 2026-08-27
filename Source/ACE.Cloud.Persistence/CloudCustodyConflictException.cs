namespace ACE.Cloud.Persistence;

/// <summary>
/// An expected, caller-recoverable custody conflict (e.g. a biota that currently has world
/// possession, or a stale expected version) is reported through <see cref="CloudBoundaryOutcome{T}"/>,
/// not this exception (issue #4's Green section: "return explicit conflict and unavailable outcomes").
///
/// This exception is reserved for the narrower case where a boundary invariant itself appears
/// broken -- for example, a committed <see cref="CloudIdempotencyRecord"/> whose referenced
/// <see cref="CloudCustodyRecord"/> no longer exists, which the same transactional commit
/// (ARCH-006) should make impossible. That is not a normal domain conflict a caller should
/// silently branch on; it means the Cloud schema's own invariants were violated out of band.
/// </summary>
public sealed class CloudCustodyConflictException : Exception
{
    public CloudCustodyConflictException(string message)
        : base(message)
    {
    }
}
