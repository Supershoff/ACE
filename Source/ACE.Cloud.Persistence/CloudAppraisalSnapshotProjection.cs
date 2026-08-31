using System.Text.Json;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The rebuildable read-model cache of one native biota's complete Full Cloud Appraisal snapshot
/// (issue #34 human-acceptance correction: "Capture the complete rebuildable, player-facing
/// appraisal snapshot ... at the ACE world boundary while the live WorldObject exists"). Stores the
/// entire <see cref="CloudAppraisalRawItemSnapshot"/> -- including its <c>WieldRequirements</c>/
/// <c>Spells</c> collections and <c>ArmorProfile</c>/<c>WeaponProfile</c> sub-records -- as a single
/// JSON payload rather than a fully normalized set of tables: this row is explicitly disposable and
/// rebuildable from ACE's own biota properties at any time (the same rationale
/// <see cref="CloudInventoryItemPropertiesProjection"/>'s doc comment already gives), so a schema
/// this variable-shaped does not need, and would not benefit from, relational normalization the way
/// custody-authoritative state does.
///
/// Populating this row from a live ACE <c>WorldObject</c> is the responsibility of ACE's own
/// world-boundary code (<c>Player_CloudCustodian</c>), exactly like
/// <see cref="CloudInventoryItemPropertiesProjection"/>.
/// </summary>
public sealed class CloudAppraisalSnapshotProjection
{
    private CloudAppraisalSnapshotProjection()
    {
    }

    private CloudAppraisalSnapshotProjection(uint biotaId, string shardId)
    {
        BiotaId = biotaId;
        ShardId = shardId;
    }

    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public string SnapshotJson { get; private set; } = null!;

    /// <summary>Caller-supplied monotonic write guard (see this type's doc comment); 0 means never written.</summary>
    public long Revision { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private static readonly JsonSerializerOptions SerializerOptions = new();

    /// <summary>
    /// Applies one appraisal snapshot to a (possibly brand-new) row, following the same idempotent,
    /// order-tolerant rule <see cref="CloudProjectionSequenceGuard"/> already gives outbox-sourced
    /// projections.
    /// </summary>
    public static (CloudAppraisalSnapshotProjection Row, bool Applied) TryApply(
        CloudAppraisalSnapshotProjection? current,
        uint biotaId,
        string shardId,
        CloudAppraisalRawItemSnapshot snapshot,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An appraisal snapshot row requires a Cloud Shard ID.", nameof(shardId));
        }

        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "An appraisal snapshot row requires a positive revision.");
        }

        var row = current ?? new CloudAppraisalSnapshotProjection(biotaId, shardId);

        if (!CloudProjectionSequenceGuard.ShouldApply(current?.Revision, revision))
        {
            return (row, Applied: false);
        }

        row.SnapshotJson = JsonSerializer.Serialize(snapshot, SerializerOptions);
        row.Revision = revision;
        row.UpdatedAtUtc = DateTime.UtcNow;
        return (row, Applied: true);
    }

    /// <summary>Deserializes the stored snapshot, or null if this row has never been written.</summary>
    public CloudAppraisalRawItemSnapshot? ToSnapshot() =>
        string.IsNullOrEmpty(SnapshotJson) ? null : JsonSerializer.Deserialize<CloudAppraisalRawItemSnapshot>(SnapshotJson, SerializerOptions);
}
