namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Closes a gap left by <see cref="AddCloudCustodyRecords"/> (issue #2): that migration's triggers
/// stop ace_shard from granting a Cloud-custodied biota new Container/Wielder/Location world
/// possession, but nothing stopped ace_shard from deleting the biota row outright. ACE's own
/// orphan-cleanup pass (<c>ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel</c>) classifies
/// any biota with no Container, Wielder, or Location as garbage today -- which is exactly the shape
/// of a valid Cloud Custody Record's native biota (ARCH-005). Without this guard, that cleanup pass
/// (invoked at every ACE.Server startup when Offline.PurgeOrphanedBiotas is enabled) would delete a
/// valid Cloud Item's native biota, permanently losing its GUID lineage (ARCH-010/ARCH-011) and
/// freeing the GUID for reuse by GuidManager's next allocation.
///
/// A BEFORE DELETE trigger is the correct mechanism for the same MariaDB reason
/// <see cref="AddCloudCustodyRecords"/> already documents: CHECK constraints cannot express a
/// cross-schema lookup. The trigger uses the same FOR UPDATE locking pattern as the existing
/// custody-boundary triggers so a concurrent, uncommitted Cloud custody deposit cannot race a
/// concurrent delete attempt for the same biota.
/// </summary>
public sealed class ProtectCloudCustodyBiotaFromDeletion : CloudSchemaMigrationStep
{
    public ProtectCloudCustodyBiotaFromDeletion()
        : base("20260827000003_ProtectCloudCustodyBiotaFromDeletion")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TRIGGER ace_shard.trg_biota_reject_delete_when_cloud_custodied
        BEFORE DELETE ON ace_shard.biota
        FOR EACH ROW
        BEGIN
            IF EXISTS (SELECT 1 FROM ace_cloud.CloudCustodyRecord WHERE BiotaId = OLD.id FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota is in Cloud custody and cannot be deleted from ace_shard while custodied.';
            END IF;
        END
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TRIGGER IF EXISTS ace_shard.trg_biota_reject_delete_when_cloud_custodied;",
    ];
}
