namespace ACE.Cloud.Domain;

/// <summary>The outcome of <see cref="PyrealRemainderWithdrawalPolicy.Decide"/>.</summary>
public enum PyrealRemainderWithdrawalDecisionKind
{
    /// <summary>The requested amount may be withdrawn from the Pyreal Remainder.</summary>
    Approved,

    /// <summary>
    /// The requested amount is zero, negative, or exceeds the currently available Pyreal Remainder.
    /// Nothing changes; the remainder stays exactly as it was and the request may be retried, for
    /// example after a later deposit grows it.
    /// </summary>
    InsufficientRemainder,

    /// <summary>Every custody/reservation mutation is currently frozen (ADM-004).</summary>
    Frozen,
}

/// <summary>
/// The pure decision for one raw Pyreal Remainder withdrawal request (DEP-006's "allow safe raw
/// remainder withdrawal"). Kept free of any persistence dependency (ARCH-012) so every case can run
/// in a unit test.
/// </summary>
public sealed record PyrealRemainderWithdrawalDecision
{
    public PyrealRemainderWithdrawalDecisionKind Kind { get; }

    /// <summary>Only meaningful for <see cref="PyrealRemainderWithdrawalDecisionKind.Approved"/>.</summary>
    public long NewRemainder { get; }

    /// <summary>
    /// Only meaningful for <see cref="PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder"/>:
    /// the remainder that was actually available (unchanged by the refused request).
    /// </summary>
    public long AvailableRemainder { get; }

    private PyrealRemainderWithdrawalDecision(PyrealRemainderWithdrawalDecisionKind kind, long newRemainder, long availableRemainder)
    {
        Kind = kind;
        NewRemainder = newRemainder;
        AvailableRemainder = availableRemainder;
    }

    public static PyrealRemainderWithdrawalDecision Approved(long newRemainder) =>
        new(PyrealRemainderWithdrawalDecisionKind.Approved, newRemainder, availableRemainder: 0);

    public static PyrealRemainderWithdrawalDecision InsufficientRemainder(long availableRemainder) =>
        new(PyrealRemainderWithdrawalDecisionKind.InsufficientRemainder, newRemainder: 0, availableRemainder);

    public static PyrealRemainderWithdrawalDecision Frozen() =>
        new(PyrealRemainderWithdrawalDecisionKind.Frozen, newRemainder: 0, availableRemainder: 0);
}
