namespace ACE.Cloud.TestKit;

/// <summary>
/// Reusable contract tests for idempotent commands (ARCH-006, transaction rules 4 and 8). Any
/// adapter proves it honors the idempotency invariant by implementing
/// <see cref="ICloudIdempotentCommandHarness{TEffect}"/> and inheriting this class -- MSTest
/// discovers the inherited <c>[TestMethod]</c>s automatically once the concrete subclass carries
/// <c>[TestClass]</c>, so no adapter needs to copy this suite's test logic (issue #10's acceptance
/// criterion).
/// </summary>
public abstract class CloudIdempotentCommandInvariantSuite<TEffect>
{
    protected abstract ICloudIdempotentCommandHarness<TEffect> CreateHarness();

    [TestMethod]
    public async Task RepeatedIdempotencyKey_ReplaysTheSameCommittedEffect_WithoutDuplicating()
    {
        var harness = CreateHarness();
        var idempotencyKey = Guid.NewGuid();

        var first = await harness.ExecuteAsync(idempotencyKey);
        var second = await harness.ExecuteAsync(idempotencyKey);

        Assert.AreEqual(
            harness.IdentityOf(first),
            harness.IdentityOf(second),
            "Repeating a command with the same idempotency key must replay the original committed effect, not reapply the mutation (ARCH-006, transaction rule 4).");
        Assert.AreEqual(
            1,
            await harness.CountCommittedEffectsAsync(),
            "A repeated idempotency key must never leave a second committed effect behind.");
    }

    [TestMethod]
    public async Task DifferentIdempotencyKeys_ProduceIndependentCommittedEffects()
    {
        var harness = CreateHarness();

        var a = await harness.ExecuteAsync(Guid.NewGuid());
        var b = await harness.ExecuteAsync(Guid.NewGuid());

        Assert.AreNotEqual(harness.IdentityOf(a), harness.IdentityOf(b));
        Assert.AreEqual(2, await harness.CountCommittedEffectsAsync());
    }

    [TestMethod]
    public async Task ConcurrentSameIdempotencyKey_BothCallersObserveTheSameCommittedEffect()
    {
        var harness = CreateHarness();
        var idempotencyKey = Guid.NewGuid();

        var firstAttempt = harness.ExecuteAsync(idempotencyKey);
        var secondAttempt = harness.ExecuteAsync(idempotencyKey);
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.AreEqual(
            harness.IdentityOf(results[0]),
            harness.IdentityOf(results[1]),
            "Two concurrent callers racing the same idempotency key must never observe two different committed effects (transaction rule 8: never infer failure and reapply).");
        Assert.AreEqual(1, await harness.CountCommittedEffectsAsync());
    }
}
