namespace ACE.Cloud.Domain;

/// <summary>
/// The three component versions OPS-002 requires to match exactly before a Cloud boundary
/// mutation may proceed: the ACE extension, the applied Cloud schema, and the negotiated
/// contract protocol.
/// </summary>
public sealed record CloudComponentVersions
{
    public CloudComponentVersions(string aceExtensionVersion, string cloudSchemaVersion, string contractProtocolVersion)
    {
        AceExtensionVersion = RequireVersion(aceExtensionVersion, nameof(aceExtensionVersion));
        CloudSchemaVersion = RequireVersion(cloudSchemaVersion, nameof(cloudSchemaVersion));
        ContractProtocolVersion = RequireVersion(contractProtocolVersion, nameof(contractProtocolVersion));
    }

    public string AceExtensionVersion { get; init; }

    public string CloudSchemaVersion { get; init; }

    public string ContractProtocolVersion { get; init; }

    private static string RequireVersion(string version, string paramName)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Component versions are required and cannot be empty.", paramName);
        }

        return version;
    }
}
