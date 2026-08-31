using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only Activity Ledger entry for an Asset Import outcome (EVT-001: "import... appear in
/// the Activity Ledger"; EVT-002). Written in the same database transaction as the state change it
/// records (transaction rule 5). Kept as its own dedicated table for the same reason
/// <see cref="CloudAccountLinkLedgerEvent"/> is separate from the biota-shaped
/// <see cref="CloudActivityLedgerEvent"/>: an Asset Import has no native biota or Cloud Custody
/// Record identity of its own.
///
/// <see cref="CloudAssetImportLedgerEventType.ManifestActivated"/>'s <see cref="ManifestId"/>/
/// <see cref="ManifestVersion"/> double as the cache-invalidation intent ASSET-002's Green section
/// asks for ("invalidate derived caches by manifest version"): a downstream read model can watch
/// this table for that event type instead of a separate outbox, exactly as new activations are the
/// only occurrences that ever need to invalidate anything.
/// </summary>
public sealed class CloudAssetImportLedgerEvent
{
    private CloudAssetImportLedgerEvent()
    {
    }

    public CloudAssetImportLedgerEvent(
        Guid correlationId,
        string shardId,
        CloudAssetKind kind,
        CloudAssetImportLedgerEventType eventType,
        Guid? sessionId,
        Guid? manifestId,
        int? manifestVersion,
        uint adminAccountId,
        string? reason)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An Asset Import ledger event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Asset Import ledger event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (adminAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adminAccountId), "An Asset Import ledger event requires the administrator's account ID.");
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        Kind = kind;
        EventType = eventType;
        SessionId = sessionId;
        ManifestId = manifestId;
        ManifestVersion = manifestVersion;
        AdminAccountId = adminAccountId;
        Reason = reason;
        OccurredAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public CloudAssetImportLedgerEventType EventType { get; private set; }

    public Guid? SessionId { get; private set; }

    public Guid? ManifestId { get; private set; }

    public int? ManifestVersion { get; private set; }

    public uint AdminAccountId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
