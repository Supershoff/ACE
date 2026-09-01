namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #36's schema additions (SHARE-001..004, AUTH-008, WDR-002): <c>CloudSharingGrant</c> is one
/// current row per (owner, grantee) pair keyed uniquely so "set" is an idempotent upsert;
/// <c>CloudSharingGrantLedgerEvent</c> is its admin-scoped audit trail (mirroring
/// <c>CloudAccountLinkLedgerEvent</c>'s own established shape). <c>CloudWithdrawalReservation</c>
/// gains two nullable columns so a grant-derived Withdrawal Token can bind redemption authority to
/// its grantee's group and exact grant provenance separately from the asset owner it still validates
/// every target against.
/// </summary>
public sealed class AddCloudSharingGrants : CloudSchemaMigrationStep
{
    public AddCloudSharingGrants()
        : base("20260901000001_AddCloudSharingGrants")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudSharingGrant (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            GranteeId CHAR(36) NOT NULL,
            Level VARCHAR(16) NOT NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            UpdatedAtUtc DATETIME(6) NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudSharingGrant_Owner_Grantee UNIQUE (ShardId, OwnerId, GranteeId),
            CONSTRAINT FK_SharingGrant_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudSharingGrant_Shard_Grantee ON CloudSharingGrant (ShardId, GranteeId);",
        """
        CREATE TABLE CloudSharingGrantLedgerEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(32) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            GranteeId CHAR(36) NOT NULL,
            Reason VARCHAR(512) NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_SharingGrantLedgerEvent_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudSharingGrantLedgerEvent_CorrelationId ON CloudSharingGrantLedgerEvent (CorrelationId);",
        "CREATE INDEX IX_CloudSharingGrantLedgerEvent_OwnerId ON CloudSharingGrantLedgerEvent (OwnerId);",
        "CREATE INDEX IX_CloudSharingGrantLedgerEvent_GranteeId ON CloudSharingGrantLedgerEvent (GranteeId);",
        "ALTER TABLE CloudWithdrawalReservation ADD COLUMN RedeemerOwnerId CHAR(36) NULL;",
        "ALTER TABLE CloudWithdrawalReservation ADD COLUMN SharingGrantId CHAR(36) NULL;",
        "CREATE INDEX IX_CloudWithdrawalReservation_RedeemerOwnerId ON CloudWithdrawalReservation (RedeemerOwnerId);",
        "CREATE INDEX IX_CloudWithdrawalReservation_SharingGrantId ON CloudWithdrawalReservation (SharingGrantId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP INDEX IX_CloudWithdrawalReservation_SharingGrantId ON CloudWithdrawalReservation;",
        "DROP INDEX IX_CloudWithdrawalReservation_RedeemerOwnerId ON CloudWithdrawalReservation;",
        "ALTER TABLE CloudWithdrawalReservation DROP COLUMN SharingGrantId;",
        "ALTER TABLE CloudWithdrawalReservation DROP COLUMN RedeemerOwnerId;",
        "DROP TABLE CloudSharingGrantLedgerEvent;",
        "DROP TABLE CloudSharingGrant;",
    ];
}
