namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Introduces the crash-safe, idempotent world-boundary handoff protocol proved by issue #4
/// (ARCH-002, ARCH-006, ARCH-007, transaction rules 1-10):
///   - CloudIdempotencyRecord: proves a handoff attempt already committed, so a repeated request
///     with the same key replays that result instead of reapplying the ownership change.
///   - CloudActivityLedgerEvent: the append-only Activity Ledger entry for a committed handoff
///     (EVT-001, EVT-002).
///   - CloudCustodyOutboxEvent: the durable Custody Outbox notification-intent entry for a
///     committed handoff (ARCH-007).
/// <see cref="CloudCustodyBoundary"/> writes all three of these plus the CloudCustodyRecord
/// mutation in one MariaDB transaction, so they are always consistent with each other -- none of
/// them is ever missing or duplicated relative to the state change it describes.
///
/// CloudIdempotencyRecord.CustodyRecordId intentionally has no foreign key: a withdrawal deletes
/// its CloudCustodyRecord row in the same transaction that writes the idempotency record
/// referencing it, so the referenced row legitimately does not exist afterward. The other two
/// tables' ShardId foreign keys follow the same cross-shard-rejection pattern the AddCloudCustodyRecords
/// migration already established for CloudCustodyRecord.
/// </summary>
public sealed class AddIdempotencyAndLedgerOutbox : CloudSchemaMigrationStep
{
    public AddIdempotencyAndLedgerOutbox()
        : base("20260827000004_AddIdempotencyAndLedgerOutbox")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudIdempotencyRecord (
            IdempotencyKey CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OperationType VARCHAR(32) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            CustodyRecordId CHAR(36) NULL,
            TargetContainerId INT UNSIGNED NULL,
            CorrelationId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (IdempotencyKey),
            CONSTRAINT FK_CloudIdempotencyRecord_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudIdempotencyRecord_ShardId ON CloudIdempotencyRecord (ShardId);",
        """
        CREATE TABLE CloudActivityLedgerEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(32) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            Outcome VARCHAR(16) NOT NULL,
            Reason VARCHAR(512) NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudActivityLedgerEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudActivityLedgerEvent_CorrelationId ON CloudActivityLedgerEvent (CorrelationId);",
        "CREATE INDEX IX_CloudActivityLedgerEvent_BiotaId ON CloudActivityLedgerEvent (BiotaId);",
        """
        CREATE TABLE CloudCustodyOutboxEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(32) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudCustodyOutboxEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudCustodyOutboxEvent_CorrelationId ON CloudCustodyOutboxEvent (CorrelationId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudCustodyOutboxEvent;",
        "DROP TABLE CloudActivityLedgerEvent;",
        "DROP TABLE CloudIdempotencyRecord;",
    ];
}
