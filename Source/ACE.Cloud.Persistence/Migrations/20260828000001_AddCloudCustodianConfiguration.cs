namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Adds versioned Custodian configuration persistence (DEP-007, DEP-008, ADM-003, issue #12):
/// whether the shared Marketplace and Mansion Custodian Location sets are each enabled, plus zero or
/// more administrator-added custom ACE positions. CloudCustodianConfiguration is a singleton row
/// (Id = 1), matching CloudShardBinding's and CloudCustodyOutboxSequence's established
/// one-row-per-deployment shape (ARCH-001); CloudCustodianCustomPosition is a child table so an
/// administrator can add or remove any number of custom positions without touching the singleton row
/// itself. Both carry a plain optimistic-concurrency Version -- the singleton row's Version is the
/// authoritative "configuration version" a Cloud Custodian's sell window is revalidated against at
/// commit (DEP-008: "A disabled Custodian must reject a stale open-window commit rather than accept
/// against old configuration").
/// </summary>
public sealed class AddCloudCustodianConfiguration : CloudSchemaMigrationStep
{
    public AddCloudCustodianConfiguration()
        : base("20260828000001_AddCloudCustodianConfiguration")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudCustodianConfiguration (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            MarketplaceEnabled TINYINT(1) NOT NULL DEFAULT 1,
            MansionsEnabled TINYINT(1) NOT NULL DEFAULT 1,
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudCustodianConfiguration_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudCustodianConfiguration_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudCustodianCustomPosition (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            PositionRaw VARCHAR(255) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudCustodianCustomPosition_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudCustodianCustomPosition_ShardId ON CloudCustodianCustomPosition (ShardId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudCustodianCustomPosition;",
        "DROP TABLE CloudCustodianConfiguration;",
    ];
}
