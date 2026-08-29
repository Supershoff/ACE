namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #26's schema addition (UI-006): <c>CloudIconDiagnostic</c> is one deduplicated,
/// administrator-visible Icon Reconstruction failure per (ShardId, DedupeKey), upserted by
/// <see cref="ACE.Cloud.Persistence.CloudIconDiagnosticGateway"/> so a repeatedly broken reference
/// grows one row's OccurrenceCount instead of producing one new row per render attempt.
/// </summary>
public sealed class AddCloudIconDiagnostics : CloudSchemaMigrationStep
{
    public AddCloudIconDiagnostics()
        : base("20260829000002_AddCloudIconDiagnostics")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudIconDiagnostic (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            DedupeKey VARCHAR(64) NOT NULL,
            LayerKind VARCHAR(24) NOT NULL,
            Did INT UNSIGNED NOT NULL,
            Reason VARCHAR(16) NOT NULL,
            OccurrenceCount INT NOT NULL DEFAULT 1,
            FirstSeenAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            LastSeenAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudIconDiagnostic_Shard_DedupeKey UNIQUE (ShardId, DedupeKey),
            CONSTRAINT FK_CloudIconDiagnostic_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudIconDiagnostic;",
    ];
}
