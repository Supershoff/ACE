namespace ACE.Cloud.Domain;

/// <summary>
/// The exact-tender engine's immutable result for one requested price (issue #9 Green section:
/// "Return allocations as immutable domain results; do not mutate custody inside the calculation
/// engine").
/// </summary>
public sealed record CloudTenderResult
{
    public CloudTenderOutcomeKind Kind { get; }

    public long? PriceUnits { get; }

    public IReadOnlyList<CloudTenderLine> Lines { get; }

    private CloudTenderResult(CloudTenderOutcomeKind kind, long? priceUnits, IReadOnlyList<CloudTenderLine> lines)
    {
        Kind = kind;
        PriceUnits = priceUnits;
        Lines = lines;
    }

    public bool IsComposed => Kind == CloudTenderOutcomeKind.Composed;

    public static CloudTenderResult Composed(long priceUnits, IReadOnlyList<CloudTenderLine> lines) =>
        new(CloudTenderOutcomeKind.Composed, priceUnits, lines);

    public static CloudTenderResult NoExactTenderExists() => new(CloudTenderOutcomeKind.NoExactTenderExists, null, []);

    public static CloudTenderResult PriceExceedsSearchBound() => new(CloudTenderOutcomeKind.PriceExceedsSearchBound, null, []);
}
