namespace ACE.Cloud.Persistence;

/// <summary>
/// The production <see cref="IProtectedAssetBlobStore"/>: plain local-disk files under
/// <see cref="CloudAssetStorageOptions.RootDirectory"/>. Every relative path passed in is resolved
/// with <see cref="Path.GetFullPath(string)"/> and verified to still be under the configured root
/// before any I/O happens -- defense in depth on top of
/// <see cref="ACE.Cloud.Domain.CloudAssetStagingPathPolicy"/> already guaranteeing every caller-built
/// path is traversal-free.
/// </summary>
public sealed class LocalProtectedAssetBlobStore : IProtectedAssetBlobStore
{
    private readonly string _root;

    public LocalProtectedAssetBlobStore(CloudAssetStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException("Protected asset storage requires a configured root directory.", nameof(options));
        }

        _root = Path.GetFullPath(options.RootDirectory);
    }

    public async Task WriteAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveAndCreateParent(relativePath);

        // Write-then-rename: a process crash mid-write leaves only a stray .tmp file, never a
        // half-written file at the path a reader might already be resolving.
        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(content, cancellationToken);
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenWriteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveAndCreateParent(relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(Resolve(relativePath)));

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task CopyAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken = default)
    {
        var sourceFullPath = Resolve(sourceRelativePath);
        var destinationFullPath = ResolveAndCreateParent(destinationRelativePath);
        File.Copy(sourceFullPath, destinationFullPath, overwrite: true);
        return Task.CompletedTask;
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative path is required.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && fullPath != _root)
        {
            throw new ArgumentException($"\"{relativePath}\" resolves outside protected asset storage.", nameof(relativePath));
        }

        return fullPath;
    }

    private string ResolveAndCreateParent(string relativePath)
    {
        var fullPath = Resolve(relativePath);
        var parentDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        return fullPath;
    }
}
