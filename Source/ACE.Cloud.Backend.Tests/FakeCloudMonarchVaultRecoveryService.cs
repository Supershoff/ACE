using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudMonarchVaultRecoveryDiagnosticReader"/>/<see cref="ICloudMonarchVaultRecoveryService"/>
/// substitute that still runs the real, pure <see cref="CloudMonarchVaultRecoveryPolicy"/> over
/// test-seeded diagnostics, mirroring <c>FakeCloudActivityLedgerQueryReader</c>'s exact shape. ADM-001
/// authorization is assumed already revalidated by the caller (matching
/// <see cref="CloudMonarchVaultRecoveryGateway.RecoverAsync"/>'s own established contract), since the
/// endpoint itself is what Backend endpoint tests exercise for the unauthenticated/non-admin cases.
/// </summary>
internal sealed class FakeCloudMonarchVaultRecoveryService : ICloudMonarchVaultRecoveryDiagnosticReader, ICloudMonarchVaultRecoveryService
{
    public List<CloudMonarchDeletionDiagnostic> Diagnostics { get; } = [];

    public CloudMutationGateState GateState { get; set; } = CloudMutationGateState.Open;

    public Task<IReadOnlyList<CloudMonarchDeletionDiagnostic>> GetUnresolvedAsync(string shardId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudMonarchDeletionDiagnostic>>(
            Diagnostics.Where(d => d.ShardId == shardId && !d.IsResolved).ToList());

    public Task<CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>> RecoverAsync(
        string shardId,
        Guid diagnosticId,
        uint adminAccountId,
        uint destinationAccountId,
        bool destinationAccountExists,
        string? reason,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var diagnostic = Diagnostics.SingleOrDefault(d => d.ShardId == shardId && d.Id == diagnosticId);
        var destinationOwnerId = destinationAccountId == 0 ? Guid.Empty : CloudOwnerIdentity.ForAccount(shardId, destinationAccountId);

        var policyResult = CloudMonarchVaultRecoveryPolicy.Authorize(new CloudMonarchVaultRecoveryRequest(
            AdminAuthorized: true,
            GateState: GateState,
            DiagnosticFound: diagnostic is not null,
            AlreadyResolved: diagnostic?.IsResolved ?? false,
            Reason: reason,
            Confirmed: confirmed,
            SourceVaultOwnerId: diagnostic?.VaultOwnerId ?? Guid.Empty,
            DestinationOwnerId: destinationOwnerId,
            DestinationAccountExists: destinationAccountExists));

        if (!policyResult.IsSuccess)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>.Conflict(policyResult.Reason!));
        }

        diagnostic!.Resolve(adminAccountId, reason!, destinationOwnerId);

        return Task.FromResult(CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>.Committed(
            new CloudMonarchVaultRecoveryTransferResult(diagnostic.Id, destinationOwnerId, CustodyRecordsMoved: 1, StackLotsMoved: 0)));
    }
}
