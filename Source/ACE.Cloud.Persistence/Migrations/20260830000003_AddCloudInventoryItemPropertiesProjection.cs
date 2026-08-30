namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #30's schema addition (UI-001, ARCH-012): the rebuildable read-model cache of one native
/// biota's category-relevant display properties (name, ItemType flags, WeenieType, denormalized
/// Inventory Category, value, burden, an icon reference). See
/// <see cref="CloudInventoryItemPropertiesProjection"/>'s doc comment for why this is a separate
/// table from CloudInventoryReadProjection/CloudCustodyRecord/CloudStackLot rather than adding columns
/// to any of them.
/// </summary>
public sealed class AddCloudInventoryItemPropertiesProjection : CloudSchemaMigrationStep
{
    public AddCloudInventoryItemPropertiesProjection()
        : base("20260830000003_AddCloudInventoryItemPropertiesProjection")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudInventoryItemPropertiesProjection (
            BiotaId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            Name VARCHAR(256) NOT NULL,
            ItemTypeFlags INT UNSIGNED NOT NULL,
            WeenieType INT NOT NULL,
            Category VARCHAR(32) NOT NULL,
            Value INT NULL,
            Burden INT NULL,
            IconCacheKeyHex VARCHAR(64) NULL,
            Revision BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (BiotaId),
            CONSTRAINT FK_CloudInventoryItemProperties_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudInventoryItemPropertiesProjection_Shard_Category ON CloudInventoryItemPropertiesProjection (ShardId, Category);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudInventoryItemPropertiesProjection;",
    ];
}
