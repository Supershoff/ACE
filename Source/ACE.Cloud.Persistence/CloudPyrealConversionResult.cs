namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of a Raw Pyreal Deposit conversion (DEP-006): the MMD custody records
/// created and the account's exact new Pyreal Remainder.
/// </summary>
public sealed record CloudPyrealConversionResult(IReadOnlyList<CloudCustodyRecord> MmdCustodyRecords, long NewRemainder);
