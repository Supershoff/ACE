using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The ACE-side gateway for Allegiance Vault emptiness checks, Vault Absorption, and out-of-band
/// monarch-deletion detection (issue #17: VAULT-001, VAULT-004, VAULT-005). Deliberately not part of
/// <see cref="CloudCustodyBoundary"/>: an Allegiance Vault is a Cloud ownership/ordinary-transfer
/// concept (CONTEXT.md's Cloud Transaction Authority scope: "vault activity"), not a native-biota
/// world-boundary handoff, and
/// <see cref="ACE.Cloud.RepositoryPolicyTests.CloudWorldBoundaryAuthoritySurfaceTests"/> proves
/// <see cref="CloudCustodyBoundary"/>'s public surface never grows a marketplace/ownership-shaped
/// operation like this one (ARCH-002/ARCH-003). This class exists because VAULT-004/VAULT-005 are
/// specifically triggered by ACE-side seams (an allegiance swear, a character deletion attempt) that
/// have no other way to reach ace_cloud; it models a vault the same way the rest of this schema
/// already anticipates -- as an ordinary <see cref="CloudAccountId"/> derived deterministically from
/// its monarch (<see cref="CloudOwnerIdentity.ForAllegianceVault"/>) -- so it reuses
/// <see cref="CloudCustodyRecord"/>/<see cref="CloudStackLot"/> exactly like any other owner, with no
/// separate vault-contents table.
/// </summary>
public sealed class CloudAllegianceVaultGateway
{
    private readonly CloudDbContext _context;

    public CloudAllegianceVaultGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Whether <paramref name="monarchCharacterId"/>'s Allegiance Vault currently has any contents
    /// (VAULT-005's guard input). Also ensures a <see cref="CloudAllegianceVaultBinding"/> row exists
    /// for this vault identity, so a later integrity scan
    /// (<see cref="DetectOutOfBandMonarchVaultOrphansAsync"/>) can find it even if it turns out to be
    /// empty right now.
    /// </summary>
    public async Task<bool> GetIsEmptyAsync(string shardId, uint monarchCharacterId, CancellationToken cancellationToken = default)
    {
        var vaultOwnerId = await EnsureBindingAsync(shardId, monarchCharacterId, cancellationToken);
        return await IsVaultOwnerEmptyAsync(vaultOwnerId, cancellationToken);
    }

