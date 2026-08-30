namespace ACE.Cloud.Domain;

/// <summary>The two independently limited Storage Quota scopes (INV-004).</summary>
public enum CloudStorageQuotaScope
{
    /// <summary>An individual Main Account's personal Cloud Inventory.</summary>
    Personal,

    /// <summary>One monarch's Allegiance Vault.</summary>
    AllegianceVault,
}
