namespace ACE.Cloud.Persistence;

/// <summary>The exact Sharing Grant lifecycle moment a <see cref="CloudSharingGrantLedgerEvent"/> records (EVT-001, EVT-002).</summary>
public enum CloudSharingGrantLedgerEventType
{
    /// <summary>The owner set (created or changed) an explicit grant level, including to None (SHARE-004).</summary>
    LevelSet,

    /// <summary>Linking revoked this grant because it named the just-linked source account as owner or grantee (AUTH-008).</summary>
    RevokedByAccountLink,

    /// <summary>A grant-derived Withdrawal Reservation was released because this grant no longer authorizes it (SHARE-004).</summary>
    DerivedWithdrawalInvalidated,
}
