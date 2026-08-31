using System;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Cloud.Hosting;
using ACE.Common;
using ACE.Server.Managers;

namespace ACE.Server.Tests
{
    /// <summary>
    /// Issue #34's blocking defect #1 (a real, ACE-owned liveness endpoint -- not a fake listener)
    /// and defect #6 (an automated acceptance-contract test against the real hosting component,
    /// since a mock-only health test is insufficient). Every test here hosts a real Kestrel listener
    /// on an OS-assigned loopback port (<c>Port = 0</c>), never a fixed port, so this never collides
    /// with another process or a parallel CI run.
    /// </summary>
    [TestClass]
    public class CloudWorldBoundaryHealthHostTests
    {
        [TestCleanup]
        public void TestCleanup()
        {
            CloudWorldBoundaryHealthHost.Stop();
        }

        private static MasterConfiguration EnabledConfig() => new MasterConfiguration
        {
            CloudMule = new CloudMuleConfiguration
            {
                Enabled = true,
                HealthEndpoint = new CloudMuleHealthEndpointConfiguration
                {
                    Enabled = true,
                    BindAddress = "127.0.0.1",
                    Port = 0,
                },
            },
        };

        [TestMethod]
        public async Task Start_ExposesRealLivenessEndpoint()
        {
            ConfigManager.Initialize(EnabledConfig());

            CloudWorldBoundaryHealthHost.Start();

            Assert.IsNotNull(CloudWorldBoundaryHealthHost.ListenAddress, "Start() should resolve a real bound address.");

            using var client = new HttpClient();
            using var response = await client.GetAsync(new Uri(CloudWorldBoundaryHealthHost.ListenAddress, "health/live"));

            Assert.IsTrue(response.IsSuccessStatusCode);
        }

        [TestMethod]
        public void Start_NoOpWhenCloudMuleDisabled()
        {
            ConfigManager.Initialize(new MasterConfiguration
            {
                CloudMule = new CloudMuleConfiguration { Enabled = false },
            });

            CloudWorldBoundaryHealthHost.Start();

            Assert.IsNull(CloudWorldBoundaryHealthHost.ListenAddress, "Disabled CloudMule must never open a listening socket.");
        }

        [TestMethod]
        public void Start_NoOpWhenHealthEndpointDisabled()
        {
            var config = EnabledConfig();
            config.CloudMule.HealthEndpoint.Enabled = false;
            ConfigManager.Initialize(config);

            CloudWorldBoundaryHealthHost.Start();

            Assert.IsNull(CloudWorldBoundaryHealthHost.ListenAddress);
        }

        [TestMethod]
        public async Task Stop_LeavesEndpointUnreachable()
        {
            ConfigManager.Initialize(EnabledConfig());
            CloudWorldBoundaryHealthHost.Start();
            var address = CloudWorldBoundaryHealthHost.ListenAddress;

            CloudWorldBoundaryHealthHost.Stop();

            Assert.IsNull(CloudWorldBoundaryHealthHost.ListenAddress);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            await Assert.ThrowsExactlyAsync<HttpRequestException>(
                () => client.GetAsync(new Uri(address, "health/live")));
        }

        /// <summary>
        /// The acceptance-contract test itself: reuses the exact production probe
        /// (<see cref="HttpCloudWorldBoundaryHealthProbe"/> via <see cref="CloudStartupChecks.WorldBoundary"/>)
        /// Backend and Worker call at their own startup, against the real ACE liveness host -- proving
        /// the prepare -&gt; ACE live -&gt; ready contract with no mock in the loop.
        /// </summary>
        [TestMethod]
        public async Task RealProductionProbe_ObservesLiveThenStopped()
        {
            ConfigManager.Initialize(EnabledConfig());
            CloudWorldBoundaryHealthHost.Start();
            Assert.IsNotNull(CloudWorldBoundaryHealthHost.ListenAddress);

            using var httpClient = new HttpClient();
            var probe = new HttpCloudWorldBoundaryHealthProbe(httpClient, new CloudWorldBoundaryProbeOptions
            {
                HealthEndpoint = new Uri(CloudWorldBoundaryHealthHost.ListenAddress, "health/live"),
            });
            var check = CloudStartupChecks.WorldBoundary(probe);

            var liveResult = await check(default);
            Assert.IsTrue(liveResult.IsHealthy, liveResult.Reason);

            CloudWorldBoundaryHealthHost.Stop();

            var stoppedResult = await check(default);
            Assert.IsFalse(stoppedResult.IsHealthy, "The probe must report unhealthy once ACE's liveness host has stopped.");
        }
    }
}
