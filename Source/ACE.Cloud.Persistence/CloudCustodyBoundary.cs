using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of the World Boundary Authority's deposit and withdrawal handoffs
/// (ARCH-002, ARCH-006). Callers must be ACE world-boundary code holding a connection privileged to
/// read and write ace_shard; the narrowly privileged companion web identity (ARCH-004) must never
/// be given this class.
///
/// Both <see cref="DepositAsync"/> and <see cref="WithdrawAsync"/> follow the same protocol:
///   1. An idempotency check runs first, outside any new transaction: a repeated call with the same
///      idempotency key replays the already-committed result instead of re-running the handoff
///      (ARCH-006, transaction rules 4 and 8).
///   2. One MariaDB transaction performs deterministic row locking, revalidates the precondition
///      under that lock, mutates custody/possession, and appends the Activity Ledger and Custody
///      Outbox rows -- all before a single commit (transaction rule 5). Holding the lock from
///      validation through commit is what makes that validation also a commit-time revalidation:
///      there is no unlocked window in which the validated fact could change out from under it.
///   3. The idempotency record is written last, inside the same transaction, so a caller can never
///      observe a committed idempotency record whose underlying state change did not also commit.
///
/// This is a complementary, commit-time revalidation layer, not the only enforcement: the
/// ace_shard/ace_cloud triggers added by the AddCloudCustodyRecords migration already reject a
/// conflicting deposit at the database level (MariaDB CHECK constraints cannot express that
/// cross-schema lookup, so triggers are the primary database constraint). Revalidating here too
/// means a missing/misconfigured trigger cannot silently admit a conflict, and callers observe a
/// typed <see cref="CloudBoundaryOutcome{T}"/> instead of a raw provider exception.
/// </summary>
public sealed partial class CloudCustodyBoundary
{
    private readonly CloudDbContext _context;
    private readonly CloudComponentVersions? _expectedVersions;

    public CloudCustodyBoundary(CloudDbContext context)
        : this(context, expectedVersions: null)
    {
    }

    /// <summary>
    /// When <paramref name="expectedVersions"/> is supplied, every mutating method first refuses
    /// with an <see cref="CloudBoundaryOutcomeKind.Unavailable"/> outcome unless the deployed
    /// <see cref="CloudShardBinding"/>'s recorded versions match it exactly (OPS-002: "Refuse
    /// mutations when the ACE extension, Auth Bridge, Cloud schema, and backend protocol versions
    /// are incompatible"). Passing null (the single-argument constructor's behavior) skips that
    /// check entirely, preserving every existing caller's behavior unchanged.
    /// </summary>
    public CloudCustodyBoundary(CloudDbContext context, CloudComponentVersions? expectedVersions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _expectedVersions = expectedVersions;
    }

    /// <summary>
    /// Moves a native biota into Cloud custody, atomically clearing any remaining world possession
    /// (Container, Wielder, or Location) as part of the same commit that creates the Cloud Custody
    /// Record (ARCH-002, ARCH-005) -- so a caller crash or a rejected commit can never leave the
    /// biota with neither world possession nor a Cloud Custody Record. Repeating this call with the
    /// same <paramref name="idempotencyKey"/> after a caller timeout, retry, or crash returns the
    /// original committed result rather than creating a second Cloud Custody Record (transaction
    /// rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudCustodyRecord>> DepositAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null) =>
        DepositAsync(biotaId, shardId, ownerId, idempotencyKey, faultInjector: null, cancellationToken, preservationRequirements);

    /// <summary>
    /// Test-only overload: <paramref name="faultInjector"/> is invoked at every named
    /// <see cref="CloudBoundaryFaultPoint"/> so fault-injection tests can simulate a crash at each
    /// boundary. Internal and reachable only from ACE.Cloud.PersistenceIntegrationTests
    /// (AssemblyInfo.cs); production callers always use the public overload above.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudCustodyRecord>> DepositAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null)
    {
        RequireIdempotencyKey(idempotencyKey);

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryDepositOnceAsync(biotaId, shardId, ownerId, idempotencyKey, faultInjector, preservationRequirements, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a deposit previously started with
    /// <paramref name="idempotencyKey"/>, or null if no such deposit has committed yet. A caller
    /// that timed out waiting for <see cref="DepositAsync"/> must call this instead of inferring
    /// failure (transaction rule 8): the original attempt may still be committing, or may already
    /// have committed without the timed-out caller observing the return value.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudCustodyRecord>?> TryGetDepositOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayDepositAsync(existing, cancellationToken);
    }

    /// <summary>
    /// Returns a native biota from Cloud custody to world possession by granting the recipient
    /// container (ARCH-002, ARCH-005). This is a minimal handoff prototype: it proves the crash-safe
    /// transactional protocol using a direct Container grant, not ACE's full inventory-receive
    /// validation (slots, burden, stack merges, uniqueness), which a later withdrawal-feature issue
    /// adds in front of this boundary.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudWithdrawalResult>> WithdrawAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithdrawAsync(custodyRecordId, expectedVersion, recipientContainerId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudWithdrawalResult>> WithdrawAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken = default)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A withdrawal requires a real recipient container GUID.");
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryWithdrawOnceAsync(custodyRecordId, expectedVersion, recipientContainerId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a withdrawal previously started with
    /// <paramref name="idempotencyKey"/>, or null if no such withdrawal has committed yet. See
    /// <see cref="TryGetDepositOutcomeAsync"/> for why a timed-out caller must call this instead of
    /// inferring failure.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudWithdrawalResult>?> TryGetWithdrawalOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        return ReplayWithdrawal(existing);
    }

    /// <summary>
    /// Moves a stackable native biota into Cloud custody as a stack Cloud Custody Record plus its
    /// initial single Cloud Stack Lot claiming the entire quantity for <paramref name="ownerId"/>
    /// (ARCH-002, ARCH-005, ARCH-010), atomically clearing any remaining world possession as part of
    /// the same commit (see <see cref="DepositAsync"/>'s matching doc comment). <paramref
    /// name="quantity"/> is the exact quantity ACE observed on the live object at deposit time; this
    /// call also writes it to ace_shard's PropertyInt.StackSize row so the persisted native state
    /// matches (idempotent/no-op if it already does). Repeating this call with the same <paramref
    /// name="idempotencyKey"/> replays the original committed result (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudStackDepositResult>> DepositStackAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        int quantity,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null) =>
        DepositStackAsync(biotaId, shardId, ownerId, quantity, idempotencyKey, faultInjector: null, cancellationToken, preservationRequirements);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudStackDepositResult>> DepositStackAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        int quantity,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements = null)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A stack deposit requires a positive quantity.");
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryDepositStackOnceAsync(biotaId, shardId, ownerId, quantity, idempotencyKey, faultInjector, preservationRequirements, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a stack deposit previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet. See
    /// <see cref="TryGetDepositOutcomeAsync"/> for why a timed-out caller must call this instead of
    /// inferring failure.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackDepositResult>?> TryGetStackDepositOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayStackDepositAsync(existing, cancellationToken);
    }

