namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #38's schema addition (VAULT-005, ADM-002): records an administrator's audited decision on
/// a <c>CloudMonarchDeletionDiagnostic</c> row -- who resolved it, when, why, and which explicit
/// destination they chose. A diagnostic is resolved at most once (<c>CloudMonarchDeletionDiagnostic.Resolve</c>
/// throws otherwise), so a committed recovery can never be silently overridden by a later attempt.
/// </summary>
public sealed class AddMonarchVaultRecovery : CloudSchemaMigrationStep
{
    public AddMonarchVaultRecovery()
        : base("20260901000002_AddMonarchVaultRecovery")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        "ALTER TABLE CloudMonarchDeletionDiagnostic ADD COLUMN IsResolved TINYINT(1) NOT NULL DEFAULT 0;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic ADD COLUMN ResolvedAtUtc DATETIME(6) NULL;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic ADD COLUMN ResolvedByAdminAccountId INT UNSIGNED NULL;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic ADD COLUMN ResolutionReason VARCHAR(512) NULL;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic ADD COLUMN DestinationOwnerId CHAR(36) NULL;",
        "CREATE INDEX IX_CloudMonarchDeletionDiagnostic_Shard_IsResolved ON CloudMonarchDeletionDiagnostic (ShardId, IsResolved);",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "DROP INDEX IX_CloudMonarchDeletionDiagnostic_Shard_IsResolved ON CloudMonarchDeletionDiagnostic;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic DROP COLUMN DestinationOwnerId;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic DROP COLUMN ResolutionReason;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic DROP COLUMN ResolvedByAdminAccountId;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic DROP COLUMN ResolvedAtUtc;",
        "ALTER TABLE CloudMonarchDeletionDiagnostic DROP COLUMN IsResolved;",
    ];
}
