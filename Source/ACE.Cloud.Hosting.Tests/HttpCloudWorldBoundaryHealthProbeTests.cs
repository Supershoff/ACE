using System.Net;
using ACE.Cloud.Hosting;

namespace ACE.Cloud.Hosting.Tests;

/// <summary>
/// Red -> Green tests for issue #18's "world process offline" startup scenario. Uses a fake
/// <see cref="HttpMessageHandler"/> instead of a real ACE.Server process: the companion services
/// never reference ACE.Server (ARCH-003/ARCH-004), so the only contract-honest way to prove "the
/// world-boundary endpoint is unreachable" behavior is over the network seam this probe actually
/// uses, not an in-process call into ACE.Server.
/// </summary>
[TestClass]
public sealed class HttpCloudWorldBoundaryHealthProbeTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:9600/health/live");

    [TestMethod]
    public async Task CheckAsync_WhenTheEndpointRespondsSuccessfully_ReturnsHealthy()
    {
        var probe = CreateProbe(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var result = await probe.CheckAsync();

        Assert.IsTrue(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.WorldBoundary, result.Component);
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheEndpointRespondsWithAnErrorStatus_ReturnsUnhealthy()
    {
        var probe = CreateProbe(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var result = await probe.CheckAsync();

        Assert.IsFalse(result.IsHealthy);
        StringAssert.Contains(result.Reason, "ACE world process is offline");
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheConnectionIsRefused_ReturnsUnhealthy_InsteadOfThrowing()
    {
        var probe = CreateProbe(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused")));

        var result = await probe.CheckAsync();

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.WorldBoundary, result.Component);
        StringAssert.Contains(result.Reason, "ACE world process is offline");
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheEndpointNeverResponds_ReturnsUnhealthy_InsteadOfHangingOrThrowing()
    {
        var probe = CreateProbe(
            new FakeHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await probe.CheckAsync();

        Assert.IsFalse(result.IsHealthy);
        StringAssert.Contains(result.Reason, "ACE world process is offline");
    }

    private static HttpCloudWorldBoundaryHealthProbe CreateProbe(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler),
            new CloudWorldBoundaryProbeOptions { HealthEndpoint = Endpoint, Timeout = timeout ?? TimeSpan.FromSeconds(3) });

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respondAsync(request, cancellationToken);
    }
}
