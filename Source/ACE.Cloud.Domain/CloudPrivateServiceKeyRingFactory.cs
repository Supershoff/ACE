namespace ACE.Cloud.Domain;

/// <summary>Builds a <see cref="CloudPrivateServiceKeyRing"/> from base64-encoded configuration values shared by every host that signs/verifies against it.</summary>
public static class CloudPrivateServiceKeyRingFactory
{
    public static CloudPrivateServiceKeyRing Create(
        string activeKeyId, string activeKeySecretBase64, string? previousKeyId, string? previousKeySecretBase64)
    {
        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            throw new ArgumentException("An active service key ID is required.", nameof(activeKeyId));
        }

        if (string.IsNullOrWhiteSpace(activeKeySecretBase64))
        {
            throw new ArgumentException("An active service key secret is required.", nameof(activeKeySecretBase64));
        }

        var activeKey = new CloudPrivateServiceKey(activeKeyId, Convert.FromBase64String(activeKeySecretBase64));

        if (string.IsNullOrWhiteSpace(previousKeyId) && string.IsNullOrWhiteSpace(previousKeySecretBase64))
        {
            return new CloudPrivateServiceKeyRing(activeKey);
        }

        if (string.IsNullOrWhiteSpace(previousKeyId) || string.IsNullOrWhiteSpace(previousKeySecretBase64))
        {
            throw new ArgumentException("A previous service key requires both a key ID and a secret, or neither.");
        }

        var previousKey = new CloudPrivateServiceKey(previousKeyId, Convert.FromBase64String(previousKeySecretBase64));
        return new CloudPrivateServiceKeyRing(activeKey, previousKey);
    }
}
