namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Introduces the first-class Cloud Custody Record (ARCH-005, INV-001) and the database-level
/// exclusivity guards required by AGENTS.md's custody authority rules and IMPLEMENTATION-BRIEF's
/// ARCH-004/ARCH-005: a biota can never be simultaneously world-possessed and Cloud-custodied.
///
/// MariaDB limitation this migration works around: CHECK constraints cannot reference other
/// tables or contain subqueries, so the cross-table/cross-schema invariants below (native-biota
/// existence, and exclusivity against ace_shard's Container/Wielder/Location properties) cannot be
/// expressed as CHECK constraints. They are implemented as BEFORE INSERT/UPDATE triggers instead
/// (still a database-level constraint, just a heavier mechanism than CHECK/FOREIGN KEY), guarding
/// both directions of the boundary:
///   - ace_cloud.CloudCustodyRecord refuses a row for a biota that does not exist, or that
///     currently has world possession, in ace_shard.
///   - ace_shard.biota_properties_i_i_d (Container/Wielder) and biota_properties_position
///     (Location) refuse a row for a biota that currently has a CloudCustodyRecord.
/// Every cross-schema EXISTS subquery above uses FOR UPDATE: two overlapping, uncommitted
/// transactions racing opposite directions of the boundary (one granting world possession, one
/// depositing Cloud custody, for the same biota) must not both read an empty result from a stale
/// snapshot. FOR UPDATE forces the second transaction's check to block on the first transaction's
/// row/gap lock until it commits or rolls back, so the check that runs second always observes the
/// first transaction's outcome instead of racing past it.
/// Triggers run with definer privileges, so the narrowly privileged Cloud web identity (ARCH-004)
/// never needs its own read/write grant on ace_shard to be protected by the ace_cloud-side guard.
/// <see cref="CloudCustodyBoundary"/> adds a complementary application-level, commit-time
/// revalidation layer (ARCH-006) so a misconfigured/missing trigger cannot silently admit a
/// conflicting deposit, and so callers see a typed domain exception instead of a raw MySqlException.
///
/// A second MariaDB limitation affects this migrator directly: DDL statements (CREATE TABLE,
/// CREATE TRIGGER, ...) cause an implicit commit and are not transactional, so
/// <see cref="CloudSchemaMigrator"/> cannot wrap a migration's statements in a rollback-safe
/// transaction the way ordinary DML can be. Each statement here is written to be independently
/// safe to apply once; recovering from a partial failure is an operator concern (fix the schema
/// state, then retry), the same as with any other MariaDB DDL migration tool.
/// </summary>
public sealed class AddCloudCustodyRecords : CloudSchemaMigrationStep
{
    private const string ContainerPropertyType = "2"; // PropertyInstanceId.Container
    private const string WielderPropertyType = "3"; // PropertyInstanceId.Wielder
    private const string LocationPositionType = "1"; // PositionType.Location

    public AddCloudCustodyRecords()
        : base("20260827000002_AddCloudCustodyRecords")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudCustodyRecord (
            Id CHAR(36) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            LedgerCorrelationId CHAR(36) NOT NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            -- INV-001: exactly one custody record per native biota (duplicate custody rejection).
            CONSTRAINT AK_CloudCustodyRecord_BiotaId UNIQUE (BiotaId),
            CONSTRAINT FK_CloudCustodyRecord_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudCustodyRecord_ShardId ON CloudCustodyRecord (ShardId);",
        $"""
        CREATE TRIGGER trg_cloud_custody_record_biota_boundary
        BEFORE INSERT ON CloudCustodyRecord
        FOR EACH ROW
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM ace_shard.biota WHERE id = NEW.BiotaId FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Cloud Custody Record references a native biota that does not exist in ace_shard.';
            END IF;

            IF EXISTS (
                SELECT 1 FROM ace_shard.biota_properties_i_i_d
                WHERE object_Id = NEW.BiotaId AND type IN ({ContainerPropertyType}, {WielderPropertyType})
                FOR UPDATE
            ) OR EXISTS (
                SELECT 1 FROM ace_shard.biota_properties_position
                WHERE object_Id = NEW.BiotaId AND position_Type = {LocationPositionType}
                FOR UPDATE
            ) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota currently has world possession (Container, Wielder, or Location) and cannot enter Cloud custody.';
            END IF;
        END
        """,
        $"""
        CREATE TRIGGER ace_shard.trg_biota_iid_reject_cloud_custodied_insert
        BEFORE INSERT ON ace_shard.biota_properties_i_i_d
        FOR EACH ROW
        BEGIN
            IF NEW.type IN ({ContainerPropertyType}, {WielderPropertyType})
                AND EXISTS (SELECT 1 FROM ace_cloud.CloudCustodyRecord WHERE BiotaId = NEW.object_Id FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota is in Cloud custody and cannot receive world possession (Container/Wielder).';
            END IF;
        END
        """,
        $"""
        CREATE TRIGGER ace_shard.trg_biota_iid_reject_cloud_custodied_update
        BEFORE UPDATE ON ace_shard.biota_properties_i_i_d
        FOR EACH ROW
        BEGIN
            IF NEW.type IN ({ContainerPropertyType}, {WielderPropertyType})
                AND EXISTS (SELECT 1 FROM ace_cloud.CloudCustodyRecord WHERE BiotaId = NEW.object_Id FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota is in Cloud custody and cannot receive world possession (Container/Wielder).';
            END IF;
        END
        """,
        $"""
        CREATE TRIGGER ace_shard.trg_biota_position_reject_cloud_custodied_insert
        BEFORE INSERT ON ace_shard.biota_properties_position
        FOR EACH ROW
        BEGIN
            IF NEW.position_Type = {LocationPositionType}
                AND EXISTS (SELECT 1 FROM ace_cloud.CloudCustodyRecord WHERE BiotaId = NEW.object_Id FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota is in Cloud custody and cannot receive world possession (Location).';
            END IF;
        END
        """,
        $"""
        CREATE TRIGGER ace_shard.trg_biota_position_reject_cloud_custodied_update
        BEFORE UPDATE ON ace_shard.biota_properties_position
        FOR EACH ROW
        BEGIN
            IF NEW.position_Type = {LocationPositionType}
                AND EXISTS (SELECT 1 FROM ace_cloud.CloudCustodyRecord WHERE BiotaId = NEW.object_Id FOR UPDATE) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Biota is in Cloud custody and cannot receive world possession (Location).';
            END IF;
        END
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TRIGGER IF EXISTS ace_shard.trg_biota_position_reject_cloud_custodied_update;",
        "DROP TRIGGER IF EXISTS ace_shard.trg_biota_position_reject_cloud_custodied_insert;",
        "DROP TRIGGER IF EXISTS ace_shard.trg_biota_iid_reject_cloud_custodied_update;",
        "DROP TRIGGER IF EXISTS ace_shard.trg_biota_iid_reject_cloud_custodied_insert;",
        "DROP TRIGGER IF EXISTS trg_cloud_custody_record_biota_boundary;",
        "DROP TABLE CloudCustodyRecord;",
    ];
}
