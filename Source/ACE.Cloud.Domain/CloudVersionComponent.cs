namespace ACE.Cloud.Domain;

/// <summary>
/// Identifies which versioned component of a Cloud boundary transaction was found incompatible.
/// </summary>
public enum CloudVersionComponent
{
    AceExtension,
    CloudSchema,
    ContractProtocol,
}
