namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Issue #28's "item/manifest correlation" Red requirement: adds a nullable
/// <c>LastSeenManifestVersion</c> column to <c>CloudIconDiagnostic</c> so an administrator can see
/// which Asset Manifest version most recently reproduced a deduplicated diagnostic row. Nullable
/// because rows written before this migration have no recorded value; it never joins
/// <c>CloudAssetManifest</c> by foreign key because a diagnostic must remain visible after its
/// manifest version is superseded or its retained source DAT is reprocessed away.
/// </summary>
public sealed class AddCloudIconDiagnosticManifestCorrelation : CloudSchemaMigrationStep
{
    public AddCloudIconDiagnosticManifestCorrelation()
        : base("20260829000003_AddCloudIconDiagnosticManifestCorrelation")
    {
    }

    public override IReadOnlyList<string> UpStatements { get; } =
    [
        "ALTER TABLE CloudIconDiagnostic ADD COLUMN LastSeenManifestVersion INT UNSIGNED NULL AFTER LastSeenAtUtc;",
    ];

    public override IReadOnlyList<string> DownStatements { get; } =
    [
        "ALTER TABLE CloudIconDiagnostic DROP COLUMN LastSeenManifestVersion;",
    ];
}
