namespace ACE.Cloud.Domain;

/// <summary>The three shapes a Cloud Custodian sell-window row can resolve to (DEP-002).</summary>
public enum CloudCustodianDepositRowDecisionKind
{
    /// <summary>Deposit the whole non-stackable native biota as a whole-item Cloud Custody Record.</summary>
    DepositWhole,

    /// <summary>Deposit a stackable native biota as a stack Cloud Custody Record plus one Cloud Stack Lot.</summary>
    DepositStack,

    /// <summary>Reject this row; the item stays with the player.</summary>
    Reject,
}
