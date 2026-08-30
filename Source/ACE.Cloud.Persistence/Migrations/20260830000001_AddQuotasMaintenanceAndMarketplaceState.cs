namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #23's schema additions (INV-004, ADM-004, MKT-203, MKT-204):
///
///   - CloudStorageQuotaLimits: the single shard-wide personal/Allegiance Vault Storage Quota row.
///     Both limits null (unlimited) by default.
///
///   - CloudGlobalMaintenance: the single Global Cloud Maintenance state row (open/frozen, reason,
///     entered-at, entered-by).
///
///   - CloudGlobalMaintenanceLedgerEvent: the append-only audit trail for every maintenance
///     entry/exit (ADM-004's "ledger event" requirement).
///
///   - CloudMarketplaceConfiguration: the single Marketplace State row (Enabled by default).
/// </summary>
public sealed class AddQuotasMaintenanceAndMarketplaceState : CloudSchemaMigrationStep
{
    public AddQuotasMaintenanceAndMarketplaceState()
        : base("20260830000001_AddQuotasMaintenanceAndMarketplaceState")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudStorageQuotaLimits (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            PersonalLimit INT NULL,
            VaultLimit INT NULL,
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudStorageQuotaLimits_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudStorageQuotaLimits_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudGlobalMaintenance (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            IsFrozen TINYINT(1) NOT NULL DEFAULT 0,
            Reason VARCHAR(512) NULL,
            EnteredAtUtc DATETIME(6) NULL,
            EnteredByAccountId INT UNSIGNED NULL,
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudGlobalMaintenance_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudGlobalMaintenance_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudGlobalMaintenanceLedgerEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(16) NOT NULL,
            Reason VARCHAR(512) NULL,
            ActorAccountId INT UNSIGNED NULL,
            FrozenDurationSeconds BIGINT NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudGlobalMaintenanceLedgerEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudGlobalMaintenanceLedgerEvent_ShardId ON CloudGlobalMaintenanceLedgerEvent (ShardId);",
        """
        CREATE TABLE CloudMarketplaceConfiguration (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            State VARCHAR(24) NOT NULL DEFAULT 'Enabled',
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudMarketplaceConfiguration_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudMarketplaceConfiguration_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudMarketplaceConfiguration;",
        "DROP TABLE CloudGlobalMaintenanceLedgerEvent;",
        "DROP TABLE CloudGlobalMaintenance;",
        "DROP TABLE CloudStorageQuotaLimits;",
    ];
}
