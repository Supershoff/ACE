namespace ACE.Cloud.Domain;

/// <summary>
/// The pure Raw Pyreal Deposit conversion rule (DEP-006, requirement `DEP-006`): "Raw Pyreals
/// convert at exactly 287,500 Pyreals per MMD (`Trade Note (250,000)`). Combine deposits with an
/// exact account-level Pyreal Remainder, create as many MMDs as possible, preserve the remainder
/// without rounding, and allow raw withdrawal of that remainder." Kept free of any persistence or
/// ACE.Server dependency (ARCH-012) so every boundary/property case can run in a unit test.
/// </summary>
public static class PyrealConversionPolicy
{
    /// <summary>The exact conversion rate: one MMD per this many raw Pyreals (DEP-006).</summary>
    public const long PyrealsPerMmd = 287_500L;

    /// <summary>
    /// Combines <paramref name="existingRemainder"/> (an account's current Pyreal Remainder) with
    /// <paramref name="incomingRawAmount"/> (a new Raw Pyreal Deposit's amount) and splits the exact
    /// total into as many whole MMDs as possible plus the exact leftover remainder. No Pyreal is
    /// ever rounded, lost, duplicated, or converted at another rate: the combined total is always
    /// exactly reconstructible as <c>result.MmdCount * PyrealsPerMmd + result.NewRemainder</c>.
    /// </summary>
    public static PyrealConversionResult Convert(long existingRemainder, long incomingRawAmount)
    {
        if (existingRemainder < 0 || existingRemainder >= PyrealsPerMmd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(existingRemainder),
                existingRemainder,
                $"An existing Pyreal Remainder must be at least 0 and strictly less than {PyrealsPerMmd} (a remainder at or above the threshold should already have been converted).");
        }

        if (incomingRawAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(incomingRawAmount), incomingRawAmount, "A Raw Pyreal Deposit amount cannot be negative.");
        }

        long total;
        try
        {
            total = checked(existingRemainder + incomingRawAmount);
        }
        catch (OverflowException)
        {
            throw new OverflowException(
                $"Combining the existing Pyreal Remainder ({existingRemainder}) with the incoming deposit ({incomingRawAmount}) overflows a 64-bit total; no conversion can be performed safely.");
        }

        var mmdCount = total / PyrealsPerMmd;
        var newRemainder = total % PyrealsPerMmd;

        return new PyrealConversionResult(mmdCount, newRemainder);
    }
}
