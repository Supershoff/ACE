namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// [P1 correction to issue #23's Storage Quota gate] Adds a per-owner lock row so a Storage Quota
/// count and the deposit it gates can be serialized under one <c>FOR UPDATE</c> lock (transaction
/// rule 9's "deterministic locks"), closing a race where two concurrent deposits for the same owner
/// both observe a projected count one below the configured limit and both commit. The row carries no
/// data of its own -- only its primary key exists to be locked, exactly like the per-owner
/// <c>CloudPyrealRemainder</c> row <see cref="ACE.Cloud.Persistence.CloudCustodyBoundary"/> already
/// upserts-then-locks for Pyreal conversions/withdrawals.
/// </summary>
public sealed class AddCloudStorageQuotaOwnerLock : CloudSchemaMigrationStep
{
    public AddCloudStorageQuotaOwnerLock()
        : base("20260830000002_AddCloudStorageQuotaOwnerLock")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudStorageQuotaOwnerLock (
            OwnerId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            PRIMARY KEY (OwnerId, ShardId),
            CONSTRAINT FK_CloudStorageQuotaOwnerLock_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudStorageQuotaOwnerLock;",
    ];
}
