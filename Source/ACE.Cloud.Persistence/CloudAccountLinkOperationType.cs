namespace ACE.Cloud.Persistence;

/// <summary>The kind of operation a <see cref="CloudAccountLinkIdempotencyRecord"/> replays.</summary>
public enum CloudAccountLinkOperationType
{
    Link,
    Unlink,
}
