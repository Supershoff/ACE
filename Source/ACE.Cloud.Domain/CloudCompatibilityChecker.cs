namespace ACE.Cloud.Domain;

/// <summary>
/// Enforces OPS-002: refuse a Cloud boundary mutation when the ACE extension, Cloud schema, or
/// contract protocol versions of the two sides of the transaction do not match exactly.
/// </summary>
public static class CloudCompatibilityChecker
{
    public static CloudCompatibilityResult Evaluate(CloudComponentVersions expected, CloudComponentVersions actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.AceExtensionVersion, actual.AceExtensionVersion, StringComparison.Ordinal))
        {
            return CloudCompatibilityResult.Incompatible(
                CloudVersionComponent.AceExtension,
                $"ACE extension version mismatch: expected {expected.AceExtensionVersion}, found {actual.AceExtensionVersion}.");
        }

        if (!string.Equals(expected.CloudSchemaVersion, actual.CloudSchemaVersion, StringComparison.Ordinal))
        {
            return CloudCompatibilityResult.Incompatible(
                CloudVersionComponent.CloudSchema,
                $"Cloud schema version mismatch: expected {expected.CloudSchemaVersion}, found {actual.CloudSchemaVersion}.");
        }

        if (!string.Equals(expected.ContractProtocolVersion, actual.ContractProtocolVersion, StringComparison.Ordinal))
        {
            return CloudCompatibilityResult.Incompatible(
                CloudVersionComponent.ContractProtocol,
                $"Contract protocol version mismatch: expected {expected.ContractProtocolVersion}, found {actual.ContractProtocolVersion}.");
        }

        return CloudCompatibilityResult.Compatible();
    }
}
