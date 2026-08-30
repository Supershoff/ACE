namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #34's schema addition (EVT-003): the Notification Center's coalesced rows, one per
/// (owner, kind) unread streak. <see cref="CloudNotificationProjectionConsumer"/> is the only writer,
/// following the exact singleton-checkpoint-plus-dead-letter shape
/// <see cref="AddProjectionCheckpointsDeadLettersAndLiveStream"/> already established for every other
/// outbox projection consumer -- this migration adds only the notification row itself; the shared
/// <c>CloudProjectionCheckpoint</c>/<c>CloudProjectionDeadLetter</c> tables that migration created are
/// reused as-is with a new <c>ConsumerName</c> ("NotificationProjection").
/// </summary>
public sealed class AddCloudNotification : CloudSchemaMigrationStep
{
    public AddCloudNotification()
        : base("20260830000005_AddCloudNotification")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        """
        CREATE TABLE CloudNotification (
            Id CHAR(36) NOT NULL,
            ShardId VARCHAR(64) NOT NULL,
            OwnerId CHAR(36) NOT NULL,
            Kind VARCHAR(32) NOT NULL,
            Destination VARCHAR(128) NOT NULL,
            OccurrenceCount INT NOT NULL DEFAULT 0,
            LatestSourceEventId CHAR(36) NOT NULL,
            LatestSourceSequenceNumber BIGINT NOT NULL,
            IsRead TINYINT(1) NOT NULL DEFAULT 0,
            FirstOccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            LastOccurredAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            ReadAtUtc DATETIME(6) NULL,
            PRIMARY KEY (Id),
            CONSTRAINT FK_CloudNotification_CloudShardBinding_ShardId
                FOREIGN KEY (ShardId) REFERENCES CloudShardBinding (ShardId)
                ON DELETE RESTRICT ON UPDATE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """,
        // The Notification Center's own two read paths: "this owner's unread badge count" and "this
        // owner's most recent unread notification of a given kind" (the coalescing lookup).
        "CREATE INDEX IX_CloudNotification_ShardId_OwnerId_IsRead ON CloudNotification (ShardId, OwnerId, IsRead);",
        "CREATE INDEX IX_CloudNotification_ShardId_OwnerId_Kind_IsRead ON CloudNotification (ShardId, OwnerId, Kind, IsRead);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP TABLE CloudNotification;",
    ];
}
