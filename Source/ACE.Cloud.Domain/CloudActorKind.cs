namespace ACE.Cloud.Domain;

/// <summary>
/// The kind of actor identity behind a Cloud command or Activity Ledger event (EVT-002).
/// </summary>
public enum CloudActorKind
{
    /// <summary>An authenticated Main or Linked Account web session.</summary>
    Account,

    /// <summary>An in-game character, for example a Withdrawal Token redemption or a Cloud Custodian deposit.</summary>
    Character,

    /// <summary>An ACE administrator (accessLevel 5) performing an audited intervention (ADM-001).</summary>
    Administrator,

    /// <summary>An automated system actor with no individual account or character, for example an expiry sweep.</summary>
    System,
}
