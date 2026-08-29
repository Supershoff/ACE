namespace ACE.Cloud.Domain;

/// <summary>
/// One curated Icon Reconstruction fixture for issue #28's protected fidelity gate (ASSET-005:
/// "golden reconstruction tests for a curated corpus covering clothing palette/shade variants,
/// underlays, overlays, tailoring, imbues, magical UI effects, stack counts, and missing/corrupt
/// references"). Deliberately carries only <see cref="ExpectedPngSha256Hex"/> rather than the expected
/// PNG bytes themselves: the fixture file is safe to check in or hand to an agent, because it names no
/// DID's rendered appearance, only a content hash an operator's own protected DAT must reproduce.
/// </summary>
public sealed record CloudIconGoldenFixture
{
    public required string FixtureName { get; init; }

    public required CloudIconCompositionInputs Inputs { get; init; }

    public required string ExpectedPngSha256Hex { get; init; }
}
