namespace ACE.Cloud.Domain;

/// <summary>
/// The client DAT an Asset Import session extracts from (ASSET-001: "Admin uploads
/// client_portal.dat... client_highres.dat is optional"). Each shard has at most one in-flight
/// import per kind.
/// </summary>
public enum CloudAssetKind
{
    Portal,
    HighRes,
}
