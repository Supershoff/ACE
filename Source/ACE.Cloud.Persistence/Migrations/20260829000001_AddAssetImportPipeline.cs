namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #25's schema additions (ASSET-001..004, ADM-001, EVT-001): <c>CloudAssetImportSession</c> is
/// one resumable chunked upload; <c>CloudAssetImportChunkRecord</c> makes a resend of an already
/// -received chunk detectable; <c>CloudAssetImportCurrentSessionMarker</c> is the actual "at most one
/// in-flight import per shard/kind" enforcement, the same role
/// <c>CloudActiveAccountLinkMarker</c> plays for account linking; <c>CloudAssetManifest</c> and
/// <c>CloudAssetManifestEntryRecord</c> are one immutable, versioned, DID-addressable extraction
/// result; <c>CloudActiveAssetManifest</c> is the atomically swapped active-manifest pointer;
/// <c>CloudRetainedSourceAsset</c> is the single latest checksum-verified source DAT retained per
/// shard/kind for reprocessing; <c>CloudAssetImportLedgerEvent</c> audits every import/staging
/// /activation outcome. <c>CloudAssetImportSession.ManifestId</c> is deliberately not a foreign key
/// (unlike every other reference here): <c>CloudAssetManifest.SourceImportSessionId</c> already
/// references the session in the other direction, and a table cannot hold two enforced FKs pointing
/// at each other without one side momentarily violating its own constraint at insert time.
/// </summary>
public sealed class AddAssetImportPipeline : CloudSchemaMigrationStep
{
    public AddAssetImportPipeline()
        : base("20260829000001_AddAssetImportPipeline")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudAssetImportSession (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            TotalBytes BIGINT NOT NULL,
            ChunkSizeBytes INT NOT NULL,
            ChunkCount INT NOT NULL,
            ExpectedChecksumHex CHAR(64) NOT NULL,
            InitiatedByAccountId INT UNSIGNED NOT NULL,
            State VARCHAR(24) NOT NULL,
            ReceivedChunkCount INT NOT NULL,
            ManifestId CHAR(36) NULL,
            ErrorMessage VARCHAR(1024) NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_AssetImportSession_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudAssetImportSession_Shard_Kind_State ON CloudAssetImportSession (ShardId, Kind, State);",
        """
        CREATE TABLE CloudAssetImportChunkRecord (
            SessionId CHAR(36) NOT NULL,
            ChunkIndex INT NOT NULL,
            Sha256Hex CHAR(64) NOT NULL,
            ByteLength BIGINT NOT NULL,
            ReceivedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (SessionId, ChunkIndex),
            CONSTRAINT FK_AssetImportChunk_Session
                FOREIGN KEY (SessionId) REFERENCES CloudAssetImportSession (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudAssetImportCurrentSessionMarker (
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            SessionId CHAR(36) NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (ShardId, Kind),
            CONSTRAINT FK_AssetImportCurrentMarker_Session
                FOREIGN KEY (SessionId) REFERENCES CloudAssetImportSession (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_AssetImportCurrentMarker_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudAssetImportCurrentSessionMarker_SessionId ON CloudAssetImportCurrentSessionMarker (SessionId);",
        """
        CREATE TABLE CloudAssetManifest (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            Version INT NOT NULL,
            State VARCHAR(16) NOT NULL,
            SourceImportSessionId CHAR(36) NOT NULL,
            EntryCount INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            ActivatedAtUtc DATETIME(6) NULL,
            SupersededAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudAssetManifest_Shard_Kind_Version UNIQUE (ShardId, Kind, Version),
            CONSTRAINT FK_AssetManifest_Session
                FOREIGN KEY (SourceImportSessionId) REFERENCES CloudAssetImportSession (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_AssetManifest_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudAssetManifestEntryRecord (
            ManifestId CHAR(36) NOT NULL,
            Did INT UNSIGNED NOT NULL,
            FileKind VARCHAR(16) NOT NULL,
            RelativePath VARCHAR(255) NOT NULL,
            ByteLength BIGINT NOT NULL,
            Sha256Hex CHAR(64) NOT NULL,
            PRIMARY KEY (ManifestId, Did, FileKind),
            CONSTRAINT FK_AssetManifestEntry_Manifest
                FOREIGN KEY (ManifestId) REFERENCES CloudAssetManifest (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudActiveAssetManifest (
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            ManifestId CHAR(36) NOT NULL,
            ManifestVersion INT NOT NULL,
            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (ShardId, Kind),
            CONSTRAINT FK_ActiveAssetManifest_Manifest
                FOREIGN KEY (ManifestId) REFERENCES CloudAssetManifest (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_ActiveAssetManifest_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudRetainedSourceAsset (
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            RelativePath VARCHAR(255) NOT NULL,
            ByteLength BIGINT NOT NULL,
            Sha256Hex CHAR(64) NOT NULL,
            SourceImportSessionId CHAR(36) NOT NULL,
            RetainedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            PRIMARY KEY (ShardId, Kind),
            CONSTRAINT FK_RetainedSourceAsset_Session
                FOREIGN KEY (SourceImportSessionId) REFERENCES CloudAssetImportSession (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_RetainedSourceAsset_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudAssetImportLedgerEvent (
            Id CHAR(36) NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            EventType VARCHAR(32) NOT NULL,
            SessionId CHAR(36) NULL,
            ManifestId CHAR(36) NULL,
            ManifestVersion INT NULL,
            AdminAccountId INT UNSIGNED NOT NULL,
            Reason VARCHAR(512) NULL,
            OccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT FK_AssetImportLedgerEvent_Session
                FOREIGN KEY (SessionId) REFERENCES CloudAssetImportSession (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_AssetImportLedgerEvent_Manifest
                FOREIGN KEY (ManifestId) REFERENCES CloudAssetManifest (Id)
                ON DELETE RESTRICT ON UPDATE RESTRICT,
            CONSTRAINT FK_AssetImportLedgerEvent_Shard
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudAssetImportLedgerEvent_CorrelationId ON CloudAssetImportLedgerEvent (CorrelationId);",
        "CREATE INDEX IX_CloudAssetImportLedgerEvent_Shard_Kind ON CloudAssetImportLedgerEvent (ShardId, Kind);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudAssetImportLedgerEvent;",
        "DROP TABLE CloudRetainedSourceAsset;",
        "DROP TABLE CloudActiveAssetManifest;",
        "DROP TABLE CloudAssetManifestEntryRecord;",
        "DROP TABLE CloudAssetManifest;",
        "DROP TABLE CloudAssetImportCurrentSessionMarker;",
        "DROP TABLE CloudAssetImportChunkRecord;",
        "DROP TABLE CloudAssetImportSession;",
    ];
}
