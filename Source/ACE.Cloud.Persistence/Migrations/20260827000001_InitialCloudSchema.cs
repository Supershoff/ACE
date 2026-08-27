namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Baseline migration for issue #1's empty versioned Cloud schema: the singleton CloudShardBinding
/// table. This formalizes the shape <see cref="CloudDbContext"/> previously created via
/// EnsureCreatedAsync, so applying it is a pure formalization with no behavior change.
/// </summary>
public sealed class InitialCloudSchema : CloudSchemaMigrationStep
{
    public InitialCloudSchema()
        : base("20260827000001_InitialCloudSchema")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudShardBinding (
            Id INT NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SchemaVersion VARCHAR(32) NOT NULL,
            AceExtensionVersion VARCHAR(32) NOT NULL,
            ContractProtocolVersion VARCHAR(32) NOT NULL,
            AppliedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            -- ARCH-001: one Cloud Mule deployment serves exactly one immutable Cloud Shard ID,
            -- so this table may never hold more than its single Id = 1 row.
            CONSTRAINT CK_CloudShardBinding_Singleton CHECK (`Id` = 1)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE UNIQUE INDEX IX_CloudShardBinding_ShardId ON CloudShardBinding (ShardId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudShardBinding;",
    ];
}
