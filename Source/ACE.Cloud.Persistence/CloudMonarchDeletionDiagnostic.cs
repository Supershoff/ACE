namespace ACE.Cloud.Persistence;

/// <summary>
/// Records that a nonempty Allegiance Vault's monarch character no longer exists in ace_shard even
/// though nothing routed through ACE's own guarded deletion path ever reported blocking it --
/// i.e. the character row was removed out-of-band (CONTEXT.md line 407: "An out-of-band monarch
/// deletion leaves the vault available only for audited administrator recovery"). This never
/// reassigns the vault to a guessed successor (VAULT-005: "do not guess a successor vault"); it only
/// surfaces the fact for an administrator to resolve. There is no update/delete path here other than
/// an administrator's own audited recovery workflow, which is out of this issue's scope.
/// </summary>
public sealed class CloudMonarchDeletionDiagnostic
{
    private CloudMonarchDeletionDiagnostic()
    {
    }

    public CloudMonarchDeletionDiagnostic(string shardId, uint monarchCharacterId, Guid vaultOwnerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A monarch deletion diagnostic requires a Cloud Shard ID.", nameof(shardId));
        }

        if (monarchCharacterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monarchCharacterId), "A monarch deletion diagnostic requires a real monarch character GUID.");
        }

        if (vaultOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A monarch deletion diagnostic requires the vault's owner ID.", nameof(vaultOwnerId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A monarch deletion diagnostic requires a reason.", nameof(reason));
        }

        Id = Guid.NewGuid();
        ShardId = shardId;
        MonarchCharacterId = monarchCharacterId;
        VaultOwnerId = vaultOwnerId;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint MonarchCharacterId { get; private set; }

    public Guid VaultOwnerId { get; private set; }

    public string Reason { get; private set; } = null!;

    public DateTime DetectedAtUtc { get; private set; }

    /// <summary>
    /// Whether an administrator has already recorded a recovery decision for this diagnostic
    /// (ADM-002, VAULT-005). Once true, this diagnostic's committed transfer can never be
    /// overridden by a later recovery attempt -- see <see cref="Resolve"/>.
    /// </summary>
    public bool IsResolved { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>The ACE account ID of the administrator who recorded this decision.</summary>
    public uint? ResolvedByAdminAccountId { get; private set; }

    /// <summary>The administrator's own written reason for this exact recovery decision (ADM-002).</summary>
    public string? ResolutionReason { get; private set; }

    /// <summary>
    /// The administrator-chosen destination owner the vault's contents were moved into. Always an
    /// explicit administrator decision, never a guessed successor (VAULT-005).
    /// </summary>
    public Guid? DestinationOwnerId { get; private set; }

    /// <summary>
    /// Records the administrator's recovery decision for this diagnostic (ADM-002: "requires a
    /// written reason and delayed confirmation"). Callers must check <see cref="IsResolved"/> under
    /// a row lock before calling this -- it throws rather than silently overwriting an earlier
    /// decision, since a committed recovery can never be overridden (VAULT-005).
    /// </summary>
    public void Resolve(uint adminAccountId, string reason, Guid destinationOwnerId)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException(
                $"Allegiance Vault recovery diagnostic {Id} was already resolved and cannot be overridden.");
        }

        if (adminAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adminAccountId), "Resolving a monarch deletion diagnostic requires a real administrator account ID.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Resolving a monarch deletion diagnostic requires a written reason.", nameof(reason));
        }

        if (destinationOwnerId == Guid.Empty || destinationOwnerId == VaultOwnerId)
        {
            throw new ArgumentException(
                "Resolving a monarch deletion diagnostic requires a real destination different from the orphaned vault itself.", nameof(destinationOwnerId));
        }

        IsResolved = true;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedByAdminAccountId = adminAccountId;
        ResolutionReason = reason;
        DestinationOwnerId = destinationOwnerId;
    }
}