    /// <summary>
    /// Withdraws <paramref name="quantityToWithdraw"/> from a Cloud Stack Lot (ARCH-002, ARCH-005,
    /// ARCH-010, INV-003). If this lot is the only one left claiming its backing stack and the
    /// entire remaining quantity is withdrawn, the original biota is delivered directly, exactly
    /// like a non-stack withdrawal. Otherwise the withdrawal materializes a new native child biota
    /// under <paramref name="materializedBiotaId"/> -- which callers must obtain from ACE's own GUID
    /// allocator (ARCH-010: only ACE may allocate a child GUID; this boundary never invents one) --
    /// and the original biota's GUID stays with whatever quantity remains in Cloud custody
    /// (INV-003's remainder preference). Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed result.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>> WithdrawLotAsync(
        Guid lotId,
        int expectedLotVersion,
        int quantityToWithdraw,
        uint recipientContainerId,
        uint? materializedBiotaId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithdrawLotAsync(lotId, expectedLotVersion, quantityToWithdraw, recipientContainerId, materializedBiotaId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>> WithdrawLotAsync(
        Guid lotId,
        int expectedLotVersion,
        int quantityToWithdraw,
        uint recipientContainerId,
        uint? materializedBiotaId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A withdrawal requires a real recipient container GUID.");
        }

        if (quantityToWithdraw <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityToWithdraw), "A lot withdrawal requires a positive quantity.");
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryWithdrawLotOnceAsync(lotId, expectedLotVersion, quantityToWithdraw, recipientContainerId, materializedBiotaId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a lot withdrawal previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>?> TryGetLotWithdrawalOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : ReplayStackWithdrawal(existing);
    }

    /// <summary>
    /// Reads an account's current Pyreal Remainder (DEP-006), 0 if it has never deposited raw
    /// Pyreals. Callers use this to decide how many MMD biotas to allocate/materialize
    /// <em>before</em> calling <see cref="ConvertPyrealDepositAsync"/> -- this read is not itself
    /// transactionally authoritative (the remainder can change before that call locks it), so a
    /// racing concurrent conversion for the same owner still gets caught and refused as a Conflict
    /// by that call's own locked revalidation, never silently under- or over-converted.
    /// </summary>
    public async Task<long> GetPyrealRemainderAsync(string shardId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading a Pyreal Remainder requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Reading a Pyreal Remainder requires an owner.", nameof(ownerId));
        }

