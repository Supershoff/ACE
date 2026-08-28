using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// AC Cloud Mule issue #17's Red requirement: "Replay duplicate/out-of-order identity and allegiance
/// events into a fake consumer." Adopts <see cref="CloudEventConsumptionInvariantSuite{TPayload}"/>
/// with zero test logic of its own -- the exact same "adopt without copying" pattern issue #10
/// established -- proving character rename/deletion events survive duplicate and out-of-order
/// delivery exactly like every other outbox event shape already covered.
/// </summary>
[TestClass]
public sealed class CloudCharacterIdentityEventConsumptionInvariantSuiteTests
    : CloudEventConsumptionInvariantSuite<CloudCharacterIdentityEventPayload>
{
    protected override ICloudEventConsumptionHarness<CloudCharacterIdentityEventPayload> CreateHarness() =>
        new InMemoryEventConsumptionHarness<CloudCharacterIdentityEventPayload>();

    protected override CloudCharacterIdentityEventPayload CreatePayload(int step) =>
        new(characterId: (uint)(0x80000000 + step), accountId: 1, CloudIdentityEventType.CharacterRenamed, $"Name{step}", totalLogins: step);
}
