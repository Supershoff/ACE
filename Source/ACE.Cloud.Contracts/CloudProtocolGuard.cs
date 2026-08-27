using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Refuses a Cloud boundary mutation unless the incoming handshake targets this deployment's
/// Cloud Shard ID (ARCH-001) and matches its expected component versions exactly (OPS-002).
/// </summary>
public static class CloudProtocolGuard
{
    public static CloudMutationAuthorizationResult Authorize(
        CloudShardId deploymentShardId,
        CloudComponentVersions expectedVersions,
        CloudProtocolHandshake incoming) =>
        Authorize(deploymentShardId, expectedVersions, incoming, supportedProtocolWindow: null);

    /// <summary>
    /// Authorizes the same way as the three-argument overload, except that when
    /// <paramref name="supportedProtocolWindow"/> is supplied, the incoming contract protocol
    /// version only needs to fall within that declared inclusive range instead of matching
    /// <paramref name="expectedVersions"/> exactly (OPS-002).
    /// </summary>
    public static CloudMutationAuthorizationResult Authorize(
        CloudShardId deploymentShardId,
        CloudComponentVersions expectedVersions,
        CloudProtocolHandshake incoming,
        CloudSupportedProtocolWindow? supportedProtocolWindow)
    {
        ArgumentNullException.ThrowIfNull(deploymentShardId);
        ArgumentNullException.ThrowIfNull(expectedVersions);
        ArgumentNullException.ThrowIfNull(incoming);

        if (deploymentShardId != incoming.ShardId)
        {
            return CloudMutationAuthorizationResult.Refused(
                $"Cloud Shard ID mismatch: this deployment serves {deploymentShardId}, not {incoming.ShardId}.");
        }

        var compatibility = CloudCompatibilityChecker.Evaluate(expectedVersions, incoming.Versions, supportedProtocolWindow);

        return compatibility.IsCompatible
            ? CloudMutationAuthorizationResult.Authorized()
            : CloudMutationAuthorizationResult.Refused(compatibility.Reason!);
    }
}
