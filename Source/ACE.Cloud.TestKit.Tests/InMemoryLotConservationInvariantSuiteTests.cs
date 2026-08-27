using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>
/// Adopts <see cref="CloudLotConservationInvariantSuite{TLotId, TOwnerId}"/> with zero test logic
/// of its own. See <see cref="InMemoryIdempotentCommandInvariantSuiteTests"/> for why this proves
/// issue #10's "adopt without copying" acceptance criterion.
/// </summary>
[TestClass]
public sealed class InMemoryLotConservationInvariantSuiteTests : CloudLotConservationInvariantSuite<Guid, Guid>
{
    protected override ICloudLotConservationHarness<Guid, Guid> CreateHarness() => new InMemoryLotConservationHarness(totalQuantity: 1_000);
}
