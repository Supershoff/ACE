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
        CloudProtocolHandshake incoming)
    {
        ArgumentNullException.ThrowIfNull(deploymentShardId);
        ArgumentNullException.ThrowIfNull(expectedVersions);
        ArgumentNullException.ThrowIfNull(incoming);

        if (deploymentShardId != incoming.ShardId)
        {
            return CloudMutationAuthorizationResult.Refused(
                $"Cloud Shard ID mismatch: this deployment serves {deploymentShardId}, not {incoming.ShardId}.");
        }

        var compatibility = CloudCompatibilityChecker.Evaluate(expectedVersions, incoming.Versions);

        return compatibility.IsCompatible
            ? CloudMutationAuthorizationResult.Authorized()
            : CloudMutationAuthorizationResult.Refused(compatibility.Reason!);
    }
}
