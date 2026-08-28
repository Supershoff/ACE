namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #16's two schema additions completing local Withdrawal Token redemption (WDR-001..WDR-008,
/// INV-002, INV-003):
///
///   - CloudStackLotWithdrawalReservation is CloudWithdrawalReservation's Cloud Stack Lot analog
///     (docs/adr/0002-defer-native-materialization-for-partial-stacks.md, INV-002, INV-003): a
///     Withdrawal Token whose selection reserves a quantity claim against a stackable biota rather
///     than a whole item. It mirrors CloudWithdrawalReservation's shape exactly (TokenHash unique,
///     OpenIdempotencyKey unique, Status/ReleaseReason/Version/expiry), so both share the same
///     redemption-side crash-safety guarantees.
///
///   - CloudWithdrawalLocationConfiguration/CloudWithdrawalNamedLandblock persist the
///     administrator-managed Withdrawal Landblock allowlist and shard-wide `withdraw anywhere`
///     bypass (WDR-006, ADM-003), the same versioned singleton-plus-child-rows shape
///     AddCloudCustodianConfiguration already established for Custodian locations. Marketplace and
///     housing/SlumLord landblocks are not stored here: WDR-006 makes them always-allowed defaults
///     ACE.Server resolves directly from live world content, so only the administrator-named
///     landblocks and the anywhere bypass need persistence.
/// </summary>
public sealed class AddWithdrawalRedemptionSupport : CloudSchemaMigrationStep
{
    public AddWithdrawalRedemptionSupport()
        : base("20260828000005_AddWithdrawalRedemptionSupport")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudStackLotWithdrawalReservation (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            LotId CHAR(36) NOT NULL,
            Quantity INT NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            TokenHash CHAR(64) NOT NULL,
            OpenIdempotencyKey CHAR(36) NOT NULL,
            Status VARCHAR(16) NOT NULL,
            ReleaseReason VARCHAR(32) NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            ExpiresAtUtc DATETIME(6) NOT NULL,
            ReleasedAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudStackLotWithdrawalReservation_TokenHash UNIQUE (TokenHash),
            CONSTRAINT UQ_CloudStackLotWithdrawalReservation_OpenIdempotencyKey UNIQUE (OpenIdempotencyKey),
            CONSTRAINT FK_CloudStackLotWithdrawalReservation_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudStackLotWithdrawalReservation_LotId ON CloudStackLotWithdrawalReservation (LotId);",
        """
        CREATE TABLE CloudWithdrawalLocationConfiguration (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            WithdrawAnywhereEnabled TINYINT(1) NOT NULL DEFAULT 0,
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudWithdrawalLocationConfiguration_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudWithdrawalLocationConfig_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudWithdrawalNamedLandblock (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            Landblock SMALLINT UNSIGNED NOT NULL,
            Name VARCHAR(128) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudWithdrawalNamedLandblock_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudWithdrawalNamedLandblock_ShardId ON CloudWithdrawalNamedLandblock (ShardId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudWithdrawalNamedLandblock;",
        "DROP TABLE CloudWithdrawalLocationConfiguration;",
        "DROP TABLE CloudStackLotWithdrawalReservation;",
    ];
}
