namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #11's two additions to the ACE-side world-boundary persistence gateway:
///
///   - CloudCustodyOutboxSequence + CloudCustodyOutboxEvent.SequenceNumber give the Custody Outbox a
///     durable total order (ARCH-007's "durable ordered outbox events"): CreatedAtUtc alone cannot
///     guarantee a strict order because two events committed in the same database-clock microsecond
///     would tie. Reserving the next value locks CloudCustodyOutboxSequence's single row for the
///     whole boundary transaction (the same deterministic-locking approach AddCloudStackLots already
///     established for conservation), so sequence numbers are assigned in commit order with no gaps
///     shared between concurrent writers.
///
///   - CloudWithdrawalReservation is ACE's local authority record for a Withdrawal Token's exclusive
///     Withdrawal Reservation (WDR-001, WDR-002, WDR-003, WDR-008): it lets ACE validate and redeem
///     an already-issued token entirely from its own database during a web outage, without waiting
///     for the companion backend's own reservation bookkeeping. TokenHash is a one-way verifier of
///     the token secret (security baseline), never the secret itself.
/// </summary>
public sealed class AddWithdrawalReservationsAndOrderedOutbox : CloudSchemaMigrationStep
{
    public AddWithdrawalReservationsAndOrderedOutbox()
        : base("20260827000006_AddWithdrawalReservationsAndOrderedOutbox")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudCustodyOutboxSequence (
            Id INT NOT NULL,
            NextValue BIGINT NOT NULL,
            PRIMARY KEY (Id),
            CONSTRAINT CK_CloudCustodyOutboxSequence_Singleton CHECK (`Id` = 1)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "INSERT INTO CloudCustodyOutboxSequence (Id, NextValue) VALUES (1, 1);",
        "ALTER TABLE CloudCustodyOutboxEvent ADD COLUMN SequenceNumber BIGINT NOT NULL AFTER Id;",
        "CREATE UNIQUE INDEX IX_CloudCustodyOutboxEvent_SequenceNumber ON CloudCustodyOutboxEvent (SequenceNumber);",
        """
        CREATE TABLE CloudWithdrawalReservation (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            BiotaId INT UNSIGNED NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            TokenHash CHAR(64) NOT NULL,
            OpenIdempotencyKey CHAR(36) NOT NULL,
            Status VARCHAR(16) NOT NULL,
            ReleaseReason VARCHAR(32) NULL,
            Version INT NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            ExpiresAtUtc DATETIME(6) NOT NULL,
            ReleasedAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT UQ_CloudWithdrawalReservation_TokenHash UNIQUE (TokenHash),
            CONSTRAINT UQ_CloudWithdrawalReservation_OpenIdempotencyKey UNIQUE (OpenIdempotencyKey),
            CONSTRAINT FK_CloudWithdrawalReservation_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        "CREATE INDEX IX_CloudWithdrawalReservation_BiotaId ON CloudWithdrawalReservation (BiotaId);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudWithdrawalReservation;",
        "DROP INDEX IX_CloudCustodyOutboxEvent_SequenceNumber ON CloudCustodyOutboxEvent;",
        "ALTER TABLE CloudCustodyOutboxEvent DROP COLUMN SequenceNumber;",
        "DROP TABLE CloudCustodyOutboxSequence;",
    ];
}
