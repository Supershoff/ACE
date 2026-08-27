using ACE.Cloud.Domain;

namespace ACE.Cloud.TestKit;

/// <summary>
/// Reusable contract tests proving duplicate and out-of-order Custody Outbox event consumption is
/// safe (ARCH-007, transaction rule 6). See
/// <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> for how an adapter adopts a suite
/// like this without copying its test logic.
/// </summary>
public abstract class CloudEventConsumptionInvariantSuite<TPayload>
{
    protected abstract ICloudEventConsumptionHarness<TPayload> CreateHarness();

    protected abstract TPayload CreatePayload(int step);

    [TestMethod]
    public async Task DuplicateDelivery_OfAnAlreadyAppliedEvent_IsANoOp()
    {
        var harness = CreateHarness();
        var envelope = harness.CreateEnvelope(CloudAggregateVersion.Initial, CreatePayload(1));

        await harness.ApplyAsync(envelope);
        await harness.ApplyAsync(envelope);

        Assert.AreEqual(
            CloudAggregateVersion.Initial,
            await harness.GetAppliedVersionAsync(),
            "Redelivering an already-applied event must be an idempotent no-op; a consumer must never advance or duplicate its projected state on a replay (ARCH-007).");
    }

    [TestMethod]
    public async Task OutOfOrderDelivery_ConvergesToTheHighestVersion_EvenWhenTheLaterEventArrivesFirst()
    {
        var harness = CreateHarness();
        var older = harness.CreateEnvelope(CloudAggregateVersion.Initial, CreatePayload(1));
        var newer = harness.CreateEnvelope(CloudAggregateVersion.Initial.Next(), CreatePayload(2));

        await harness.ApplyAsync(newer);
        await harness.ApplyAsync(older);

        Assert.AreEqual(
            newer.Version,
            await harness.GetAppliedVersionAsync(),
            "A consumer must converge to the highest version it has observed regardless of delivery order (transaction rule 6: at-least-once delivery requires idempotent, order-tolerant consumers).");
    }

    [TestMethod]
    public async Task RedeliveringAnOlderEvent_AfterANewerEventAlreadyApplied_NeverRegressesState()
    {
        var harness = CreateHarness();
        var older = harness.CreateEnvelope(CloudAggregateVersion.Initial, CreatePayload(1));
        var newer = harness.CreateEnvelope(CloudAggregateVersion.Initial.Next(), CreatePayload(2));

        await harness.ApplyAsync(older);
        await harness.ApplyAsync(newer);
        await harness.ApplyAsync(older);

        Assert.AreEqual(
            newer.Version,
            await harness.GetAppliedVersionAsync(),
            "A stale redelivery arriving after a newer event was already applied must never roll the projection backward.");
    }
}
