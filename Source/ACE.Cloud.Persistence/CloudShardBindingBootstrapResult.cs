namespace ACE.Cloud.Persistence;

/// <summary>
/// The outcome of <see cref="CloudShardBindingBootstrapper.BootstrapAsync"/>: whether this call
/// inserted the singleton row for the first time, or found an existing row that already matched
/// exactly (both are success -- the whole point is idempotency across repeat acceptance-launcher
/// runs). A genuine mismatch is never represented here; it throws <see cref="CloudShardBindingMismatchException"/>
/// instead, since silently returning a "did not match" result is too easy for a caller to ignore.
/// </summary>
public sealed record CloudShardBindingBootstrapResult
{
    private CloudShardBindingBootstrapResult(bool wasCreated)
    {
        WasCreated = wasCreated;
    }

    /// <summary>True the first time this shard binding was inserted; false on every idempotent repeat.</summary>
    public bool WasCreated { get; }

    public static CloudShardBindingBootstrapResult Created() => new(true);

    public static CloudShardBindingBootstrapResult AlreadyMatches() => new(false);
}
