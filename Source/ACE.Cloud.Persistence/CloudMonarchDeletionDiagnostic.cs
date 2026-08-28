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
}
