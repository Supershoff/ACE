using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Validates a Cloud boundary command's envelope preconditions before any business rule or
/// mutation runs: the protocol handshake must authorize against this deployment (ARCH-001,
/// OPS-002, reusing <see cref="CloudProtocolGuard"/>), and a command that targets an existing
/// aggregate must present its current authoritative version (ARCH-006, transaction rule 3).
/// </summary>
public static class CloudCommandGuard
{
    public static CloudCommandPreconditionResult Evaluate<TCommand>(
        CloudCommandEnvelope<TCommand> envelope,
        CloudShardId deploymentShardId,
        CloudComponentVersions expectedVersions,
        CloudAggregateVersion? currentAggregateVersion)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(deploymentShardId);
        ArgumentNullException.ThrowIfNull(expectedVersions);

        var authorization = CloudProtocolGuard.Authorize(deploymentShardId, expectedVersions, envelope.Handshake);
        if (!authorization.IsAuthorized)
        {
            return CloudCommandPreconditionResult.Failed(CloudCommandResultKind.ValidationFailed, authorization.Reason!);
        }

        if (envelope.ExpectedVersion is not null)
        {
            if (currentAggregateVersion is null)
            {
                return CloudCommandPreconditionResult.Failed(
                    CloudCommandResultKind.Conflict,
                    $"Expected aggregate version {envelope.ExpectedVersion.Value}, but the aggregate does not currently exist.");
            }

            if (envelope.ExpectedVersion != currentAggregateVersion)
            {
                return CloudCommandPreconditionResult.Failed(
                    CloudCommandResultKind.Conflict,
                    $"Expected aggregate version {envelope.ExpectedVersion.Value}, but the current version is {currentAggregateVersion.Value}.");
            }
        }

        return CloudCommandPreconditionResult.Ok();
    }
}
