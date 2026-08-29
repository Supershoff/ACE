namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #122's correction to issues #11/#16's Withdrawal Reservation shape: replaces the two
/// independent per-target-type tables (CloudWithdrawalReservation, whole-item only, and
/// CloudStackLotWithdrawalReservation) -- each with its own <c>TokenHash</c> unique constraint,
/// which let the same token secret address two different, independently consumable reservations at
/// once -- with one parent CloudWithdrawalReservation aggregate whose single <c>TokenHash</c>
/// uniqueness spans every target kind, plus a CloudWithdrawalReservationTarget child table carrying
/// one row per locked whole Cloud Item or Cloud Stack Lot quantity claim in the mixed selection.
///
/// This is a pre-release schema still under active phase development (no migration in this project
/// has yet shipped against real operator data), so this step alters the existing table in place and
/// drops the retired one outright rather than writing a data-preserving transform.
/// </summary>
public sealed class UnifyWithdrawalReservationTargets : CloudSchemaMigrationStep
{
    public UnifyWithdrawalReservationTargets()
        : base("20260829000001_UnifyWithdrawalReservationTargets")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        "DROP TABLE CloudStackLotWithdrawalReservation;",
        "DROP INDEX IX_CloudWithdrawalReservation_BiotaId ON CloudWithdrawalReservation;",
        "ALTER TABLE CloudWithdrawalReservation DROP COLUMN BiotaId;",
        """
        CREATE TABLE CloudWithdrawalReservationTarget (
            Id CHAR(36) NOT NULL,
            ReservationId CHAR(36) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            ItemBiotaId INT UNSIGNED NULL,
            StackLotId CHAR(36) NULL,
            Quantity INT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudWithdrawalReservationTarget_ReservationId
                FOREIGN KEY (ReservationId) REFERENCES CloudWithdrawalReservation (Id)
                ON DELETE CASCADE ON UPDATE RESTRICT,
            CONSTRAINT CK_CloudWithdrawalReservationTarget_KindShape CHECK (
                (Kind = 'Item' AND ItemBiotaId IS NOT NULL AND StackLotId IS NULL AND Quantity IS NULL) OR
                (Kind = 'StackLot' AND StackLotId IS NOT NULL AND ItemBiotaId IS NULL AND Quantity IS NOT NULL AND Quantity > 0)
            )
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudWithdrawalReservationTarget_ReservationId ON CloudWithdrawalReservationTarget (ReservationId);",
        "CREATE INDEX IX_CloudWithdrawalReservationTarget_ItemBiotaId ON CloudWithdrawalReservationTarget (ItemBiotaId);",
        "CREATE INDEX IX_CloudWithdrawalReservationTarget_StackLotId ON CloudWithdrawalReservationTarget (StackLotId);",
        """
        CREATE TABLE CloudWithdrawalRedemptionDeliveryItem (
            Id CHAR(36) NOT NULL,
            RedemptionIdempotencyKey CHAR(36) NOT NULL,
            OrdinalPosition INT NOT NULL,
            DeliveredBiotaId INT UNSIGNED NOT NULL,
            Quantity INT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudWithdrawalRedemptionDeliveryItem_Key_Ordinal UNIQUE (RedemptionIdempotencyKey, OrdinalPosition),
            CONSTRAINT FK_CloudWithdrawalRedemptionDeliveryItem_IdempotencyKey
                FOREIGN KEY (RedemptionIdempotencyKey) REFERENCES CloudIdempotencyRecord (IdempotencyKey)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudWithdrawalRedemptionDeliveryItem_IdempotencyKey ON CloudWithdrawalRedemptionDeliveryItem (RedemptionIdempotencyKey);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudWithdrawalRedemptionDeliveryItem;",
        "DROP TABLE CloudWithdrawalReservationTarget;",
        "ALTER TABLE CloudWithdrawalReservation ADD COLUMN BiotaId INT UNSIGNED NOT NULL AFTER ShardId;",
        "CREATE INDEX IX_CloudWithdrawalReservation_BiotaId ON CloudWithdrawalReservation (BiotaId);",
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
    ];
}
