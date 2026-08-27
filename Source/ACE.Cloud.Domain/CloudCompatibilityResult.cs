namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of comparing two sides of a Cloud boundary transaction's component versions.
/// </summary>
public sealed record CloudCompatibilityResult
{
    private CloudCompatibilityResult(bool isCompatible, CloudVersionComponent? incompatibleComponent, string? reason)
    {
        IsCompatible = isCompatible;
        IncompatibleComponent = incompatibleComponent;
        Reason = reason;
    }

    public bool IsCompatible { get; }

    public CloudVersionComponent? IncompatibleComponent { get; }

    public string? Reason { get; }

    public static CloudCompatibilityResult Compatible() => new(true, null, null);

    public static CloudCompatibilityResult Incompatible(CloudVersionComponent component, string reason) =>
        new(false, component, reason);
}
