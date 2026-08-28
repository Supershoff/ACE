namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Adds Raw Pyreal Deposit conversion and Pyreal Remainder withdrawal persistence (DEP-006, issue
/// #14): an account-scoped Pyreal Remainder, a dedicated conversion idempotency record with its
/// created-MMD detail rows, and a dedicated remainder-withdrawal idempotency record with its
/// delivered-biota detail rows. Kept as separate tables from <see cref="CloudIdempotencyRecord"/>
/// because these two operations can create or deliver more than one native biota per request, which
/// that table's single-BiotaId shape has no room for.
/// </summary>
public sealed class AddCloudPyrealRemainder : CloudSchemaMigrationStep
{
    public AddCloudPyrealRemainder()
        : base("20260828000003_AddCloudPyrealRemainder")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudPyrealRemainder (
            OwnerId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            RemainderAmount BIGINT NOT NULL,
            Version INT NOT NULL DEFAULT 1,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (OwnerId, ShardId),
            CONSTRAINT CK_CloudPyrealRemainder_BelowThreshold CHECK (RemainderAmount >= 0 AND RemainderAmount < 287500),
            CONSTRAINT FK_CloudPyrealRemainder_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudPyrealConversionRecord (
            IdempotencyKey CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            RawBiotaId INT UNSIGNED NOT NULL,
            RawPyrealAmount BIGINT NOT NULL,
            RemainderBefore BIGINT NOT NULL,
            RemainderAfter BIGINT NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (IdempotencyKey),
            CONSTRAINT CK_CloudPyrealConversionRecord_PositiveAmount CHECK (RawPyrealAmount > 0),
            CONSTRAINT FK_CloudPyrealConversionRecord_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudPyrealConversionRecord_OwnerId ON CloudPyrealConversionRecord (OwnerId);",
        """
        CREATE TABLE CloudPyrealConversionMmd (
            Id CHAR(36) NOT NULL,
            ConversionIdempotencyKey CHAR(36) NOT NULL,
            MmdBiotaId INT UNSIGNED NOT NULL,
            CustodyRecordId CHAR(36) NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudPyrealConversionMmd_ConversionRecord
                FOREIGN KEY (ConversionIdempotencyKey) REFERENCES CloudPyrealConversionRecord (IdempotencyKey)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudPyrealConversionMmd_ConversionIdempotencyKey ON CloudPyrealConversionMmd (ConversionIdempotencyKey);",
        "CREATE UNIQUE INDEX UX_CloudPyrealConversionMmd_MmdBiotaId ON CloudPyrealConversionMmd (MmdBiotaId);",
        """
        CREATE TABLE CloudPyrealRemainderWithdrawalRecord (
            IdempotencyKey CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            Amount BIGINT NOT NULL,
            RemainderBefore BIGINT NOT NULL,
            RemainderAfter BIGINT NOT NULL,
            RecipientContainerId INT UNSIGNED NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (IdempotencyKey),
            CONSTRAINT CK_CloudPyrealRemainderWithdrawalRecord_PositiveAmount CHECK (Amount > 0),
            CONSTRAINT FK_CloudPyrealRemainderWithdrawalRecord_ShardBinding
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudPyrealRemainderWithdrawalRecord_OwnerId ON CloudPyrealRemainderWithdrawalRecord (OwnerId);",
        """
        CREATE TABLE CloudPyrealRemainderWithdrawalBiota (
            Id CHAR(36) NOT NULL,
            WithdrawalIdempotencyKey CHAR(36) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudPyrealRemainderWithdrawalBiota_WithdrawalRecord
                FOREIGN KEY (WithdrawalIdempotencyKey) REFERENCES CloudPyrealRemainderWithdrawalRecord (IdempotencyKey)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudPyrealRemainderWithdrawalBiota_WithdrawalIdempotencyKey ON CloudPyrealRemainderWithdrawalBiota (WithdrawalIdempotencyKey);",
        "CREATE UNIQUE INDEX UX_CloudPyrealRemainderWithdrawalBiota_BiotaId ON CloudPyrealRemainderWithdrawalBiota (BiotaId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudPyrealRemainderWithdrawalBiota;",
        "DROP TABLE CloudPyrealRemainderWithdrawalRecord;",
        "DROP TABLE CloudPyrealConversionMmd;",
        "DROP TABLE CloudPyrealConversionRecord;",
        "DROP TABLE CloudPyrealRemainder;",
    ];
}
