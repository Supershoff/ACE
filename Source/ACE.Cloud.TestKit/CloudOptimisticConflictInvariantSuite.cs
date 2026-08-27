namespace ACE.Cloud.TestKit;

/// <summary>
/// Reusable contract tests for optimistic-version conflicts and deterministic locking (ARCH-006,
/// transaction rules 2 and 3). See <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> for
/// how an adapter adopts a suite like this without copying its test logic.
/// </summary>
public abstract class CloudOptimisticConflictInvariantSuite<TState>
{
    protected abstract ICloudOptimisticConflictHarness<TState> CreateHarness();

    [TestMethod]
    public async Task StaleExpectedVersion_IsRejectedAsAConflict()
    {
        var harness = CreateHarness();
        var state = await harness.ArrangeAsync();
        var staleVersion = harness.VersionOf(state) + 1;

        var committed = await harness.TryMutateAsync(state, staleVersion);

        Assert.IsFalse(
            committed,
            "A command whose expected version does not match the aggregate's current authoritative version must be rejected, not silently applied (ARCH-006, transaction rule 3).");
    }

    [TestMethod]
    public async Task MatchingExpectedVersion_Commits()
    {
        var harness = CreateHarness();
        var state = await harness.ArrangeAsync();

        var committed = await harness.TryMutateAsync(state, harness.VersionOf(state));

        Assert.IsTrue(committed, "A command presenting the aggregate's true current version must be allowed to commit.");
    }

    [TestMethod]
    public async Task ConcurrentMutationsAgainstTheSameExpectedVersion_ExactlyOneCommits()
    {
        var harness = CreateHarness();
        var state = await harness.ArrangeAsync();
        var version = harness.VersionOf(state);

        var first = harness.TryMutateAsync(state, version);
        var second = harness.TryMutateAsync(state, version);
        var results = await Task.WhenAll(first, second);

        Assert.AreEqual(
            1,
            results.Count(committed => committed),
            "Deterministic row locking must serialize two concurrent mutations racing the same expected version so exactly one commits and the loser sees a conflict, never both or neither (transaction rule 2).");
    }
}
