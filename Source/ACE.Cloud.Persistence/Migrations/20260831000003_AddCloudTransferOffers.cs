namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #35's schema additions (XFER-001, XFER-002, INV-002, EVT-001, EVT-003):
/// <c>CloudTransferOffer</c> is both the offer and its own backing exclusive reservation in one row
/// (mirroring <c>CloudWithdrawalReservation</c>'s established combined shape); <c>CloudTransferOfferTarget</c>
/// holds one row per offered whole item or Cloud Stack Lot quantity claim.
/// </summary>
public sealed class AddCloudTransferOffers : CloudSchemaMigrationStep
{
    public AddCloudTransferOffers()
        : base("20260831000003_AddCloudTransferOffers")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudTransferOffer (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SenderAccountId CHAR(36) NOT NULL,
            RecipientAccountId CHAR(36) NOT NULL,
            CreateIdempotencyKey CHAR(36) NOT NULL,
            Status VARCHAR(16) NOT NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            ExpiresAtUtc DATETIME(6) NOT NULL,
            ResolvedAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudTransferOffer_CreateIdempotencyKey UNIQUE (CreateIdempotencyKey),
            CONSTRAINT FK_TransferOffer_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudTransferOffer_Shard_Sender_Status ON CloudTransferOffer (ShardId, SenderAccountId, Status);",
        "CREATE INDEX IX_CloudTransferOffer_Shard_Recipient_Status ON CloudTransferOffer (ShardId, RecipientAccountId, Status);",
        "CREATE INDEX IX_CloudTransferOffer_Shard_Status_Expires ON CloudTransferOffer (ShardId, Status, ExpiresAtUtc);",
        """
        CREATE TABLE CloudTransferOfferTarget (
            Id CHAR(36) NOT NULL,
            OfferId CHAR(36) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            ItemBiotaId INT UNSIGNED NULL,
            StackLotId CHAR(36) NULL,
            Quantity INT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_TransferOfferTarget_Offer
                FOREIGN KEY (OfferId) REFERENCES CloudTransferOffer (Id)
                ON DELETE CASCADE ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudTransferOfferTarget_OfferId ON CloudTransferOfferTarget (OfferId);",
        "CREATE INDEX IX_CloudTransferOfferTarget_ItemBiotaId ON CloudTransferOfferTarget (ItemBiotaId);",
        "CREATE INDEX IX_CloudTransferOfferTarget_StackLotId ON CloudTransferOfferTarget (StackLotId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudTransferOfferTarget;",
        "DROP TABLE CloudTransferOffer;",
    ];
}
