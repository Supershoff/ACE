namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of resolving one <see cref="CloudIconLayerReference"/>: either a usable decoded
/// <see cref="Raster"/>, or one specific <see cref="CloudIconLayerResolutionOutcomeKind"/> failure
/// reason. <see cref="Raster"/> is non-null if and only if <see cref="Outcome"/> is
/// <see cref="CloudIconLayerResolutionOutcomeKind.Resolved"/>, enforced by the constructor so callers
/// can never observe an inconsistent pairing.
/// </summary>
public sealed record CloudIconLayerResolution
{
    public CloudIconLayerResolutionOutcomeKind Outcome { get; }

    public CloudIconRasterLayer? Raster { get; }

    private CloudIconLayerResolution(CloudIconLayerResolutionOutcomeKind outcome, CloudIconRasterLayer? raster)
    {
        if ((outcome == CloudIconLayerResolutionOutcomeKind.Resolved) != (raster is not null))
        {
            throw new ArgumentException("A raster is required if and only if the outcome is Resolved.");
        }

        Outcome = outcome;
        Raster = raster;
    }

    public static CloudIconLayerResolution Resolved(CloudIconRasterLayer raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        return new CloudIconLayerResolution(CloudIconLayerResolutionOutcomeKind.Resolved, raster);
    }

    public static CloudIconLayerResolution Failed(CloudIconLayerResolutionOutcomeKind reason)
    {
        if (reason == CloudIconLayerResolutionOutcomeKind.Resolved)
        {
            throw new ArgumentException("Use Resolved(...) for a successful resolution.", nameof(reason));
        }

        return new CloudIconLayerResolution(reason, null);
    }
}
