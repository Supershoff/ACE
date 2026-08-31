namespace ACE.Cloud.Persistence;

/// <summary>
/// Thrown by <see cref="CloudShardBindingBootstrapper"/> when an existing <see cref="CloudShardBinding"/>
/// row's identity or versions do not match what was requested (ARCH-001: one immutable Cloud Shard ID
/// per deployment). Never caught to silently proceed -- the acceptance launcher surfaces this message
/// verbatim and stops rather than risk rewriting a different shard's identity.
/// </summary>
public sealed class CloudShardBindingMismatchException : Exception
{
    public CloudShardBindingMismatchException(string message)
        : base(message)
    {
    }
}
