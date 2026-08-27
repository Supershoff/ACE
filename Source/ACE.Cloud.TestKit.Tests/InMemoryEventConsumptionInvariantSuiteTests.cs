using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// Adopts <see cref="CloudEventConsumptionInvariantSuite{TPayload}"/> with zero test logic of its
/// own. See <see cref="InMemoryIdempotentCommandInvariantSuiteTests"/> for why this proves issue
/// #10's "adopt without copying" acceptance criterion.
/// </summary>
[TestClass]
public sealed class InMemoryEventConsumptionInvariantSuiteTests : CloudEventConsumptionInvariantSuite<string>
{
    protected override ICloudEventConsumptionHarness<string> CreateHarness() => new InMemoryEventConsumptionHarness();

    protected override string CreatePayload(int step) => $"payload-{step}";
}
