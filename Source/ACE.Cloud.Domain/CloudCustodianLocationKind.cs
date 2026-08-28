namespace ACE.Cloud.Domain;

/// <summary>
/// The three sources a Custodian Location can come from (DEP-007: "Default Custodian locations are
/// every mansion and Marketplace... administrators may add or remove custom positions").
/// </summary>
public enum CloudCustodianLocationKind
{
    Marketplace,
    Mansion,
    Custom,
}
