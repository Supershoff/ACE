namespace ACE.Cloud.TestKit;

/// <summary>
/// The minimal surface an adapter exposes so
/// <see cref="CloudOptimisticConflictInvariantSuite{TState}"/> can prove ARCH-006 transaction rule
/// 3 (every mutable aggregate carries a version, and commands must present the expected version)
/// together with transaction rule 2 (deterministic row locking serializes concurrent mutations
/// instead of corrupting state).
/// </summary>
public interface ICloudOptimisticConflictHarness<TState>
{
    /// <summary>Creates one fresh mutable aggregate for a test to mutate.</summary>
    Task<TState> ArrangeAsync();

    /// <summary>The aggregate's current authoritative version.</summary>
    int VersionOf(TState state);

    /// <summary>
    /// Attempts a mutation against <paramref name="state"/> using <paramref name="expectedVersion"/>
    /// as the caller's optimistic precondition. Returns true if it committed, false if it was
    /// rejected as a version conflict.
    /// </summary>
    Task<bool> TryMutateAsync(TState state, int expectedVersion);
}
