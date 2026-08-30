namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #32's schema addition (SRCH-001): the single administrator-controlled Safe Regex Search
/// toggle for this deployment. Matches <see cref="AddQuotasMaintenanceAndMarketplaceState"/>'s
/// singleton admin-config table shape (fixed <c>Id = 1</c>, a <c>ShardId</c> foreign key, and an
/// optimistic-concurrency <c>Version</c>) rather than adding a column to any existing table, exactly
/// like <see cref="CloudSearchConfigurationRecord"/>'s doc comment explains for
/// <see cref="CloudMarketplaceConfigurationRecord"/>.
/// </summary>
public sealed class AddCloudSearchConfiguration : CloudSchemaMigrationStep
{
    public AddCloudSearchConfiguration()
        : base("20260830000004_AddCloudSearchConfiguration")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudSearchConfiguration (
            Id TINYINT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            RegexSearchEnabled TINYINT(1) NOT NULL DEFAULT 1,
            Version INT NOT NULL DEFAULT 1,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudSearchConfiguration_Singleton CHECK (Id = 1),
            CONSTRAINT FK_CloudSearchConfiguration_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudSearchConfiguration;",
    ];
}
