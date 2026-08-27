namespace ACE.Cloud.Persistence;

/// <summary>
/// A Cloud custody boundary transition was refused because it would violate exclusivity between
/// world possession and Cloud custody (ARCH-005).
/// </summary>
public sealed class CloudCustodyConflictException : Exception
{
    public CloudCustodyConflictException(string message)
        : base(message)
    {
    }
}