    /// <summary>
    /// Absorbs every item from <paramref name="oldMonarchCharacterId"/>'s Allegiance Vault into
    /// <paramref name="newMonarchCharacterId"/>'s (VAULT-004), atomically, when the former monarch
    /// joins the latter's allegiance. Absorbing an already-empty vault is a valid no-op success, not
    /// a conflict: a monarch with no vault contents may freely swear into another allegiance.
    /// Vault contents can never carry an active exclusive reservation (VAULT-003: an Allegiance Vault
    /// cannot create Withdrawal Tokens, listings, bids, or external Transfer Offers), so unlike an
    /// ordinary <see cref="CloudOwnershipTransferPolicy"/> transfer there is no per-item reservation
    /// precondition to revalidate here -- only the top-level gate/identity check.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudVaultAbsorptionResult>> AbsorbAsync(
        string shardId,
        uint oldMonarchCharacterId,
        uint newMonarchCharacterId,
        CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var sourceVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(shardId, oldMonarchCharacterId);
        var destinationVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(shardId, newMonarchCharacterId);

        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);
        var policyResult = CloudAllegianceVaultAbsorptionPolicy.Absorb(
            new CloudAccountId(sourceVaultOwnerId), new CloudAccountId(destinationVaultOwnerId), gateState);
        if (!policyResult.IsSuccess)
        {
            await RecordAbsorptionFailureDiagnosticAsync(
                shardId, oldMonarchCharacterId, sourceVaultOwnerId,
                $"Vault Absorption from monarch {oldMonarchCharacterId} into {newMonarchCharacterId} was refused: {policyResult.Reason}. "
                    + "The former monarch's Allegiance Vault requires audited administrator recovery (VAULT-004).",
                cancellationToken);

            return CloudBoundaryOutcome<CloudVaultAbsorptionResult>.Conflict(policyResult.Reason!);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await EnsureBindingAsync(shardId, oldMonarchCharacterId, cancellationToken);
        await EnsureBindingAsync(shardId, newMonarchCharacterId, cancellationToken);

        var custodyRecords = await _context.CloudCustodyRecords
            .Where(r => r.OwnerId == sourceVaultOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var record in custodyRecords)
        {
            record.ChangeOwner(destinationVaultOwnerId);
        }

        var stackLots = await _context.CloudStackLots
            .Where(l => l.OwnerId == sourceVaultOwnerId)
            .ToListAsync(cancellationToken);
        foreach (var lot in stackLots)
        {
            lot.ChangeOwner(destinationVaultOwnerId);
        }

        var stackLotBackingBiotaIds = await _context.CloudCustodyRecords
            .Where(r => stackLots.Select(l => l.CustodyRecordId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.BiotaId, cancellationToken);

        var movedBiotaIds = custodyRecords.Select(r => r.BiotaId)
            .Concat(stackLots.Select(l => stackLotBackingBiotaIds[l.CustodyRecordId]))
            .ToList();

        await AppendAbsorptionLedgerAndOutboxAsync(shardId, destinationVaultOwnerId, movedBiotaIds, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudVaultAbsorptionResult>.Committed(
            new CloudVaultAbsorptionResult(custodyRecords.Count, stackLots.Count));
    }

    /// <summary>
    /// Appends one <see cref="CloudActivityLedgerEvent"/> and one matching
    /// <see cref="CloudCustodyOutboxEvent"/> per moved item (issue #17 review, finding 2 / P1),
    /// analogous to <see cref="CloudCustodyBoundary"/>'s own append-ledger-and-outbox pattern for
    /// every other Cloud ownership transfer, so a successful Vault Absorption preserves provenance
    /// (CONTEXT.md) and lets the companion web's read model catch up by replaying the Custody Outbox
    /// instead of silently diverging from actual ownership.
    /// </summary>
    private async Task AppendAbsorptionLedgerAndOutboxAsync(
        string shardId, Guid destinationVaultOwnerId, IReadOnlyList<uint> movedBiotaIds, CancellationToken cancellationToken)
    {
        if (movedBiotaIds.Count == 0)
        {
            return;
        }

        var correlationId = Guid.NewGuid();

        foreach (var biotaId in movedBiotaIds)
        {
            _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                correlationId, shardId, CloudBoundaryOperationType.VaultAbsorption, biotaId, destinationVaultOwnerId, CloudBoundaryOutcomeKind.Committed));
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var biotaId in movedBiotaIds)
        {
            var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
            _context.CloudCustodyOutboxEvents.Add(new CloudCustodyOutboxEvent(
                correlationId, shardId, CloudBoundaryOperationType.VaultAbsorption, biotaId, destinationVaultOwnerId, sequenceNumber));
        }
    }

    /// <summary>
    /// Locks <see cref="CloudCustodyOutboxSequence"/>'s single row and returns the next durable order
    /// position, the same locking approach <see cref="CloudCustodyBoundary"/> uses for every other
    /// Custody Outbox append (ARCH-007).
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

    /// <summary>
    /// Scans every currently known Allegiance Vault on this shard (from
    /// <see cref="CloudAllegianceVaultBinding"/>) for one whose monarch character no longer exists
    /// (or has been soft-deleted) in ace_shard while the vault still has contents, and records a
    /// <see cref="CloudMonarchDeletionDiagnostic"/> for each newly found one (VAULT-005's
    /// out-of-band recovery case). A vault already diagnosed is never re-diagnosed; resolving it is
    /// an audited administrator workflow outside this issue's scope. Safe to call repeatedly, for
    /// example once at ACE startup.
    /// </summary>
    public async Task<IReadOnlyList<CloudMonarchDeletionDiagnostic>> DetectOutOfBandMonarchVaultOrphansAsync(
        string shardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Detecting out-of-band monarch vault orphans requires a Cloud Shard ID.", nameof(shardId));
        }

        var bindings = await _context.CloudAllegianceVaultBindings
            .AsNoTracking()
            .Where(b => b.ShardId == shardId)
            .ToListAsync(cancellationToken);

        var alreadyDiagnosedMonarchIds = (await _context.CloudMonarchDeletionDiagnostics
            .AsNoTracking()
            .Where(d => d.ShardId == shardId)
            .Select(d => d.MonarchCharacterId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var newDiagnostics = new List<CloudMonarchDeletionDiagnostic>();

        foreach (var binding in bindings)
        {
            if (alreadyDiagnosedMonarchIds.Contains(binding.MonarchCharacterId))
            {
                continue;
            }

            if (await IsVaultOwnerEmptyAsync(binding.OwnerId, cancellationToken))
            {
                continue;
            }

            string reason;
            if (!await CharacterExistsAndIsNotDeletedAsync(binding.MonarchCharacterId, cancellationToken))
            {
                reason =
                    $"Monarch character {binding.MonarchCharacterId} no longer exists (or was deleted) in ace_shard, but Allegiance "
                        + $"Vault {binding.OwnerId} still has contents. This character was removed out-of-band, not through ACE's own "
                        + "guarded deletion path; the vault requires audited administrator recovery (VAULT-005).";
            }
            else if (!await IsStillALiveMonarchAsync(binding.MonarchCharacterId, cancellationToken))
            {
                // Issue #17 review, finding 2 (P1): the character still exists, but has since sworn
                // allegiance to someone else -- i.e. a VAULT-004 Vault Absorption should have moved
                // this vault's contents into their new monarch's vault and never did (a failed or
                // refused Absorption that only ever left a log line). This state-based check catches
                // that even when the failure happened while Cloud itself was unreachable, unlike
                // RecordAbsorptionFailureDiagnosticAsync, which can only record what it could reach.
                reason =
                    $"Character {binding.MonarchCharacterId} still exists but is no longer the monarch of Allegiance Vault "
                        + $"{binding.OwnerId}, which still has contents. A VAULT-004 Vault Absorption into their new monarch's "
                        + "vault likely failed or was never completed; the vault requires audited administrator recovery (VAULT-004).";
            }
            else
            {
                continue;
            }

            var diagnostic = new CloudMonarchDeletionDiagnostic(shardId, binding.MonarchCharacterId, binding.OwnerId, reason);
            _context.CloudMonarchDeletionDiagnostics.Add(diagnostic);
            newDiagnostics.Add(diagnostic);
        }

        if (newDiagnostics.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return newDiagnostics;
    }

    /// <summary>
    /// Idempotently records (or returns the already-recorded) reverse-lookup binding from a monarch's
    /// deterministic Allegiance Vault owner identity back to the monarch, so a later integrity scan
    /// can enumerate every known vault (see <see cref="CloudAllegianceVaultBinding"/>'s doc comment).
    /// </summary>
    private async Task<Guid> EnsureBindingAsync(string shardId, uint monarchCharacterId, CancellationToken cancellationToken)
    {
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(shardId, monarchCharacterId);

        var exists = await _context.CloudAllegianceVaultBindings
            .AsNoTracking()
            .AnyAsync(b => b.OwnerId == vaultOwnerId, cancellationToken);
        if (!exists)
        {
            _context.CloudAllegianceVaultBindings.Add(new CloudAllegianceVaultBinding(vaultOwnerId, shardId, monarchCharacterId));

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
            {
                // A concurrent caller already inserted this exact binding; that is the desired
                // outcome (the binding exists), not a failure.
                _context.ChangeTracker.Clear();
            }
        }

        return vaultOwnerId;
    }

    /// <summary>
    /// Records a durable, admin-visible <see cref="CloudMonarchDeletionDiagnostic"/> for a failed
    /// VAULT-004 Vault Absorption (issue #17 review, finding 2 / P1): before this fix, a refused or
    /// failed Absorption left only a log line -- not queryable, not part of the Activity Ledger, and
    /// invisible to <see cref="DetectOutOfBandMonarchVaultOrphansAsync"/> (which only ever looked for
    /// a monarch whose character row was gone, not one who simply stopped being a monarch). A vault
    /// already diagnosed (by this or the out-of-band scan) is never re-diagnosed, matching
    /// <see cref="CloudMonarchDeletionDiagnostic"/>'s (ShardId, MonarchCharacterId) uniqueness.
    /// </summary>
    private async Task RecordAbsorptionFailureDiagnosticAsync(
        string shardId, uint oldMonarchCharacterId, Guid vaultOwnerId, string reason, CancellationToken cancellationToken)
    {
        var alreadyDiagnosed = await _context.CloudMonarchDeletionDiagnostics
            .AsNoTracking()
            .AnyAsync(d => d.ShardId == shardId && d.MonarchCharacterId == oldMonarchCharacterId, cancellationToken);
        if (alreadyDiagnosed)
        {
            return;
        }

        _context.CloudMonarchDeletionDiagnostics.Add(
            new CloudMonarchDeletionDiagnostic(shardId, oldMonarchCharacterId, vaultOwnerId, reason));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            // A concurrent caller (or the out-of-band scan) already diagnosed this monarch's vault;
            // that is the desired outcome, not a failure.
            _context.ChangeTracker.Clear();
        }
    }

    private async Task<bool> IsVaultOwnerEmptyAsync(Guid vaultOwnerId, CancellationToken cancellationToken)
    {
        var hasCustodyRecord = await _context.CloudCustodyRecords
            .AsNoTracking()
            .AnyAsync(r => r.OwnerId == vaultOwnerId, cancellationToken);
        if (hasCustodyRecord)
        {
            return false;
        }

        var hasStackLot = await _context.CloudStackLots
            .AsNoTracking()
            .AnyAsync(l => l.OwnerId == vaultOwnerId, cancellationToken);
        return !hasStackLot;
    }

    /// <summary>
    /// Whether <paramref name="characterId"/> still exists in ace_shard and has not been (soft-)
    /// deleted, queried directly against ace_shard.character on this same connection -- the same
    /// cross-schema reach <see cref="CloudCustodyBoundary"/> already uses for biota rows (ARCH-002).
    /// </summary>
    private async Task<bool> CharacterExistsAndIsNotDeletedAsync(uint characterId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT COUNT(*) FROM ace_shard.character WHERE id = @id AND is_Deleted = 0;";
            CloudRawSqlHelpers.AddParameter(command, "@id", characterId);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return count > 0;
        }
        catch (MySqlConnector.MySqlException ex) when (CloudRawSqlHelpers.IsAccessDenied(ex))
        {
            throw new CloudDatabasePrivilegeException();
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Whether <paramref name="characterId"/> is still their own live monarch (VAULT-004): queried
    /// directly against their persisted Monarch instance property (PropertyInstanceId.Monarch = 26)
    /// in ace_shard on this same connection, the same cross-schema reach
    /// <see cref="CharacterExistsAndIsNotDeletedAsync"/> already uses. A player who has never sworn
    /// allegiance to anyone (or who leads their own allegiance) has no such row, which means "still
    /// their own monarch"; a row whose value differs from <paramref name="characterId"/> means they
    /// have since sworn allegiance to someone else -- exactly the moment a VAULT-004 Vault Absorption
    /// should have emptied this vault.
    /// </summary>
    private async Task<bool> IsStillALiveMonarchAsync(uint characterId, CancellationToken cancellationToken)
    {
        const short monarchPropertyType = 26; // PropertyInstanceId.Monarch

        var connection = _context.Database.GetDbConnection();
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT value FROM ace_shard.biota_properties_i_i_d WHERE object_Id = @id AND type = @type;";
            CloudRawSqlHelpers.AddParameter(command, "@id", characterId);
            CloudRawSqlHelpers.AddParameter(command, "@type", monarchPropertyType);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null or DBNull)
            {
                return true;
            }

            return Convert.ToUInt32(result) == characterId;
        }
        catch (MySqlConnector.MySqlException ex) when (CloudRawSqlHelpers.IsAccessDenied(ex))
        {
            throw new CloudDatabasePrivilegeException();
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}
