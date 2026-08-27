namespace ACE.Cloud.Persistence;

/// <summary>
/// The three possible outcomes of a Cloud world-boundary handoff attempt (issue #4's Green
/// section): a committed state change, an explicit domain conflict, or an explicit unavailable
/// result. Neither Conflict nor Unavailable ever queues a mutation for later replay (ARCH-009).
/// </summary>
public enum CloudBoundaryOutcomeKind
{
    Committed,
    Conflict,
    Unavailable,
}
