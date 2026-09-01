using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>The committed result of one <see cref="CloudMonarchVaultRecoveryGateway.RecoverAsync"/> call.</summary>
public sealed record CloudMonarchVaultRecoveryTransferResult(
    Guid DiagnosticId, Guid DestinationOwnerId, int CustodyRecordsMoved, int StackLotsMoved)
{
    public int TotalItemsMoved => CustodyRecordsMoved + StackLotsMoved;
}

/// <summary>Interface-extracted so <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake.</summary>
public interface ICloudMonarchVaultRecoveryDiagnosticReader
{
    /// <summary>Every out-of-band monarch deletion diagnostic on this shard awaiting an administrator decision (VAULT-005).</summary>
    Task<IReadOnlyList<CloudMonarchDeletionDiagnostic>> GetUnresolvedAsync(string shardId, CancellationToken cancellationToken = default);
}

/// <summary>Interface-extracted so <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake.</summary>
public interface ICloudMonarchVaultRecoveryService
{
    Task<CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>> RecoverAsync(
        string shardId,
        Guid diagnosticId,
        uint adminAccountId,
        uint destinationAccountId,
        bool destinationAccountExists,
        string? reason,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The audited administrator recovery path for VAULT-005's out-of-band monarch deletion case
/// (issue #38, ADM-002): "An out-of-band monarch deletion leaves the vault available only for
/// audited administrator recovery" and never guesses a successor -- the destination is always
/// whatever the administrator explicitly typed. Deliberately a separate class from
/// <see cref="CloudAllegianceVaultGateway"/> (issue #17's existing emptiness-check/Absorption/
/// diagnostic-detection gateway, whose constructor takes no <see cref="ICloudAccountOwnershipResolver"/>
/// and is already exercised by ACE-side call sites with that single-argument shape) for the exact
/// same reason <see cref="CloudAllegianceVaultTransactionGateway"/> is: this needs to resolve an
/// administrator-supplied destination account to its effective Main Account, a capability those
/// established callers never need.
///
/// The diagnostic row itself is this operation's idempotency guard, not a separately issued key: it
/// is locked (<c>FOR UPDATE</c>) and its <see cref="CloudMonarchDeletionDiagnostic.IsResolved"/> flag
/// is rechecked inside the transaction, exactly like every other Cloud Transaction Authority
/// mutation's double-check pattern. A process crash before commit leaves <c>IsResolved</c> false (the
/// whole transaction rolls back), so a retried request safely re-attempts the same recovery; a
/// concurrent or replayed request that arrives after a successful commit finds <c>IsResolved = true</c>
/// and is refused as a Conflict instead of moving anything a second time -- "inability to override
/// committed transfers" (issue #38's Red requirement).
/// </summary>
public sealed class CloudMonarchVaultRecoveryGateway : ICloudMonarchVaultRecoveryDiagnosticReader, ICloudMonarchVaultRecoveryService
{
    private readonly CloudDbContext _context;
    private readonly ICloudAccountOwnershipResolver _ownershipResolver;

    public CloudMonarchVaultRecoveryGateway(CloudDbContext context, ICloudAccountOwnershipResolver ownershipResolver)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ownershipResolver = ownershipResolver ?? throw new ArgumentNullException(nameof(ownershipResolver));
    }

    public async Task<IReadOnlyList<CloudMonarchDeletionDiagnostic>> GetUnresolvedAsync(
        string shardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Listing unresolved Allegiance Vault recovery diagnostics requires a Cloud Shard ID.", nameof(shardId));
        }

        return await _context.CloudMonarchDeletionDiagnostics.AsNoTracking()
            .Where(d => d.ShardId == shardId && !d.IsResolved)
            .OrderBy(d => d.DetectedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Moves every item from a diagnosed orphaned Allegiance Vault into <paramref name="destinationAccountId"/>'s
    /// own personal Cloud Inventory and records the administrator's decision. Callers must have
    /// already revalidated <paramref name="adminAccountId"/> as accessLevel 5 for this exact request
    /// (ADM-001) before calling this -- see the resulting <see cref="CloudBoundaryOutcome{T}.Reason"/>
    /// when they have not, since this itself also refuses without a fresh revalidation. Likewise,
    /// <paramref name="destinationAccountExists"/> must be a fresh Auth Bridge existence check for
    /// <paramref name="destinationAccountId"/> (mirroring the same ADM-001 discipline): this refuses
    /// rather than commits when it is false, since a committed recovery can never be re-applied and a
    /// typo'd destination would otherwise permanently strand the vault's contents.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>> RecoverAsync(
        string shardId,
        Guid diagnosticId,
        uint adminAccountId,
        uint destinationAccountId,
        bool destinationAccountExists,
        string? reason,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Allegiance Vault recovery requires a Cloud Shard ID.", nameof(shardId));
        }

        if (diagnosticId == Guid.Empty)
        {
            throw new ArgumentException("An Allegiance Vault recovery requires a diagnostic ID.", nameof(diagnosticId));
        }

        if (adminAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adminAccountId), "An Allegiance Vault recovery requires a real administrator account ID.");
        }

        _context.ChangeTracker.Clear();

        var destinationOwnerId = destinationAccountId == 0
            ? Guid.Empty
            : CloudOwnerIdentity.ForAccount(
                shardId, await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, destinationAccountId, cancellationToken));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var diagnostic = await _context.CloudMonarchDeletionDiagnostics
            .FromSqlInterpolated($"SELECT * FROM CloudMonarchDeletionDiagnostic WHERE Id = {diagnosticId} AND ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);

        var policyResult = CloudMonarchVaultRecoveryPolicy.Authorize(new CloudMonarchVaultRecoveryRequest(
            AdminAuthorized: true, // callers only ever reach this after a fresh ADM-001 revalidation (see this method's own doc comment)
            GateState: gateState,
            DiagnosticFound: diagnostic is not null,
            AlreadyResolved: diagnostic?.IsResolved ?? false,
            Reason: reason,
            Confirmed: confirmed,
            SourceVaultOwnerId: diagnostic?.VaultOwnerId ?? Guid.Empty,
            DestinationOwnerId: destinationOwnerId,
            DestinationAccountExists: destinationAccountExists));

        if (!policyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>.Conflict(policyResult.Reason!);
        }

        var sourceVaultOwnerId = diagnostic!.VaultOwnerId;

        var custodyRecords = await _context.CloudCustodyRecords
            .Where(r => r.OwnerId == sourceVaultOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var record in custodyRecords)
        {
            record.ChangeOwner(destinationOwnerId);
        }

        var stackLots = await _context.CloudStackLots
            .Where(l => l.OwnerId == sourceVaultOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var lot in stackLots)
        {
            lot.ChangeOwner(destinationOwnerId);
        }

        var stackLotBackingBiotaIds = await _context.CloudCustodyRecords
            .Where(r => stackLots.Select(l => l.CustodyRecordId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.BiotaId, cancellationToken);

        var movedBiotaIds = custodyRecords.Select(r => r.BiotaId)
            .Concat(stackLots.Select(l => stackLotBackingBiotaIds[l.CustodyRecordId]))
            .ToList();

        diagnostic.Resolve(adminAccountId, reason!, destinationOwnerId);
        _context.CloudMonarchDeletionDiagnostics.Update(diagnostic);

        var correlationId = await AppendRecoveryLedgerAndOutboxAsync(shardId, destinationOwnerId, movedBiotaIds, reason!, cancellationToken);

        if (correlationId is { } sourceEventId)
        {
            await AddDirectNotificationAsync(shardId, destinationOwnerId, sourceEventId, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudMonarchVaultRecoveryTransferResult>.Committed(
            new CloudMonarchVaultRecoveryTransferResult(diagnostic.Id, destinationOwnerId, custodyRecords.Count, stackLots.Count));
    }

    /// <summary>
    /// Appends one <see cref="CloudActivityLedgerEvent"/> and one matching
    /// <see cref="CloudCustodyOutboxEvent"/> per moved item (EVT-001, ADM-002). A diagnostic resolved
    /// while its vault happens to already be empty (a rare race with some other actor emptying it
    /// between detection and this recovery) still durably records the administrator's decision on the
    /// <see cref="CloudMonarchDeletionDiagnostic"/> row itself -- <see cref="CloudBoundaryOperationType"/>'s
    /// per-item ledger/outbox shape has no biota to attach to when nothing moved, so this simply
    /// appends nothing in that case rather than recording a placeholder against a real native biota's
    /// own identity space.
    /// </summary>
    private async Task<Guid?> AppendRecoveryLedgerAndOutboxAsync(
        string shardId, Guid destinationOwnerId, IReadOnlyList<uint> movedBiotaIds, string reason, CancellationToken cancellationToken)
    {
        if (movedBiotaIds.Count == 0)
        {
            return null;
        }

        var correlationId = Guid.NewGuid();

        foreach (var biotaId in movedBiotaIds)
        {
            _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                correlationId, shardId, CloudBoundaryOperationType.AdminVaultRecovery, biotaId, destinationOwnerId, CloudBoundaryOutcomeKind.Committed, reason));
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var biotaId in movedBiotaIds)
        {
            var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
            _context.CloudCustodyOutboxEvents.Add(new CloudCustodyOutboxEvent(
                correlationId, shardId, CloudBoundaryOperationType.AdminVaultRecovery, biotaId, destinationOwnerId, sequenceNumber));
        }

        return correlationId;
    }

    /// <summary>ADM-002: "Affected owners receive the administrator's intervention reason in an in-app notification" -- the reason itself lives on the ledger entry this notification links to.</summary>
    private async Task AddDirectNotificationAsync(string shardId, Guid ownerId, Guid sourceEventId, CancellationToken cancellationToken)
    {
        var sequenceNumber = await CloudLiveStreamSequenceReserver.ReserveNextAsync(_context, cancellationToken);

        _context.CloudNotifications.Add(
            CloudNotification.CreateFirst(shardId, ownerId, CloudNotificationKind.AdminVaultRecoveryApplied, "/dashboard", sourceEventId, sequenceNumber));
        _context.CloudLiveStreamEvents.Add(new CloudLiveStreamEvent(shardId, sequenceNumber, isPublic: false, ownerId, "Notification", sourceEventId, sequenceNumber));
    }

    /// <summary>
    /// Locks <see cref="CloudCustodyOutboxSequence"/>'s single row and returns the next durable order
    /// position, the same locking approach every other Custody Outbox append in this project uses
    /// (ARCH-007).
    /// </summary>
    private async Task<long> ReserveNextOutboxSequenceNumberAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        long reserved;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT NextValue FROM CloudCustodyOutboxSequence WHERE Id = 1 FOR UPDATE;";
            reserved = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE CloudCustodyOutboxSequence SET NextValue = @nextValue WHERE Id = 1;";
            CloudRawSqlHelpers.AddParameter(update, "@nextValue", reserved + 1);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }
}
