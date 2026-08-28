namespace ACE.Cloud.Domain;

/// <summary>
/// The pure result of <see cref="PyrealConversionPolicy.Convert"/> (DEP-006): exactly how many
/// MMDs a combined raw-Pyreal total creates, and the exact unconverted Pyreal Remainder left over.
/// <c>MmdCount * PyrealConversionPolicy.PyrealsPerMmd + NewRemainder</c> always equals the total
/// combined amount that was converted -- the conservation property CONTEXT.md requires ("preserved
/// without rounding or loss").
/// </summary>
public readonly record struct PyrealConversionResult(long MmdCount, long NewRemainder);
