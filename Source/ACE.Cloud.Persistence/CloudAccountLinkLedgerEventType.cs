namespace ACE.Cloud.Persistence;

/// <summary>The kind of audited fact one <see cref="CloudAccountLinkLedgerEvent"/> records (EVT-001, EVT-002).</summary>
public enum CloudAccountLinkLedgerEventType
{
    Linked,
    LinkRejected,
    Unlinked,
    UnlinkRejected,
}
