namespace ACE.Cloud.TestKit;

/// <summary>
/// The minimal surface an adapter (ACE, persistence, backend, or worker) exposes so
/// <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> can prove ARCH-006 and transaction
/// rule 4 -- repeating a request with the same idempotency key must replay the original committed
/// effect instead of reapplying the mutation -- without that adapter writing its own copy of the
/// suite's test logic.
/// </summary>
public interface ICloudIdempotentCommandHarness<TEffect>
{
    /// <summary>
    /// Executes the command under test with the given idempotency key. A second call with the same
    /// key must behave exactly as ARCH-006 requires: replay the original committed effect.
    /// </summary>
    Task<TEffect> ExecuteAsync(Guid idempotencyKey);

    /// <summary>
    /// The number of distinct committed effects the harness has produced so far, counted from the
    /// adapter's own authoritative storage rather than from anything this suite tracked itself.
    /// </summary>
    Task<int> CountCommittedEffectsAsync();

    /// <summary>
    /// A stable identity for a committed effect, used to prove two calls observed the very same
    /// effect rather than two effects that merely look alike.
    /// </summary>
    Guid IdentityOf(TEffect effect);
}
