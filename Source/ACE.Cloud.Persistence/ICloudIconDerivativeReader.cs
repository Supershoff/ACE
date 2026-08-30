using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read-only access to Icon Reconstruction's already-composed web-ready PNG derivatives (UI-005,
/// UI-006), narrow enough for a companion-web endpoint to depend on directly. Interface-extracted
/// (mirroring <see cref="ICloudWebSessionStore"/>/<c>ICloudAuthBridgeClient</c>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake instead of standing up real
/// protected asset storage for endpoint tests.
/// </summary>
public interface ICloudIconDerivativeReader
{
    /// <summary>
    /// Returns the composed PNG bytes for <paramref name="cacheKey"/>, or null when no derivative
    /// has been composed for it yet (a stale/never-composed key, or protected storage that has lost
    /// the blob) -- never throws for an ordinary cache miss, matching UI-006's "missing references
    /// use an explicit neutral fallback... rather than silently showing a wrong icon" (the fallback
    /// itself is a UI-layer concern; this reader only ever reports hit or miss).
    /// </summary>
    Task<byte[]?> TryReadAsync(CloudIconCompositionCacheKey cacheKey, CancellationToken cancellationToken = default);
}
