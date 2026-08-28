namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #19's schema additions (AUTH-002): <c>CloudAuthGrantConsumption</c> records that a signed
/// ACE Auth Bridge grant's nonce was already exchanged for a session (its unique constraint is the
/// actual one-use enforcement, since the Auth Bridge itself has no Cloud schema access at all to
/// track this -- ARCH-004); <c>CloudWebSession</c> is the Cloud backend's own authoritative session
/// record, storing only a one-way hash of the session cookie's secret, never the secret itself.
/// </summary>
public sealed class AddCloudWebSessionsAndGrantConsumption : CloudSchemaMigrationStep
{
    public AddCloudWebSessionsAndGrantConsumption()
        : base("20260828000007_AddCloudWebSessionsAndGrantConsumption")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudAuthGrantConsumption (
            Nonce CHAR(36) NOT NULL,
            AccountId INT UNSIGNED NOT NULL,
            ConsumedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Nonce)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudWebSession (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            AccountId INT UNSIGNED NOT NULL,
            SecretHash CHAR(64) NOT NULL,
            CsrfToken VARCHAR(64) NOT NULL,
            RotatedFromSessionId CHAR(36) NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            ExpiresAtUtc DATETIME(6) NOT NULL,
            LastSeenAtUtc DATETIME(6) NOT NULL,
            RevokedAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudWebSession_SecretHash UNIQUE (SecretHash),
            CONSTRAINT FK_CloudWebSession_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudWebSession_AccountId ON CloudWebSession (AccountId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudWebSession;",
        "DROP TABLE CloudAuthGrantConsumption;",
    ];
}
