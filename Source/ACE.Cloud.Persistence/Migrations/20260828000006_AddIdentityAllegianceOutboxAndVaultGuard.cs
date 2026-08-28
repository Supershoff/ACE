namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #17's schema additions (AUTH-003, VAULT-001, VAULT-004, VAULT-005, ARCH-007):
///
///   - CloudIdentityOutboxSequence + CloudIdentityOutboxEvent give character/allegiance changes the
///     same durable, strictly-ordered outbox CloudCustodyOutboxSequence/CloudCustodyOutboxEvent
///     already give custody handoffs (see those tables' migration for the identical locking
///     rationale), kept as a separate table because these events have no native biota/custody owner.
///
///   - CloudAllegianceVaultBinding is the reverse lookup from an Allegiance Vault's opaque,
///     one-way-hashed owner identity (ACE.Cloud.Domain.CloudOwnerIdentity.ForAllegianceVault) back to
///     its monarch character, created lazily the first time a vault identity is actually used.
///     Without it, nothing could enumerate "every known vault" to check whether its monarch still
///     exists.
///
///   - CloudMonarchDeletionDiagnostic records a nonempty vault whose monarch no longer exists in
///     ace_shard despite never having been blocked by ACE's own guarded deletion path -- i.e. an
///     out-of-band deletion (VAULT-005). It never reassigns the vault to a guessed successor; it only
///     surfaces the fact for audited administrator recovery.
/// </summary>
public sealed class AddIdentityAllegianceOutboxAndVaultGuard : CloudSchemaMigrationStep
{
    public AddIdentityAllegianceOutboxAndVaultGuard()
        : base("20260828000006_AddIdentityAllegianceOutboxAndVaultGuard")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudIdentityOutboxSequence (
            Id INT NOT NULL,
            NextValue BIGINT NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudIdentityOutboxSequence_Singleton CHECK (`Id` = 1)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "INSERT INTO CloudIdentityOutboxSequence (Id, NextValue) VALUES (1, 1);",
        """
        CREATE TABLE CloudIdentityOutboxEvent (
            Id CHAR(36) NOT NULL,
            SequenceNumber BIGINT NOT NULL,
            CorrelationId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            EventType VARCHAR(32) NOT NULL,
            CharacterId INT UNSIGNED NOT NULL,
            AccountId INT UNSIGNED NULL,
            CharacterName VARCHAR(64) NULL,
            TotalLogins INT NULL,
            MonarchId INT UNSIGNED NULL,
            PriorMonarchId INT UNSIGNED NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudIdentityOutboxEvent_SequenceNumber UNIQUE (SequenceNumber),
            CONSTRAINT FK_CloudIdentityOutboxEvent_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudIdentityOutboxEvent_CorrelationId ON CloudIdentityOutboxEvent (CorrelationId);",
        "CREATE INDEX IX_CloudIdentityOutboxEvent_CharacterId ON CloudIdentityOutboxEvent (CharacterId);",
        """
        CREATE TABLE CloudAllegianceVaultBinding (
            OwnerId CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            MonarchCharacterId INT UNSIGNED NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (OwnerId),
            CONSTRAINT UQ_CloudAllegianceVaultBinding_Shard_Monarch UNIQUE (ShardId, MonarchCharacterId),
            CONSTRAINT FK_CloudAllegianceVaultBinding_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        """
        CREATE TABLE CloudMonarchDeletionDiagnostic (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            MonarchCharacterId INT UNSIGNED NOT NULL,
            VaultOwnerId CHAR(36) NOT NULL,
            Reason VARCHAR(512) NOT NULL,
            DetectedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudMonarchDeletionDiagnostic_Shard_Monarch UNIQUE (ShardId, MonarchCharacterId),
            CONSTRAINT FK_CloudMonarchDeletionDiagnostic_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudMonarchDeletionDiagnostic;",
        "DROP TABLE CloudAllegianceVaultBinding;",
        "DROP TABLE CloudIdentityOutboxEvent;",
        "DROP TABLE CloudIdentityOutboxSequence;",
    ];
}
