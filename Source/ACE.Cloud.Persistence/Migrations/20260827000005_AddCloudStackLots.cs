namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Introduces Cloud Stack Lots (ARCH-010, ARCH-011, INV-001, INV-003, issue #5,
/// docs/adr/0002-defer-native-materialization-for-partial-stacks.md): the join CloudCustodyRecord's
/// own doc comment already promised, letting a stackable biota's quantity transact off-world as one
/// or more exclusively owned CloudStackLot rows without ACE materializing a child biota/GUID until a
/// world-boundary withdrawal actually requires one.
///
/// CloudCustodyRecord.OwnerId becomes nullable and gains a sibling TotalQuantity column: a record is
/// exclusively non-stack (OwnerId set, TotalQuantity null) or a stack (TotalQuantity set, OwnerId
/// null), matching CONTEXT.md's Cloud Custody Record entry. CK_CloudCustodyRecord_OwnerXorStack
/// enforces that exclusivity (and TotalQuantity's positivity) as a single-row CHECK constraint --
/// no cross-table trigger is needed for this part, unlike AddCloudCustodyRecords' world-possession
/// guards.
///
/// CloudStackLot's own conservation invariant -- every lot's quantity sums to its backing record's
/// TotalQuantity, never more -- does need a cross-table check (a single row cannot see its
/// siblings), so it follows AddCloudCustodyRecords' established pattern: a BEFORE INSERT/UPDATE
/// trigger using SELECT ... FOR UPDATE against the backing CloudCustodyRecord row, so two concurrent
/// transactions racing to allocate quantity from the same stack cannot both read a stale
/// pre-allocation snapshot and jointly over-allocate. Exact equality (not just "does not exceed") is
/// preserved by application code: every operation that removes quantity from one lot adds it back
/// somewhere else (a new lot, a merged lot, or a reduced CloudCustodyRecord.TotalQuantity paired
/// with a materialized child) in the same transaction.
///
/// CloudStackLotLineageEvent logs each materialization's parent/child GUID and quantity (INV-003:
/// "logs complete lineage"), the same append-only shape as CloudActivityLedgerEvent.
/// CloudIdempotencyRecord gains a nullable Quantity column so a replayed StackDeposit/
/// StackWithdrawal can report the quantity it committed without re-deriving it.
/// </summary>
public sealed class AddCloudStackLots : CloudSchemaMigrationStep
{
    public AddCloudStackLots()
        : base("20260827000005_AddCloudStackLots")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        "ALTER TABLE CloudCustodyRecord MODIFY COLUMN OwnerId CHAR(36) NULL;",
        "ALTER TABLE CloudCustodyRecord ADD COLUMN TotalQuantity INT NULL AFTER OwnerId;",
        """
        ALTER TABLE CloudCustodyRecord ADD CONSTRAINT CK_CloudCustodyRecord_OwnerXorStack CHECK (
            (TotalQuantity IS NULL AND OwnerId IS NOT NULL)
            OR (TotalQuantity IS NOT NULL AND TotalQuantity > 0 AND OwnerId IS NULL)
        );
        """,
        """
        CREATE TABLE CloudStackLot (
            Id CHAR(36) NOT NULL,
            CustodyRecordId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            Quantity INT NOT NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudStackLot_PositiveQuantity CHECK (Quantity > 0),
            CONSTRAINT FK_CloudStackLot_CloudCustodyRecord_CustodyRecordId
                FOREIGN KEY (CustodyRecordId) REFERENCES CloudCustodyRecord (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_CloudStackLot_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudStackLot_CustodyRecordId ON CloudStackLot (CustodyRecordId);",
        """
        CREATE TRIGGER trg_cloud_stack_lot_conservation_insert
        BEFORE INSERT ON CloudStackLot
        FOR EACH ROW
        BEGIN
            DECLARE totalQty INT;
            DECLARE usedQty INT;

            SELECT TotalQuantity INTO totalQty FROM CloudCustodyRecord WHERE Id = NEW.CustodyRecordId FOR UPDATE;

            IF totalQty IS NULL THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Cloud Stack Lot references a Cloud Custody Record that is not a stack.';
            END IF;

            SELECT COALESCE(SUM(Quantity), 0) INTO usedQty FROM CloudStackLot WHERE CustodyRecordId = NEW.CustodyRecordId;

            IF usedQty + NEW.Quantity > totalQty THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Cloud Stack Lot quantities would exceed the backing stack total quantity.';
            END IF;
        END
        """,
        """
        CREATE TRIGGER trg_cloud_stack_lot_conservation_update
        BEFORE UPDATE ON CloudStackLot
        FOR EACH ROW
        BEGIN
            DECLARE totalQty INT;
            DECLARE usedQty INT;

            IF NEW.Quantity <> OLD.Quantity THEN
                SELECT TotalQuantity INTO totalQty FROM CloudCustodyRecord WHERE Id = NEW.CustodyRecordId FOR UPDATE;
                SELECT COALESCE(SUM(Quantity), 0) INTO usedQty
                    FROM CloudStackLot WHERE CustodyRecordId = NEW.CustodyRecordId AND Id <> NEW.Id;

                IF usedQty + NEW.Quantity > totalQty THEN
                    SIGNAL SQLSTATE '45000'
                        SET MESSAGE_TEXT = 'Cloud Stack Lot quantities would exceed the backing stack total quantity.';
                END IF;
            END IF;
        END
        """,
        """
        CREATE TABLE CloudStackLotLineageEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            ParentBiotaId INT UNSIGNED NOT NULL,
            ChildBiotaId INT UNSIGNED NOT NULL,
            Quantity INT NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudStackLotLineageEvent_PositiveQuantity CHECK (Quantity > 0),
            CONSTRAINT FK_CloudStackLotLineageEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudStackLotLineageEvent_CorrelationId ON CloudStackLotLineageEvent (CorrelationId);",
        "ALTER TABLE CloudIdempotencyRecord ADD COLUMN Quantity INT NULL AFTER TargetContainerId;",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "ALTER TABLE CloudIdempotencyRecord DROP COLUMN Quantity;",
        // DROP TABLE removes its indexes with it; a separate DROP INDEX first fails because the
        // FK constraint on CustodyRecordId/ShardId depends on that index while the table still
        // exists.
        "DROP TABLE CloudStackLotLineageEvent;",
        "DROP TRIGGER IF EXISTS trg_cloud_stack_lot_conservation_update;",
        "DROP TRIGGER IF EXISTS trg_cloud_stack_lot_conservation_insert;",
        "DROP TABLE CloudStackLot;",
        "ALTER TABLE CloudCustodyRecord DROP CONSTRAINT CK_CloudCustodyRecord_OwnerXorStack;",
        "ALTER TABLE CloudCustodyRecord DROP COLUMN TotalQuantity;",
        "ALTER TABLE CloudCustodyRecord MODIFY COLUMN OwnerId CHAR(36) NOT NULL;",
    ];
}
