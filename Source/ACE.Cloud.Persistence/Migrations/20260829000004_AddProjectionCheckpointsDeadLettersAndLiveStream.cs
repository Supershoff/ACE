namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #22's schema additions (ARCH-007, ARCH-012, EVT-007, SRCH-001):
///
///   - CloudProjectionCheckpoint durably records each outbox projection consumer's resume position,
///     one row per consumer name (currently "CustodyProjection" and "IdentityProjection").
///
///   - CloudProjectionDeadLetter records a poison event a consumer could not apply, so it can skip
///     past it instead of blocking every later event; the projection tables below remain disposable
///     read models, never authoritative state.
///
///   - CloudInventoryReadProjection and CloudCharacterIdentityReadProjection are the rebuildable
///     read/search projections themselves, one row per native biota / per character, built
///     exclusively by replaying CloudCustodyOutboxEvent / CloudIdentityOutboxEvent rows.
///
///   - CloudLiveStreamSequence + CloudLiveStreamEvent give the Live State Stream the exact same
///     durable, strictly-ordered append log CloudCustodyOutboxSequence/CloudCustodyOutboxEvent
///     already give the Custody Outbox (see that table's migration for the identical locking
///     rationale); ScopeOwnerId is null for a public event and required for a private one.
/// </summary>
public sealed class AddProjectionCheckpointsDeadLettersAndLiveStream : CloudSchemaMigrationStep
{
    public AddProjectionCheckpointsDeadLettersAndLiveStream()
        : base("20260829000004_AddProjectionCheckpointsDeadLettersAndLiveStream")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudProjectionCheckpoint (
            ConsumerName VARCHAR(64) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            LastAppliedSequenceNumber BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (ConsumerName),
            CONSTRAINT FK_CloudProjectionCheckpoint_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudProjectionDeadLetter (
            Id CHAR(36) NOT NULL,
            ConsumerName VARCHAR(64) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SourceEventId CHAR(36) NOT NULL,
            SourceSequenceNumber BIGINT NOT NULL,
            Reason VARCHAR(512) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudProjectionDeadLetter_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudProjectionDeadLetter_Consumer_Shard ON CloudProjectionDeadLetter (ConsumerName, ShardId);",
        """
        CREATE TABLE CloudInventoryReadProjection (
            BiotaId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            LastEventType VARCHAR(32) NOT NULL,
            LastAppliedSequenceNumber BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (BiotaId),
            CONSTRAINT FK_CloudInventoryReadProjection_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudInventoryReadProjection_Shard_Owner ON CloudInventoryReadProjection (ShardId, OwnerId);",
        """
        CREATE TABLE CloudCharacterIdentityReadProjection (
            CharacterId INT UNSIGNED NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            AccountId INT UNSIGNED NULL,
            CharacterName VARCHAR(64) NULL,
            TotalLogins INT NULL,
            MonarchId INT UNSIGNED NULL,
            LastAppliedSequenceNumber BIGINT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (CharacterId),
            CONSTRAINT FK_CloudCharacterIdentityReadProjection_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudLiveStreamSequence (
            Id INT NOT NULL,
            NextValue BIGINT NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudLiveStreamSequence_Singleton CHECK (`Id` = 1)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "INSERT INTO CloudLiveStreamSequence (Id, NextValue) VALUES (1, 1);",
        """
        CREATE TABLE CloudLiveStreamEvent (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            SequenceNumber BIGINT NOT NULL,
            IsPublic TINYINT(1) NOT NULL,
            ScopeOwnerId CHAR(36) NULL,
            EventKind VARCHAR(32) NOT NULL,
            SourceEventId CHAR(36) NOT NULL,
            SourceSequenceNumber BIGINT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudLiveStreamEvent_SequenceNumber UNIQUE (SequenceNumber),
            CONSTRAINT FK_CloudLiveStreamEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudLiveStreamEvent_ScopeOwnerId ON CloudLiveStreamEvent (ScopeOwnerId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudLiveStreamEvent;",
        "DROP TABLE CloudLiveStreamSequence;",
        "DROP TABLE CloudCharacterIdentityReadProjection;",
        "DROP TABLE CloudInventoryReadProjection;",
        "DROP TABLE CloudProjectionDeadLetter;",
        "DROP TABLE CloudProjectionCheckpoint;",
    ];
}
