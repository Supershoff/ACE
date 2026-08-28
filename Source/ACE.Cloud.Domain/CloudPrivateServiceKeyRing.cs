namespace ACE.Cloud.Domain;

/// <summary>One symmetric private-service key: an opaque ID plus its raw secret bytes.</summary>
public sealed class CloudPrivateServiceKey
{
    public CloudPrivateServiceKey(string keyId, byte[] secret)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException("A private-service key requires a non-empty key ID.", nameof(keyId));
        }

        if (secret is null || secret.Length == 0)
        {
            throw new ArgumentException("A private-service key requires a non-empty secret.", nameof(secret));
        }

        KeyId = keyId;
        Secret = secret;
    }

    public string KeyId { get; }

    public byte[] Secret { get; }
}

/// <summary>
/// The symmetric key(s) used to authenticate requests/tokens between the ACE Auth Bridge and the
/// Cloud backend (security baseline: "Private-service authentication between Cloud backend, Auth
/// Bridge, and ACE boundary endpoints; bind privately and support key rotation"). New
/// signatures/grants always use <see cref="ActiveKey"/>; <see cref="TryGetKey"/> also recognizes a
/// configured <see cref="PreviousKey"/> so tokens/requests signed just before a rotation still
/// validate during the deployment's overlap window, without ever signing anything new with it.
/// </summary>
public sealed class CloudPrivateServiceKeyRing
{
    private readonly Dictionary<string, CloudPrivateServiceKey> _keysById;

    public CloudPrivateServiceKeyRing(CloudPrivateServiceKey activeKey, CloudPrivateServiceKey? previousKey = null)
    {
        ActiveKey = activeKey ?? throw new ArgumentNullException(nameof(activeKey));
        PreviousKey = previousKey;

        if (previousKey is not null && previousKey.KeyId == activeKey.KeyId)
        {
            throw new ArgumentException("The previous key must have a different key ID than the active key.", nameof(previousKey));
        }

        _keysById = previousKey is null
            ? new Dictionary<string, CloudPrivateServiceKey> { [activeKey.KeyId] = activeKey }
            : new Dictionary<string, CloudPrivateServiceKey> { [activeKey.KeyId] = activeKey, [previousKey.KeyId] = previousKey };
    }

    public CloudPrivateServiceKey ActiveKey { get; }

    public CloudPrivateServiceKey? PreviousKey { get; }

    public bool TryGetKey(string? keyId, out CloudPrivateServiceKey key)
    {
        if (keyId is null || !_keysById.TryGetValue(keyId, out var found))
        {
            key = null!;
            return false;
        }

        key = found;
        return true;
    }
}
