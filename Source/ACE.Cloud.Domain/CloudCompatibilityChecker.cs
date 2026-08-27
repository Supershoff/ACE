namespace ACE.Cloud.Domain;

/// <summary>
/// Enforces OPS-002: refuse a Cloud boundary mutation when the ACE extension, Cloud schema, or
/// contract protocol versions of the two sides of the transaction do not match exactly.
/// </summary>
public static class CloudCompatibilityChecker
{
    /// <summary>
    /// Evaluates compatibility requiring every component -- including the contract protocol -- to
    /// match exactly. This is the default, most conservative policy and remains unchanged so
    /// existing callers keep their current exact-match behavior.
    /// </summary>
    public static CloudCompatibilityResult Evaluate(CloudComponentVersions expected, CloudComponentVersions actual) =>
        Evaluate(expected, actual, supportedProtocolWindow: null);

    /// <summary>
    /// Evaluates compatibility the same way as <see cref="Evaluate(CloudComponentVersions, CloudComponentVersions)"/>,
    /// except that when <paramref name="supportedProtocolWindow"/> is supplied, the contract
    /// protocol version only needs to fall within that declared inclusive range rather than match
    /// <paramref name="expected"/> exactly (OPS-002: "declare supported ACE releases" and "use
    /// versioned forward migrations"). The ACE extension and Cloud schema versions always require
    /// an exact match regardless of this parameter.
    /// </summary>
    public static CloudCompatibilityResult Evaluate(
        CloudComponentVersions expected, CloudComponentVersions actual, CloudSupportedProtocolWindow? supportedProtocolWindow)
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

        if (supportedProtocolWindow is null)
        {
            if (!string.Equals(expected.ContractProtocolVersion, actual.ContractProtocolVersion, StringComparison.Ordinal))
            {
                return CloudCompatibilityResult.Incompatible(
                    CloudVersionComponent.ContractProtocol,
                    $"Contract protocol version mismatch: expected {expected.ContractProtocolVersion}, found {actual.ContractProtocolVersion}.");
            }
        }
        else if (!CloudProtocolVersion.TryParse(actual.ContractProtocolVersion, out var actualProtocolVersion) ||
                 !supportedProtocolWindow.Contains(actualProtocolVersion))
        {
            return CloudCompatibilityResult.Incompatible(
                CloudVersionComponent.ContractProtocol,
                $"Contract protocol version {actual.ContractProtocolVersion} is outside the declared supported window {supportedProtocolWindow}.");
        }

        return CloudCompatibilityResult.Compatible();
    }
}
