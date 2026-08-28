namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #20's schema additions (AUTH-001, AUTH-003, AUTH-005..009): <c>CloudOwnershipGroup</c> and
/// <c>CloudAccountLink</c> are the Main/Linked Account aggregates; <c>CloudActiveAccountLinkMarker</c>
/// is the actual "at most one active link per account" enforcement (a filtered/partial unique index
/// on <c>CloudAccountLink</c> itself is not expressible in MariaDB); <c>CloudAccountLinkIdempotencyRecord</c>
/// and <c>CloudAccountLinkLedgerEvent</c> give link/unlink the same idempotent-replay and audit shape
/// every other Cloud boundary operation already has; <c>CloudDisplayCharacterSelection</c> and
/// <c>CloudDisplayCharacterSelectionHistoryEvent</c> hold AUTH-003's current pointer and immutable
/// history. Constraint names here are kept short deliberately: MySQL/MariaDB rejects any identifier
/// longer than 64 characters, which the fully descriptive `FK_&lt;Table&gt;_&lt;ReferencedTable&gt;_&lt;Column&gt;`
/// pattern used elsewhere in this schema would exceed for some of these longer table names.
/// </summary>
public sealed class AddAccountLinkingAndDisplayCharacter : CloudSchemaMigrationStep
{
    public AddAccountLinkingAndDisplayCharacter()
        : base("20260828000008_AddAccountLinkingAndDisplayCharacter")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudOwnershipGroup (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            MainAccountId INT UNSIGNED NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudOwnershipGroup_Shard_Main UNIQUE (ShardId, MainAccountId),
            CONSTRAINT FK_OwnershipGroup_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudAccountLink (
            Id CHAR(36) NOT NULL,
            OwnershipGroupId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            LinkedAccountId INT UNSIGNED NOT NULL,
            Status VARCHAR(16) NOT NULL,
            LinkedAtUtc DATETIME(6) NOT NULL,
            UnlinkedAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_AccountLink_OwnershipGroup
                FOREIGN KEY (OwnershipGroupId) REFERENCES CloudOwnershipGroup (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_AccountLink_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudAccountLink_OwnershipGroupId ON CloudAccountLink (OwnershipGroupId);",
        "CREATE INDEX IX_CloudAccountLink_Shard_LinkedAccount ON CloudAccountLink (ShardId, LinkedAccountId);",
        """
        CREATE TABLE CloudActiveAccountLinkMarker (
            ShardId VARCHAR(64) NOT NULL,
            AccountId INT UNSIGNED NOT NULL,
            AccountLinkId CHAR(36) NOT NULL,
            OwnershipGroupId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (ShardId, AccountId),
            CONSTRAINT FK_ActiveLinkMarker_AccountLink
                FOREIGN KEY (AccountLinkId) REFERENCES CloudAccountLink (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_ActiveLinkMarker_OwnershipGroup
                FOREIGN KEY (OwnershipGroupId) REFERENCES CloudOwnershipGroup (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_ActiveLinkMarker_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudActiveAccountLinkMarker_OwnershipGroupId ON CloudActiveAccountLinkMarker (OwnershipGroupId);",
        """
        CREATE TABLE CloudAccountLinkIdempotencyRecord (
            IdempotencyKey CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OperationType VARCHAR(16) NOT NULL,
            MainAccountId INT UNSIGNED NOT NULL,
            SourceAccountId INT UNSIGNED NOT NULL,
            IsApproved TINYINT(1) NOT NULL,
            RejectionCode VARCHAR(32) NOT NULL,
            AccountLinkId CHAR(36) NULL,
            OwnershipGroupId CHAR(36) NULL,
            CorrelationId CHAR(36) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (IdempotencyKey),
            CONSTRAINT FK_AccountLinkIdempotency_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudAccountLinkLedgerEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(16) NOT NULL,
            MainAccountId INT UNSIGNED NOT NULL,
            SourceAccountId INT UNSIGNED NOT NULL,
            Reason VARCHAR(512) NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_AccountLinkLedgerEvent_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudAccountLinkLedgerEvent_CorrelationId ON CloudAccountLinkLedgerEvent (CorrelationId);",
        "CREATE INDEX IX_CloudAccountLinkLedgerEvent_MainAccountId ON CloudAccountLinkLedgerEvent (MainAccountId);",
        """
        CREATE TABLE CloudDisplayCharacterSelection (
            OwnershipGroupId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            CharacterId INT UNSIGNED NULL,
            CharacterName VARCHAR(64) NULL,
            TotalLogins INT NULL,
            Version INT NOT NULL,
            SelectedAtUtc DATETIME(6) NOT NULL,
            PRIMARY KEY (OwnershipGroupId),
            CONSTRAINT FK_DisplayCharSelection_OwnershipGroup
                FOREIGN KEY (OwnershipGroupId) REFERENCES CloudOwnershipGroup (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_DisplayCharSelection_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudDisplayCharacterSelectionHistoryEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnershipGroupId CHAR(36) NOT NULL,
            Reason VARCHAR(24) NOT NULL,
            CharacterId INT UNSIGNED NULL,
            CharacterName VARCHAR(64) NULL,
            TotalLogins INT NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_DisplayCharHistory_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudDisplayCharacterSelectionHistoryEvent_CorrelationId ON CloudDisplayCharacterSelectionHistoryEvent (CorrelationId);",
        "CREATE INDEX IX_CloudDisplayCharacterSelectionHistoryEvent_OwnershipGroupId ON CloudDisplayCharacterSelectionHistoryEvent (OwnershipGroupId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudDisplayCharacterSelectionHistoryEvent;",
        "DROP TABLE CloudDisplayCharacterSelection;",
        "DROP TABLE CloudAccountLinkLedgerEvent;",
        "DROP TABLE CloudAccountLinkIdempotencyRecord;",
        "DROP TABLE CloudActiveAccountLinkMarker;",
        "DROP TABLE CloudAccountLink;",
        "DROP TABLE CloudOwnershipGroup;",
    ];
}
