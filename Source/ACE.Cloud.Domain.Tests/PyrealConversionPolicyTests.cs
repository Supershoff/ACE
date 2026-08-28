namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The boundary/property corpus required by issue #14's Red section (DEP-006): 0, 1, 287,499,
/// 287,500, 287,501, large values, existing remainders, repeated idempotency (pure math -- the same
/// inputs always produce the same outputs, proven by simply calling twice), and integer overflow.
/// </summary>
[TestClass]
public sealed class PyrealConversionPolicyTests
{
    [TestMethod]
    public void Convert_ZeroDeposit_NoExistingRemainder_ProducesNoMmdsAndNoRemainder()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: 0);

        Assert.AreEqual(0, result.MmdCount);
        Assert.AreEqual(0, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_OnePyreal_NoExistingRemainder_ProducesNoMmdsAndARemainderOfOne()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: 1);

        Assert.AreEqual(0, result.MmdCount);
        Assert.AreEqual(1, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_OneBelowThreshold_ProducesNoMmdsAndPreservesTheFullAmountAsRemainder()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: PyrealConversionPolicy.PyrealsPerMmd - 1);

        Assert.AreEqual(0, result.MmdCount);
        Assert.AreEqual(PyrealConversionPolicy.PyrealsPerMmd - 1, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_ExactlyOneThreshold_ProducesExactlyOneMmdAndNoRemainder()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: PyrealConversionPolicy.PyrealsPerMmd);

        Assert.AreEqual(1, result.MmdCount);
        Assert.AreEqual(0, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_OneAboveThreshold_ProducesExactlyOneMmdAndARemainderOfOne()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: PyrealConversionPolicy.PyrealsPerMmd + 1);

        Assert.AreEqual(1, result.MmdCount);
        Assert.AreEqual(1, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_ALargeDeposit_ProducesTheExactWholeQuotientAndRemainder()
    {
        // 34 MMDs worth plus a partial remainder, all in one deposit.
        const long incoming = 34 * PyrealConversionPolicy.PyrealsPerMmd + 123_456;

        var result = PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: incoming);

        Assert.AreEqual(34, result.MmdCount);
        Assert.AreEqual(123_456, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_CombinesWithAnExistingRemainderBeforeConverting()
    {
        // An existing remainder of 200,000 plus a fresh deposit of 100,000 crosses the threshold
        // once: 300,000 total => 1 MMD (287,500) + 12,500 remainder.
        var result = PyrealConversionPolicy.Convert(existingRemainder: 200_000, incomingRawAmount: 100_000);

        Assert.AreEqual(1, result.MmdCount);
        Assert.AreEqual(12_500, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_AnExistingRemainderWithAZeroDeposit_LeavesTheRemainderExactlyUnchanged()
    {
        var result = PyrealConversionPolicy.Convert(existingRemainder: 42, incomingRawAmount: 0);

        Assert.AreEqual(0, result.MmdCount);
        Assert.AreEqual(42, result.NewRemainder);
    }

    [TestMethod]
    public void Convert_TheSameInputsTwice_ProducesTheSameResultBothTimes()
    {
        // Pure math is trivially "idempotent under retry": calling twice with the same combined
        // inputs must never drift, matching what a caller replaying a repeated deposit needs.
        var first = PyrealConversionPolicy.Convert(existingRemainder: 5_000, incomingRawAmount: 900_000);
        var second = PyrealConversionPolicy.Convert(existingRemainder: 5_000, incomingRawAmount: 900_000);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Convert_ConservesTheExactCombinedTotalAcrossManyBoundaryCases()
    {
        long[] existingRemainders = [0, 1, 1, 287_499, 12_345, PyrealConversionPolicy.PyrealsPerMmd - 1];
        long[] incomingAmounts = [0, 1, 287_499, 1, 999_999_999, PyrealConversionPolicy.PyrealsPerMmd + 1];

        for (var i = 0; i < existingRemainders.Length; i++)
        {
            var existing = existingRemainders[i];
            var incoming = incomingAmounts[i];

            var result = PyrealConversionPolicy.Convert(existing, incoming);

            Assert.AreEqual(
                existing + incoming,
                (result.MmdCount * PyrealConversionPolicy.PyrealsPerMmd) + result.NewRemainder,
                $"Conservation failed for existing={existing}, incoming={incoming}.");
            Assert.IsTrue(result.NewRemainder is >= 0 and < PyrealConversionPolicy.PyrealsPerMmd);
        }
    }

    [TestMethod]
    public void Convert_ANegativeIncomingAmount_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PyrealConversionPolicy.Convert(existingRemainder: 0, incomingRawAmount: -1));
    }

    [TestMethod]
    public void Convert_ANegativeExistingRemainder_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PyrealConversionPolicy.Convert(existingRemainder: -1, incomingRawAmount: 0));
    }

    [TestMethod]
    public void Convert_AnExistingRemainderAtTheThreshold_Throws()
    {
        // An existing remainder is a domain invariant: it must already be below the threshold
        // (otherwise it should have already been converted). A caller-side bug that lets a
        // remainder reach or exceed the threshold must fail loudly, not silently under-convert.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => PyrealConversionPolicy.Convert(existingRemainder: PyrealConversionPolicy.PyrealsPerMmd, incomingRawAmount: 0));
    }

    [TestMethod]
    public void Convert_ATotalThatWouldOverflowA64BitSum_ThrowsInsteadOfWrapping()
    {
        Assert.ThrowsExactly<OverflowException>(
            () => PyrealConversionPolicy.Convert(existingRemainder: PyrealConversionPolicy.PyrealsPerMmd - 1, incomingRawAmount: long.MaxValue));
    }

    [TestMethod]
    public void Convert_TheLargestRepresentableRawDeposit_DoesNotThrowAndConserves()
    {
        // int.MaxValue is the practical ceiling for a single row (WorldObject.Value is int?), far
        // below where a 64-bit sum could overflow; this proves the realistic large-deposit path
        // works, distinct from the deliberate long.MaxValue overflow case above.
        var result = PyrealConversionPolicy.Convert(existingRemainder: PyrealConversionPolicy.PyrealsPerMmd - 1, incomingRawAmount: int.MaxValue);

        Assert.AreEqual(
            (PyrealConversionPolicy.PyrealsPerMmd - 1) + (long)int.MaxValue,
            (result.MmdCount * PyrealConversionPolicy.PyrealsPerMmd) + result.NewRemainder);
    }
}