        var remainder = await _context.CloudPyrealRemainders.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OwnerId == ownerId && r.ShardId == shardId, cancellationToken);
        return remainder?.RemainderAmount ?? 0;
    }

    /// <summary>
    /// Converts a raw Pyreal Deposit into MMDs plus an updated Pyreal Remainder (DEP-006), atomically
    /// consuming <paramref name="rawBiotaId"/> (the raw Pyreal coin-stack biota, already removed from
    /// world possession by the caller exactly like an ordinary deposit) and creating a whole-item
    /// Cloud Custody Record for each of <paramref name="mmdBiotaIds"/> -- native biotas the ACE-side
    /// caller must have already allocated (ACE's own GUID allocator, ARCH-002/ARCH-010) and
    /// synchronously persisted to ace_shard with no Container/Wielder/Location, exactly like a normal
    /// off-world Custodian deposit row. This method never allocates a GUID or creates a biota row
    /// itself; it only ever creates Cloud Custody Records for biotas the caller already made.
    ///
    /// The exact expected MMD count is recomputed here, under this account's locked Pyreal Remainder
    /// row, from <see cref="PyrealConversionPolicy.Convert"/>: if <paramref name="mmdBiotaIds"/>'s
    /// count does not match that recomputed count -- for example because a concurrent conversion for
    /// the same owner committed first and moved the remainder the caller's non-transactional read in
    /// <see cref="GetPyrealRemainderAsync"/> did not see -- this call refuses with a Conflict and
    /// commits nothing; the caller must re-read the remainder and retry with freshly allocated MMDs
    /// (mirroring <see cref="WithdrawLotAsync"/>'s established materializedBiotaId mismatch handling).
    /// Repeating this call with the same <paramref name="idempotencyKey"/> replays the original
    /// committed result instead of converting twice (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudPyrealConversionResult>> ConvertPyrealDepositAsync(
        uint rawBiotaId,
        string shardId,
        Guid ownerId,
        long rawPyrealAmount,
        IReadOnlyList<uint> mmdBiotaIds,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ConvertPyrealDepositAsync(rawBiotaId, shardId, ownerId, rawPyrealAmount, mmdBiotaIds, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudPyrealConversionResult>> ConvertPyrealDepositAsync(
        uint rawBiotaId,
        string shardId,
        Guid ownerId,
        long rawPyrealAmount,
        IReadOnlyList<uint> mmdBiotaIds,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (rawBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawBiotaId), "A Pyreal conversion requires the real raw Pyreal biota GUID it consumes.");
        }

        if (rawPyrealAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPyrealAmount), "A Pyreal conversion requires a positive raw amount.");
        }

        ArgumentNullException.ThrowIfNull(mmdBiotaIds);

        if (mmdBiotaIds.Any(id => id == 0))
        {
            throw new ArgumentException("Every MMD biota GUID must be real.", nameof(mmdBiotaIds));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryConvertPyrealDepositOnceAsync(rawBiotaId, shardId, ownerId, rawPyrealAmount, mmdBiotaIds, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a Pyreal conversion previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudPyrealConversionResult>?> TryGetPyrealConversionOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CloudPyrealConversionRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayPyrealConversionAsync(existing, cancellationToken);
    }

    /// <summary>
    /// Withdraws <paramref name="amount"/> from an account's Pyreal Remainder as raw Pyreal coin
    /// stacks (DEP-006), the same minimal direct-container-grant shape as <see cref="WithdrawAsync"/>
    /// (no Withdrawal Token/reservation TTL here; see
    /// <see cref="PyrealRemainderWithdrawalPolicy"/>'s doc comment for that scope decision).
    /// <paramref name="deliveryBiotaIds"/> are native Pyreal coin-stack biotas the ACE-side caller
    /// must have already created (ACE's own factory/GUID allocator) with no Container/Wielder/
    /// Location and a combined Value/StackUnitValue*StackSize summing to exactly
    /// <paramref name="amount"/> -- this method never invents Pyreals or a Marketplace Unit; it only
    /// ever grants biotas the caller already made and whose summed value it revalidates under this
    /// account's locked Pyreal Remainder row. A mismatched sum, or a request for more than the
    /// currently locked remainder actually holds (<see cref="PyrealRemainderWithdrawalPolicy"/>'s
    /// "capacity failure"), refuses with a Conflict and commits nothing -- the remainder stays
    /// exactly as it was and the request may be retried. Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed result (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>> WithdrawPyrealRemainderAsync(
        string shardId,
        Guid ownerId,
        long amount,
        IReadOnlyList<uint> deliveryBiotaIds,
        uint recipientContainerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithdrawPyrealRemainderAsync(shardId, ownerId, amount, deliveryBiotaIds, recipientContainerId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>> WithdrawPyrealRemainderAsync(
        string shardId,
        Guid ownerId,
        long amount,
        IReadOnlyList<uint> deliveryBiotaIds,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A withdrawal requires a real recipient container GUID.");
        }

        ArgumentNullException.ThrowIfNull(deliveryBiotaIds);

        if (deliveryBiotaIds.Count == 0 || deliveryBiotaIds.Any(id => id == 0))
        {
            throw new ArgumentException("A Pyreal Remainder withdrawal requires at least one real delivery biota GUID.", nameof(deliveryBiotaIds));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryWithdrawPyrealRemainderOnceAsync(shardId, ownerId, amount, deliveryBiotaIds, recipientContainerId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a Pyreal Remainder withdrawal previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>?> TryGetPyrealRemainderWithdrawalOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CloudPyrealRemainderWithdrawalRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayPyrealRemainderWithdrawalAsync(existing, cancellationToken);
    }

    /// <summary>
    /// Reads MariaDB's own clock (transaction rule 1: "Use database time... for deadlines"), not
    /// application/browser time, for reservation expiry decisions.
    /// </summary>
    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> TryDepositOnceAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudCustodyRecord>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayDepositAsync(existing, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        // Deposit removes world possession itself, on this same transaction/connection
        // (mirroring GrantContainerAsync's withdrawal-direction counterpart), so the commit below
        // either performs both the shard-side removal and the Cloud-side custody creation or
        // neither: a caller crash or Cloud-side rejection can never leave a biota that has already
        // lost world possession without yet having a Cloud Custody Record (a permanently orphaned,
        // neither-world-nor-Cloud biota).
        await RemoveWorldPossessionAsync(biotaId, cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        var correlationId = Guid.NewGuid();
        var record = new CloudCustodyRecord(biotaId, shardId, ownerId, correlationId);
        _context.CloudCustodyRecords.Add(record);

        var frozenEnchantments = BuildFrozenEnchantments(record.Id, shardId, preservationRequirements);
        if (frozenEnchantments.Count > 0)
        {
            _context.CloudFrozenEnchantments.AddRange(frozenEnchantments);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            // A concurrent caller with the same idempotency key can lose this race (its
            // CloudCustodyRecord insert collides with the winner's) without ever reaching the
            // idempotency-record-write step itself. Re-checking here, after the winner's
            // transaction has released the unique-index lock this insert collided on, lets this
            // loser replay the winner's committed result instead of reporting an unrelated-looking
            // domain Conflict (ARCH-006, transaction rules 4 and 8).
            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return await ReplayDepositAsync(winner, cancellationToken);
            }

            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Biota {biotaId} already has a Cloud Custody Record.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.Deposit, biotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.Deposit, biotaId, ownerId,
                custodyRecordId: record.Id, targetContainerId: null, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    private async Task<CloudBoundaryOutcome<CloudWithdrawalResult>> TryWithdrawOnceAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudWithdrawalResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ReplayWithdrawal(existing);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Deterministic single-row lock (transaction rules 2 and 7): SELECT ... FOR UPDATE on the
        // exact Cloud Custody Record this withdrawal targets.
        var record = await LockCustodyRecordAsync(custodyRecordId, cancellationToken);
        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            // A concurrent caller with the same idempotency key can lose this race: it blocks on
            // the winner's row lock and, once unblocked, observes the winner's delete instead of a
            // row to withdraw. Re-checking here lets this loser replay the winner's committed
            // result instead of reporting an unrelated-looking domain Conflict (ARCH-006,
            // transaction rules 4 and 8).
            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return ReplayWithdrawal(winner);
            }

            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Cloud Custody Record {custodyRecordId} does not exist or was already withdrawn.");
        }

        if (record.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Cloud Custody Record {custodyRecordId} is at version {record.Version}, not the expected version {expectedVersion}.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var biotaId = record.BiotaId;
        var shardId = record.ShardId;
        var ownerId = record.OwnerId!.Value; // non-stack: OwnerId is always set (CK_CloudCustodyRecord_OwnerXorStack).
        var correlationId = Guid.NewGuid();

        // Custody must be released before world possession is granted: the AddCloudCustodyRecords
        // migration's trg_biota_iid_reject_cloud_custodied_insert trigger refuses a Container grant
        // for a biota that still has a CloudCustodyRecord row. Deleting first, on the same
        // connection and transaction, means the trigger's own EXISTS check observes this
        // transaction's own uncommitted delete and lets the grant through.
        await ReleaseCustodyRecordAsync(record, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        await GrantContainerAsync(biotaId, recipientContainerId, cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.Withdrawal, biotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.Withdrawal, biotaId, ownerId,
                custodyRecordId, recipientContainerId, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudWithdrawalResult>.Committed(new CloudWithdrawalResult(biotaId, recipientContainerId, ownerId));
    }

    private async Task<CloudBoundaryOutcome<CloudStackDepositResult>> TryDepositStackOnceAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        int quantity,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudStackDepositResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayStackDepositAsync(existing, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        // See TryDepositOnceAsync's matching comment: removing world possession on this same
        // transaction/connection, immediately before the Cloud Custody Record insert, is what
        // makes the deposit atomic and closes the orphan window.
        await RemoveWorldPossessionAsync(biotaId, cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        var correlationId = Guid.NewGuid();
        var record = CloudCustodyRecord.CreateStack(biotaId, shardId, quantity, correlationId);
        var lot = new CloudStackLot(record.Id, shardId, ownerId, quantity);
        _context.CloudCustodyRecords.Add(record);
        _context.CloudStackLots.Add(lot);

        var frozenEnchantments = BuildFrozenEnchantments(record.Id, shardId, preservationRequirements);
        if (frozenEnchantments.Count > 0)
        {
            _context.CloudFrozenEnchantments.AddRange(frozenEnchantments);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return await ReplayStackDepositAsync(winner, cancellationToken);
            }

            return CloudBoundaryOutcome<CloudStackDepositResult>.Conflict(
                $"Biota {biotaId} already has a Cloud Custody Record.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        // The native persisted stack size should already match `quantity` (ACE observed it on the
        // live object before offering the Custodian sale); this upsert is a defensive no-op that
        // makes ace_shard's on-disk copy authoritative even if it had not been flushed yet.
        await UpsertStackSizeAsync(biotaId, quantity, cancellationToken);

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.StackDeposit, biotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.StackDeposit, biotaId, ownerId,
                custodyRecordId: record.Id, targetContainerId: null, correlationId, quantity));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudStackDepositResult>.Committed(new CloudStackDepositResult(record, lot));
    }

    private async Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>> TryWithdrawLotOnceAsync(
        Guid lotId,
        int expectedLotVersion,
        int quantityToWithdraw,
        uint recipientContainerId,
        uint? materializedBiotaId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudStackWithdrawalResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ReplayStackWithdrawal(existing);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == lotId)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);

        if (custodyRecordId is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            var winnerBeforeLot = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winnerBeforeLot is not null)
            {
                return ReplayStackWithdrawal(winnerBeforeLot);
            }

            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        // Deterministic lock order (transaction rule 2): the backing stack record before the lot.
        var record = await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
        var lot = await LockStackLotAsync(lotId, cancellationToken);

        if (record is null || lot is null || lot.CustodyRecordId != record.Id)
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return ReplayStackWithdrawal(winner);
            }

            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict($"Cloud Stack Lot {lotId} does not exist or was already withdrawn.");
        }

        if (lot.Version != expectedLotVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Cloud Stack Lot {lotId} is at version {lot.Version}, not the expected version {expectedLotVersion}.");
        }

        if (quantityToWithdraw > lot.Quantity)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Cannot withdraw {quantityToWithdraw} from Cloud Stack Lot {lotId}, which only has {lot.Quantity}.");
        }

        var siblingCount = await _context.CloudStackLots
            .CountAsync(l => l.CustodyRecordId == record.Id && l.Id != lot.Id, cancellationToken);
        var isFullStackWithdrawal = siblingCount == 0 && quantityToWithdraw == lot.Quantity;

        if (!isFullStackWithdrawal && (materializedBiotaId is null or 0))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                "A materialized child GUID (allocated by ACE) is required to withdraw part of a Cloud Stack Lot.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var originalBiotaId = record.BiotaId;
        var shardId = record.ShardId;
        var ownerId = lot.OwnerId;
        var correlationId = Guid.NewGuid();
        uint deliveredBiotaId;

        if (isFullStackWithdrawal)
        {
            _context.CloudStackLots.Remove(lot);
            await ReleaseCustodyRecordAsync(record, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

            await GrantContainerAsync(originalBiotaId, recipientContainerId, cancellationToken);
            await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

            deliveredBiotaId = originalBiotaId;
        }
        else
        {
            if (quantityToWithdraw == lot.Quantity)
            {
                _context.CloudStackLots.Remove(lot);
            }
            else
            {
                lot.ReduceQuantity(quantityToWithdraw);
                _context.CloudStackLots.Update(lot);
            }

            record.ReduceStackTotalQuantity(quantityToWithdraw);
            _context.CloudCustodyRecords.Update(record);
            await _context.SaveChangesAsync(cancellationToken);
            await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

            await MaterializeChildBiotaAsync(originalBiotaId, materializedBiotaId!.Value, quantityToWithdraw, cancellationToken);
            await UpsertStackSizeAsync(originalBiotaId, record.TotalQuantity!.Value, cancellationToken);
            await GrantContainerAsync(materializedBiotaId.Value, recipientContainerId, cancellationToken);
            await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

            deliveredBiotaId = materializedBiotaId.Value;

            _context.CloudStackLotLineageEvents.Add(
                new CloudStackLotLineageEvent(correlationId, shardId, originalBiotaId, materializedBiotaId.Value, quantityToWithdraw, ownerId));
            await _context.SaveChangesAsync(cancellationToken);
        }

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.StackWithdrawal, deliveredBiotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.StackWithdrawal, deliveredBiotaId, ownerId,
                custodyRecordId: record.Id, targetContainerId: recipientContainerId, correlationId, quantityToWithdraw));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Committed(
            new CloudStackWithdrawalResult(deliveredBiotaId, recipientContainerId, ownerId, quantityToWithdraw));
    }

    private async Task<CloudBoundaryOutcome<CloudPyrealConversionResult>> TryConvertPyrealDepositOnceAsync(
        uint rawBiotaId,
        string shardId,
        Guid ownerId,
        long rawPyrealAmount,
        IReadOnlyList<uint> mmdBiotaIds,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudPyrealConversionResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await _context.CloudPyrealConversionRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayPyrealConversionAsync(existing, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var remainder = await EnsureAndLockPyrealRemainderAsync(shardId, ownerId, cancellationToken);
        var remainderBefore = remainder.RemainderAmount;
        var conversion = PyrealConversionPolicy.Convert(remainderBefore, rawPyrealAmount);

        if (conversion.MmdCount != mmdBiotaIds.Count)
        {
            await transaction.RollbackAsync(cancellationToken);

            // The remainder can have moved since the caller's earlier, non-transactional
            // GetPyrealRemainderAsync read (a concurrent conversion for the same owner committed in
            // between) -- this is a real Conflict, not a bug, and no idempotency-key replay applies
            // because nothing committed under this key. The caller must re-read the remainder and
            // retry with a freshly allocated MMD count.
            return CloudBoundaryOutcome<CloudPyrealConversionResult>.Conflict(
                $"Expected {conversion.MmdCount} MMD biota(s) for this conversion (remainder {remainderBefore} + {rawPyrealAmount} raw Pyreals), but {mmdBiotaIds.Count} were supplied.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        // CONTEXT.md: "Raw Pyreal conversion is the only automatic consolidation or replacement of
        // deposited stackable assets" -- the raw coin stack's entire value is now exactly represented
        // by the new MMDs plus the updated remainder, so it never becomes a Cloud Custody Record
        // itself. Removing world possession first and deleting the now-fully-consumed biota on this
        // same connection/transaction mirrors DepositAsync's atomicity: a crash or rejected commit
        // can never leave it half-consumed.
        await RemoveWorldPossessionAsync(rawBiotaId, cancellationToken);
        await DeleteBiotaAsync(rawBiotaId, cancellationToken);

        var correlationId = Guid.NewGuid();
        var mmdCustodyRecords = new List<CloudCustodyRecord>(mmdBiotaIds.Count);

        foreach (var mmdBiotaId in mmdBiotaIds)
        {
            // Idempotent/no-op for a freshly materialized MMD biota that never had world possession
            // (RemoveWorldPossessionAsync's doc comment) -- kept so every biota entering custody goes
            // through the exact same path.
            await RemoveWorldPossessionAsync(mmdBiotaId, cancellationToken);

            var mmdRecord = new CloudCustodyRecord(mmdBiotaId, shardId, ownerId, correlationId);
            _context.CloudCustodyRecords.Add(mmdRecord);
            mmdCustodyRecords.Add(mmdRecord);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudPyrealConversionResult>.Conflict(
                "One of the supplied MMD biotas already has a Cloud Custody Record.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        // The conversion itself is anchored on the consumed raw biota; each MMD additionally gets
        // its own ordinary Deposit-typed ledger/outbox event (CloudBoundaryOperationType.PyrealConversion's
        // doc comment) so the companion web sees new MMDs exactly like any other deposited Cloud Item.
        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.PyrealConversion, rawBiotaId, ownerId, faultInjector, cancellationToken);
        foreach (var mmdRecord in mmdCustodyRecords)
        {
            await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.Deposit, mmdRecord.BiotaId, ownerId, faultInjector, cancellationToken);
        }

        remainder.Replace(conversion.NewRemainder);
        _context.CloudPyrealRemainders.Update(remainder);

        _context.CloudPyrealConversionMmds.AddRange(
            mmdCustodyRecords.Select(record => new CloudPyrealConversionMmd(idempotencyKey, record.BiotaId, record.Id)));

        _context.CloudPyrealConversionRecords.Add(
            new CloudPyrealConversionRecord(idempotencyKey, shardId, ownerId, rawBiotaId, rawPyrealAmount, remainderBefore, conversion.NewRemainder, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudPyrealConversionResult>.Committed(new CloudPyrealConversionResult(mmdCustodyRecords, conversion.NewRemainder));
    }

    private async Task<CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>> TryWithdrawPyrealRemainderOnceAsync(
        string shardId,
        Guid ownerId,
        long amount,
        IReadOnlyList<uint> deliveryBiotaIds,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudPyrealRemainderWithdrawalResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existing = await _context.CloudPyrealRemainderWithdrawalRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayPyrealRemainderWithdrawalAsync(existing, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var remainder = await EnsureAndLockPyrealRemainderAsync(shardId, ownerId, cancellationToken);
        var remainderBefore = remainder.RemainderAmount;

        // ADM-004 / transaction rule 9: the maintenance gate is revalidated at the exact instant the
        // remainder row is locked, not only earlier in the request pipeline. This boundary has no
        // live Global Cloud Maintenance aggregate to read yet (a later administration issue adds
        // one), so it is always Open today -- the same scope boundary WithdrawAsync already accepts.
        var decision = PyrealRemainderWithdrawalPolicy.Decide(remainderBefore, amount, CloudMutationGateState.Open);

        if (decision.Kind != PyrealRemainderWithdrawalDecisionKind.Approved)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>.Conflict(
                decision.Kind == PyrealRemainderWithdrawalDecisionKind.Frozen
                    ? "Cloud mutations are currently frozen for maintenance."
                    : $"Cannot withdraw {amount} from a Pyreal Remainder of only {decision.AvailableRemainder}.");
        }

        var deliveredSum = 0L;
        foreach (var deliveryBiotaId in deliveryBiotaIds)
        {
            deliveredSum += await ReadBiotaCoinValueAsync(deliveryBiotaId, cancellationToken);
        }

        if (deliveredSum != amount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>.Conflict(
                $"The supplied delivery biota(s) sum to {deliveredSum} Pyreals, not the requested {amount}.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var correlationId = Guid.NewGuid();

        foreach (var deliveryBiotaId in deliveryBiotaIds)
        {
            await GrantContainerAsync(deliveryBiotaId, recipientContainerId, cancellationToken);
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        remainder.Replace(decision.NewRemainder);
        _context.CloudPyrealRemainders.Update(remainder);

        await AppendLedgerAndOutboxAsync(
            correlationId, shardId, CloudBoundaryOperationType.PyrealRemainderWithdrawal, deliveryBiotaIds[0], ownerId, faultInjector, cancellationToken);

        _context.CloudPyrealRemainderWithdrawalRecords.Add(
            new CloudPyrealRemainderWithdrawalRecord(
                idempotencyKey, shardId, ownerId, amount, remainderBefore, decision.NewRemainder, recipientContainerId, correlationId));
        _context.CloudPyrealRemainderWithdrawalBiotas.AddRange(
            deliveryBiotaIds.Select(biotaId => new CloudPyrealRemainderWithdrawalBiota(idempotencyKey, biotaId)));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>.Committed(
            new CloudPyrealRemainderWithdrawalResult(deliveryBiotaIds, recipientContainerId, decision.NewRemainder));
    }

    /// <summary>
    /// Locks an account's Pyreal Remainder row for the whole boundary transaction, creating it with
    /// a zero balance first if this is the account's first ever conversion/withdrawal. The insert
    /// uses MariaDB's <c>INSERT ... ON DUPLICATE KEY UPDATE</c> upsert idiom specifically because it
    /// blocks on (rather than immediately erroring against) a concurrent transaction racing the same
    /// owner's first row, so a losing concurrent caller simply waits for the winner to commit or
    /// roll back and then locks the same row the winner created, instead of needing its own
    /// duplicate-key recovery branch.
    /// </summary>
    private async Task<CloudPyrealRemainder> EnsureAndLockPyrealRemainderAsync(string shardId, Guid ownerId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO CloudPyrealRemainder (OwnerId, ShardId, RemainderAmount, Version)
                VALUES (@ownerId, @shardId, 0, 1)
                ON DUPLICATE KEY UPDATE OwnerId = OwnerId;
                """;
            AddParameter(upsert, "@ownerId", ownerId.ToString());
            AddParameter(upsert, "@shardId", shardId);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        return await _context.CloudPyrealRemainders
            .FromSqlInterpolated($"SELECT * FROM CloudPyrealRemainder WHERE OwnerId = {ownerId} AND ShardId = {shardId} FOR UPDATE")
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Reads a coin-type biota's total Pyreal value (its <c>Value</c> property, PropertyInt 19),
    /// which ACE keeps equal to <c>StackUnitValue * StackSize</c> for a coin stack -- exactly what
    /// <see cref="TryWithdrawPyrealRemainderOnceAsync"/> revalidates the caller-supplied delivery
    /// biotas' summed value against.
    /// </summary>
    private async Task<long> ReadBiotaCoinValueAsync(uint biotaId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT value FROM ace_shard.biota_properties_int WHERE object_Id = @objectId AND type = 19;
            """;
        AddParameter(command, "@objectId", biotaId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0 : Convert.ToInt64(result);
    }

    /// <summary>
    /// Deletes a fully consumed biota outright (as opposed to <see cref="RemoveWorldPossessionAsync"/>,
    /// which only clears world possession). Only ever used for the raw Pyreal coin-stack biota a
    /// conversion consumes: its entire value has already been exactly re-materialized as MMDs plus
    /// the updated remainder, so nothing is lost by removing the now-empty original. Every
    /// ace_shard child property table cascades on biota deletion (ShardBase.sql), and the
    /// ProtectCloudCustodyBiotaFromDeletion trigger only blocks deleting a biota that still has a
    /// CloudCustodyRecord -- this biota never gets one.
    /// </summary>
    private async Task DeleteBiotaAsync(uint biotaId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DELETE FROM ace_shard.biota WHERE id = @id;";
        AddParameter(command, "@id", biotaId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudPyrealConversionResult>> ReplayPyrealConversionAsync(
        CloudPyrealConversionRecord existing, CancellationToken cancellationToken)
    {
        var mmds = await _context.CloudPyrealConversionMmds.AsNoTracking()
            .Where(m => m.ConversionIdempotencyKey == existing.IdempotencyKey)
            .ToListAsync(cancellationToken);

        var custodyRecordIds = mmds.Select(m => m.CustodyRecordId).ToList();
        var custodyRecords = await _context.CloudCustodyRecords.AsNoTracking()
            .Where(r => custodyRecordIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (custodyRecords.Count != mmds.Count)
        {
            // ARCH-006 commits the conversion record, its MMD rows, and every MMD custody record in
            // the same transaction, so a mismatch here means that invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed a Pyreal conversion whose MMD custody records no longer all exist.");
        }

        return CloudBoundaryOutcome<CloudPyrealConversionResult>.Committed(
            new CloudPyrealConversionResult(custodyRecords, existing.RemainderAfter));
    }

    private async Task<CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>> ReplayPyrealRemainderWithdrawalAsync(
        CloudPyrealRemainderWithdrawalRecord existing, CancellationToken cancellationToken)
    {
        var deliveredBiotaIds = await _context.CloudPyrealRemainderWithdrawalBiotas.AsNoTracking()
            .Where(b => b.WithdrawalIdempotencyKey == existing.IdempotencyKey)
            .Select(b => b.BiotaId)
            .ToListAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudPyrealRemainderWithdrawalResult>.Committed(
            new CloudPyrealRemainderWithdrawalResult(deliveredBiotaIds, existing.RecipientContainerId, existing.RemainderAfter));
    }

    private async Task AppendLedgerAndOutboxAsync(
        Guid correlationId,
        string shardId,
        CloudBoundaryOperationType operationType,
        uint biotaId,
        Guid ownerId,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.CloudActivityLedgerEvents.Add(
            new CloudActivityLedgerEvent(correlationId, shardId, operationType, biotaId, ownerId, CloudBoundaryOutcomeKind.Committed));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterLedgerAppend);

        var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
        _context.CloudCustodyOutboxEvents.Add(
            new CloudCustodyOutboxEvent(correlationId, shardId, operationType, biotaId, ownerId, sequenceNumber));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterOutboxAppend);
    }

    /// <summary>
    /// Locks <see cref="CloudCustodyOutboxSequence"/>'s single row and returns the next durable
    /// order position, incrementing it in the same call so no two callers -- even racing under this
    /// same open transaction's connection -- can ever be handed the same value (ARCH-007).
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
            AddParameter(update, "@nextValue", reserved + 1);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }

    /// <summary>
    /// Refuses the in-progress mutation when this boundary was constructed with an expected
    /// <see cref="CloudComponentVersions"/> and the deployed <see cref="CloudShardBinding"/>'s
    /// recorded versions do not match it exactly (OPS-002). Returns null when the mutation may
    /// proceed -- either no expected versions were supplied, or they match.
    /// </summary>
    private async Task<CloudBoundaryOutcome<T>?> CheckProtocolCompatibilityAsync<T>(CancellationToken cancellationToken)
    {
        if (_expectedVersions is null)
        {
            return null;
        }

        var binding = await _context.CloudShardBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (binding is null)
        {
            return CloudBoundaryOutcome<T>.Unavailable(
                "This deployment has no CloudShardBinding row; Operator Bootstrap has not completed.");
        }

        var actual = new CloudComponentVersions(binding.AceExtensionVersion, binding.SchemaVersion, binding.ContractProtocolVersion);
        var compatibility = CloudCompatibilityChecker.Evaluate(_expectedVersions, actual);

        return compatibility.IsCompatible
            ? null
            : CloudBoundaryOutcome<T>.Unavailable($"Cloud component version mismatch: {compatibility.Reason}");
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> ReplayDepositAsync(
        CloudIdempotencyRecord existing, CancellationToken cancellationToken)
    {
        if (existing.OperationType != CloudBoundaryOperationType.Deposit)
        {
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a Deposit.");
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == existing.CustodyRecordId, cancellationToken);

        if (record is null)
        {
            // ARCH-006 commits the idempotency record and the CloudCustodyRecord insert in the
            // same transaction, so a committed Deposit idempotency record whose custody row is
            // gone is not a normal conflict -- it means that invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed a deposit whose Cloud Custody Record no longer exists.");
        }

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    private static CloudBoundaryOutcome<CloudWithdrawalResult> ReplayWithdrawal(CloudIdempotencyRecord existing)
    {
        if (existing.OperationType != CloudBoundaryOperationType.Withdrawal)
        {
            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a Withdrawal.");
        }

        return CloudBoundaryOutcome<CloudWithdrawalResult>.Committed(
            new CloudWithdrawalResult(existing.BiotaId, existing.TargetContainerId!.Value, existing.OwnerId));
    }

    private async Task<CloudBoundaryOutcome<CloudStackDepositResult>> ReplayStackDepositAsync(
        CloudIdempotencyRecord existing, CancellationToken cancellationToken)
    {
        if (existing.OperationType != CloudBoundaryOperationType.StackDeposit)
        {
            return CloudBoundaryOutcome<CloudStackDepositResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a StackDeposit.");
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == existing.CustodyRecordId, cancellationToken);
        var lot = await _context.CloudStackLots.AsNoTracking()
            .SingleOrDefaultAsync(l => l.CustodyRecordId == existing.CustodyRecordId, cancellationToken);

        if (record is null || lot is null)
        {
            // ARCH-006 commits the idempotency record, the CloudCustodyRecord insert, and the
            // initial CloudStackLot insert in the same transaction, so a committed StackDeposit
            // idempotency record whose rows are gone is not a normal conflict -- it means that
            // invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed a stack deposit whose records no longer exist.");
        }

        return CloudBoundaryOutcome<CloudStackDepositResult>.Committed(new CloudStackDepositResult(record, lot));
    }

    private static CloudBoundaryOutcome<CloudStackWithdrawalResult> ReplayStackWithdrawal(CloudIdempotencyRecord existing)
    {
        if (existing.OperationType != CloudBoundaryOperationType.StackWithdrawal)
        {
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a StackWithdrawal.");
        }

        return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Committed(
            new CloudStackWithdrawalResult(existing.BiotaId, existing.TargetContainerId!.Value, existing.OwnerId, existing.Quantity!.Value));
    }

    private async Task<CloudIdempotencyRecord?> FindIdempotencyRecordAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        await _context.CloudIdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<CloudCustodyRecord?> LockCustodyRecordAsync(Guid custodyRecordId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE Id = {custodyRecordId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudStackLot?> LockStackLotAsync(Guid lotId, CancellationToken cancellationToken) =>
        await _context.CloudStackLots
            .FromSqlInterpolated($"SELECT * FROM CloudStackLot WHERE Id = {lotId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Materializes a native child biota under a caller-supplied (ACE-allocated) GUID, cloning the
    /// parent's weenie identity and giving it its own PropertyInt.StackSize row (ARCH-010, INV-003).
    /// This boundary never allocates <paramref name="newBiotaId"/> itself; it only ever writes the
    /// exact GUID it was given, so the only place a native GUID is actually allocated remains ACE's
    /// own GuidManager, called by the ACE-side caller of this API.
    /// </summary>
    private async Task MaterializeChildBiotaAsync(uint originalBiotaId, uint newBiotaId, int quantity, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        uint weenieClassId;
        int weenieType;
        uint populatedCollectionFlags;

        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT weenie_Class_Id, weenie_Type, populated_Collection_Flags
                FROM ace_shard.biota WHERE id = @id FOR UPDATE;
                """;
            var idParameter = read.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = originalBiotaId;
            read.Parameters.Add(idParameter);

            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new CloudCustodyConflictException(
                    $"Cannot materialize a Cloud Stack Lot child: backing biota {originalBiotaId} no longer exists in ace_shard.");
            }

            weenieClassId = Convert.ToUInt32(reader.GetValue(0));
            weenieType = Convert.ToInt32(reader.GetValue(1));
            populatedCollectionFlags = Convert.ToUInt32(reader.GetValue(2));
        }

        await using (var insertBiota = connection.CreateCommand())
        {
            insertBiota.Transaction = transaction;
            insertBiota.CommandText = """
                INSERT INTO ace_shard.biota (id, weenie_Class_Id, weenie_Type, populated_Collection_Flags)
                VALUES (@id, @weenieClassId, @weenieType, @populatedCollectionFlags);
                """;
            AddParameter(insertBiota, "@id", newBiotaId);
            AddParameter(insertBiota, "@weenieClassId", weenieClassId);
            AddParameter(insertBiota, "@weenieType", weenieType);
            AddParameter(insertBiota, "@populatedCollectionFlags", populatedCollectionFlags);
            await insertBiota.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertStackSizeAsync(newBiotaId, quantity, cancellationToken);
    }

    /// <summary>
    /// Writes or updates a biota's PropertyInt.StackSize (type 12) row to exactly
    /// <paramref name="quantity"/>.
    /// </summary>
    private async Task UpsertStackSizeAsync(uint biotaId, int quantity, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO ace_shard.biota_properties_int (object_Id, type, value)
            VALUES (@objectId, 12, @quantity)
            ON DUPLICATE KEY UPDATE value = @quantity;
            """;
        AddParameter(command, "@objectId", biotaId);
        AddParameter(command, "@quantity", quantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Clears any remaining world possession (Container, Wielder, or Location) for a biota entering
    /// Cloud custody, on the same connection/transaction as the Cloud Custody Record insert that
    /// follows it (mirroring <see cref="GrantContainerAsync"/>'s withdrawal-direction counterpart).
    /// Deleting rather than merely checking-and-rejecting is what makes deposit atomic: a single
    /// commit performs both the shard-side removal and the Cloud-side custody creation, or neither
    /// does (transaction rule 5). Idempotent/no-op if the caller had already removed possession.
    /// </summary>
    private async Task RemoveWorldPossessionAsync(uint biotaId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM ace_shard.biota_properties_i_i_d
                WHERE object_Id = @biotaId AND type IN (2, 3);
                """;
            AddParameter(command, "@biotaId", biotaId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM ace_shard.biota_properties_position
                WHERE object_Id = @biotaId AND position_Type = 1;
                """;
            AddParameter(command, "@biotaId", biotaId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task GrantContainerAsync(uint biotaId, uint recipientContainerId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO ace_shard.biota_properties_i_i_d (object_Id, type, value)
            VALUES (@biotaId, 2, @containerId);
            """;

        var biotaParameter = command.CreateParameter();
        biotaParameter.ParameterName = "@biotaId";
        biotaParameter.Value = biotaId;
        command.Parameters.Add(biotaParameter);

        var containerParameter = command.CreateParameter();
        containerParameter.ParameterName = "@containerId";
        containerParameter.Value = recipientContainerId;
        command.Parameters.Add(containerParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Resumes ACE heartbeat processing for every Frozen Enchantment preserved for a withdrawn
    /// biota (DEP-005: "resumes ACE heartbeat processing from the same remaining duration"), on the
    /// same connection/transaction as the rest of the withdrawal. ace_shard's own registry row is
    /// never touched while custody lasts (<see cref="RemoveWorldPossessionAsync"/>'s doc comment), so
    /// it can still disagree with the exact live value <see cref="CloudRuntimeEnchantmentSnapshot"/>
    /// captured at deposit time -- ACE's periodic autosave can persist a biota's enchantment registry
    /// on a different cadence than the in-memory countdown itself decreases
    /// (<c>Player.BuildRuntimeEnchantments</c>'s doc comment). Overwriting <c>start_Time</c> here with
    /// <c>RemainingDurationSeconds - duration</c> (the same "Duration + StartTime" arithmetic
    /// <c>EnchantmentManager.HeartBeat</c> and <c>HeartBeatEnchantmentsAndReturnExpired</c> use)
    /// reproduces the exact preserved remaining duration regardless of that lag, without extending or
    /// shortening it -- <c>duration</c> itself is left untouched because nothing ever changed it
    /// during custody. Runs once per withdrawal, immediately before the caller stages the matching
    /// <see cref="CloudFrozenEnchantment"/> rows for deletion in the same transaction, so a retried
    /// idempotency key can never re-apply it a second time.
    /// </summary>
    private async Task ResumeFrozenEnchantmentsAsync(
        uint biotaId, IReadOnlyList<CloudFrozenEnchantment> frozenEnchantments, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        foreach (var frozenEnchantment in frozenEnchantments)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ace_shard.biota_properties_enchantment_registry
                SET start_Time = @remainingDurationSeconds - duration
                WHERE object_Id = @biotaId AND spell_Id = @spellId AND layer_Id = @layerId;
                """;
            AddParameter(command, "@biotaId", biotaId);
            AddParameter(command, "@spellId", frozenEnchantment.SpellId);
            AddParameter(command, "@layerId", frozenEnchantment.LayerId);
            AddParameter(command, "@remainingDurationSeconds", frozenEnchantment.RemainingDurationSeconds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reduces a deposit's DEP-005 preservation requirements to the <see cref="CloudFrozenEnchantment"/>
    /// rows to persist alongside <paramref name="custodyRecordId"/>, added to the same
    /// SaveChangesAsync call as the Cloud Custody Record insert (transaction rule 5): an empty or
    /// null list produces no rows, matching an item with no active runtime enchantments.
    /// </summary>
    private static List<CloudFrozenEnchantment> BuildFrozenEnchantments(
        Guid custodyRecordId, string shardId, IReadOnlyList<CloudRuntimeEnchantmentSnapshot>? preservationRequirements)
    {
        if (preservationRequirements is null || preservationRequirements.Count == 0)
        {
            return [];
        }

        var frozen = new List<CloudFrozenEnchantment>(preservationRequirements.Count);
        foreach (var requirement in preservationRequirements)
        {
            frozen.Add(new CloudFrozenEnchantment(custodyRecordId, shardId, requirement.SpellId, requirement.RemainingDurationSeconds, requirement.LayerId));
        }

        return frozen;
    }

    /// <summary>
    /// Stages a Cloud Custody Record for removal, along with every row that references it and
    /// would otherwise block the delete or wrongly survive it (issue #13 review, findings 1 and 2):
    /// <list type="bullet">
    /// <item>Its <see cref="CloudFrozenEnchantment"/> rows: DEP-005's
    /// <c>FK_CloudFrozenEnchantment_CloudCustodyRecord_CustodyRecordId</c> is <c>ON DELETE
    /// RESTRICT</c>, so deleting the custody record without first deleting these would make every
    /// withdrawal of an item that had an active runtime enchantment at deposit time throw an
    /// unhandled foreign-key violation. Before they are deleted, <see cref="ResumeFrozenEnchantmentsAsync"/>
    /// writes each one's preserved remaining duration back into ace_shard so ACE resumes heartbeat
    /// processing from the exact frozen value (issue #15, DEP-005) rather than losing it.</item>
    /// <item>Any prior committed Deposit/StackDeposit <see cref="CloudIdempotencyRecord"/> for the
    /// same biota: <see cref="CloudOwnerIdentity.DepositIdempotencyKey"/> is deterministic in
    /// (shardId, biotaId) alone, so a future re-deposit of this same biota (legitimate once it is
    /// back in world possession) recomputes the exact same key. Leaving the old record in place
    /// would make that re-deposit replay a Cloud Custody Record that no longer exists instead of
    /// creating a new one.</item>
    /// </list>
    /// Staged on the tracked change set only; callers still issue the single SaveChangesAsync that
    /// actually deletes these rows in the same transaction as the rest of the withdrawal.
    /// </summary>
    private async Task ReleaseCustodyRecordAsync(CloudCustodyRecord record, CancellationToken cancellationToken)
    {
        var frozenEnchantments = await _context.CloudFrozenEnchantments.AsNoTracking()
            .Where(f => f.CustodyRecordId == record.Id)
            .ToListAsync(cancellationToken);
        if (frozenEnchantments.Count > 0)
        {
            await ResumeFrozenEnchantmentsAsync(record.BiotaId, frozenEnchantments, cancellationToken);
            _context.CloudFrozenEnchantments.RemoveRange(frozenEnchantments);
        }

        var priorDepositRecords = await _context.CloudIdempotencyRecords.AsNoTracking()
            .Where(r => r.BiotaId == record.BiotaId && r.ShardId == record.ShardId
                && (r.OperationType == CloudBoundaryOperationType.Deposit || r.OperationType == CloudBoundaryOperationType.StackDeposit))
            .ToListAsync(cancellationToken);
        if (priorDepositRecords.Count > 0)
        {
            _context.CloudIdempotencyRecords.RemoveRange(priorDepositRecords);
        }

        _context.CloudCustodyRecords.Remove(record);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };

    private static void RequireIdempotencyKey(Guid idempotencyKey)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A world-boundary handoff requires a non-empty idempotency key.", nameof(idempotencyKey));
        }
    }

    private static Task InvokeFaultInjectorAsync(Func<CloudBoundaryFaultPoint, Task>? faultInjector, CloudBoundaryFaultPoint point) =>
        faultInjector is null ? Task.CompletedTask : faultInjector(point);
}
