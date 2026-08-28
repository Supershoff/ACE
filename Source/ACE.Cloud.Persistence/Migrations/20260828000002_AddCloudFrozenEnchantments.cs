namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Adds Frozen Enchantment persistence (DEP-005, issue #13): the preserved remaining duration of an
/// accepted runtime (temporary) enchantment, captured at deposit time and tied to the
/// <see cref="CloudCustodyRecord"/> that took the biota into Cloud custody. A stack deposit's
/// initial Cloud Custody Record is the only one created at deposit time (its <see cref="CloudStackLot"/>
/// rows do not themselves carry item-level enchantment state), so CustodyRecordId is always the
/// stack's own backing record for a stack deposit, exactly as for a non-stack deposit.
/// </summary>
public sealed class AddCloudFrozenEnchantments : CloudSchemaMigrationStep
{
    public AddCloudFrozenEnchantments()
        : base("20260828000002_AddCloudFrozenEnchantments")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudFrozenEnchantment (
            Id CHAR(36) NOT NULL,
            CustodyRecordId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SpellId INT NOT NULL,
            RemainingDurationSeconds DOUBLE NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudFrozenEnchantment_NonNegativeDuration CHECK (RemainingDurationSeconds >= 0),
            CONSTRAINT FK_CloudFrozenEnchantment_CloudCustodyRecord_CustodyRecordId
                FOREIGN KEY (CustodyRecordId) REFERENCES CloudCustodyRecord (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_CloudFrozenEnchantment_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudFrozenEnchantment_CustodyRecordId ON CloudFrozenEnchantment (CustodyRecordId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudFrozenEnchantment;",
    ];
}
