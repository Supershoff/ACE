namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud schema's own version, applied and stamped by <see cref="CloudDbContext"/>. OPS-002
/// requires the deployed schema version to match what the connecting ACE extension and Cloud
/// backend expect before any mutation is authorized.
/// </summary>
public static class CloudSchemaInfo
{
    public const string CurrentVersion = "0.1.0";
}
