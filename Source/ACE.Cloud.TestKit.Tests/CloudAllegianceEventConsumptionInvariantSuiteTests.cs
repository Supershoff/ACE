using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// AC Cloud Mule issue #17's Red requirement: "Replay duplicate/out-of-order identity and allegiance
/// events into a fake consumer," applied to allegiance swear/break/monarch-change events (VAULT-001).
/// See <see cref="CloudCharacterIdentityEventConsumptionInvariantSuiteTests"/> for the character
/// identity analog and the "adopt without copying" rationale shared by both.
/// </summary>
[TestClass]
public sealed class CloudAllegianceEventConsumptionInvariantSuiteTests
    : CloudEventConsumptionInvariantSuite<CloudAllegianceEventPayload>
{
    protected override ICloudEventConsumptionHarness<CloudAllegianceEventPayload> CreateHarness() =>
        new InMemoryEventConsumptionHarness<CloudAllegianceEventPayload>();

    protected override CloudAllegianceEventPayload CreatePayload(int step) =>
        new(characterId: 0x80000001, CloudIdentityEventType.AllegianceSworn, monarchId: (uint)(0x80000000 + step), priorMonarchId: null,
            accountId: 1, characterName: "Vassal", totalLogins: 1);
}
