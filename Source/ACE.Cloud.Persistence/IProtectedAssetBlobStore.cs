namespace ACE.Cloud.Persistence;

/// <summary>
/// Reads and writes the actual bytes backing Asset Import chunks, assembled uploads, retained
/// source DATs, and staged manifest entries (ASSET-002, ASSET-003). Every method takes only a
/// relative path built by <see cref="ACE.Cloud.Domain.CloudAssetStagingPathPolicy"/> -- never a
/// caller-supplied free-form string -- so no implementation of this interface needs its own
/// path-traversal defense beyond the belt-and-suspenders check <see cref="LocalProtectedAssetBlobStore"/>
/// already applies. Every admin-facing consumer of this interface (Asset Import sessions, retained
/// source DATs, staged manifest entries) stays unreachable from a public (non-admin) HTTP route
/// (ASSET-004: "must not expose the source DAT through... raw download endpoints"). The sole
/// deliberate exception is <see cref="CloudIconDerivativeReader"/>, which a public inventory route may
/// depend on: its method signature structurally cannot resolve anything outside the generated
/// <c>icon-cache/</c> derivative namespace, so it can never become a path to the source DAT.
/// </summary>
public interface IProtectedAssetBlobStore
{
    Task WriteAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Creates (or truncates) <paramref name="relativePath"/> for writing, creating parent directories as needed.</summary>
    Task<Stream> OpenWriteAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    Task CopyAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken = default);
}
