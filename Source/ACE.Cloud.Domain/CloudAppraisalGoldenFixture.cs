namespace ACE.Cloud.Domain;

/// <summary>
/// One named golden fixture pairing a raw item snapshot with the panel it is expected to produce.
/// The synthetic fixtures this issue ships live only in test code; a curated corpus captured against
/// real operator-owned items (never committed to the repository) is the #28 human gate's job to
/// execute through <see cref="CloudAppraisalGoldenComparisonHarness"/> against this exact contract.
/// </summary>
public sealed record CloudAppraisalGoldenFixture
{
    public required string FixtureName { get; init; }

    public required CloudAppraisalRawItemSnapshot Snapshot { get; init; }

    public required CloudAppraisalPanel ExpectedPanel { get; init; }
}
