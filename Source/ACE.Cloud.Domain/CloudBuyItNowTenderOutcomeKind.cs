namespace ACE.Cloud.Domain;

/// <summary>The outcome of <see cref="CloudBuyItNowTenderPolicy.Preview"/> (MKT-108, MKT-109).</summary>
public enum CloudBuyItNowTenderOutcomeKind
{
    /// <summary>The exact advertised price was composed with no change (MKT-108's preferred path).</summary>
    Exact,

    /// <summary>
    /// No exact tender exists; the smallest authorized overpayment above the advertised price is
    /// offered for the buyer's explicit confirmation (MKT-108, MKT-109, Buy It Now Overpayment).
    /// </summary>
    RequiresOverpaymentConfirmation,

    /// <summary>The authorized payment mix cannot reach the advertised price at all, even overpaying.</summary>
    NoTenderAvailable,

    /// <summary>The search exceeded <see cref="CloudExactTenderEngine.MaxScaledSearchSpan"/>.</summary>
    SearchBoundExceeded,
}
