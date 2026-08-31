using System;
using System.IO;
using System.Linq;
using System.Reflection;

using log4net;

using ACE.Common;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ACE.Server.Managers
{
    /// <summary>
    /// ACE's own loopback/private-bound world-boundary liveness endpoint (ARCH-008, issue #34's
    /// blocking defect #1): a minimal, ACE-owned HTTP host that reports live only while this actual
    /// ACE process is up, plus a read-only Cloud Mule deposit-readiness diagnostic
    /// (<see cref="CloudCustodianManager.GetDepositReadinessAsync"/>). Companion services
    /// (<c>CloudStartupChecks.WorldBoundary</c>) and the disposable local acceptance launcher probe
    /// this instead of a fake listener. Exposes no custody mutation surface -- both routes are
    /// read-only.
    /// </summary>
    public static class CloudWorldBoundaryHealthHost
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static WebApplication _app;

        /// <summary>
        /// The actual bound address once <see cref="Start"/> has succeeded (resolved by Kestrel, so
        /// this reflects the real ephemeral port when <c>CloudMule.HealthEndpoint.Port</c> is 0 --
        /// used by <c>CloudWorldBoundaryHealthHostTests</c> to avoid a fixed test port colliding in
        /// CI). Null while stopped.
        /// </summary>
        public static Uri ListenAddress { get; private set; }

        /// <summary>
        /// No-op unless both <c>CloudMule.Enabled</c> and <c>CloudMule.HealthEndpoint.Enabled</c> are
        /// true. Called once from Program.cs immediately after <see cref="CloudCustodianManager.Initialize"/>.
        /// A port already in use reports a specific, actionable error and leaves ACE running -- this
        /// endpoint is a diagnostic aid, not a requirement for the world itself to start.
        /// </summary>
        public static void Start()
        {
            var cloudMuleConfig = ConfigManager.Config.CloudMule;
            var healthConfig = cloudMuleConfig.HealthEndpoint;

            if (!cloudMuleConfig.Enabled || !healthConfig.Enabled)
            {
                return;
            }

            if (_app is not null)
            {
                log.Warn("AC Cloud Mule: CloudWorldBoundaryHealthHost.Start() was called while already running; ignoring.");
                return;
            }

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://{healthConfig.BindAddress}:{healthConfig.Port}");

            var app = builder.Build();

            app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

            app.MapGet("/cloudmule/deposit-readiness", async () =>
            {
                var report = await CloudCustodianManager.GetDepositReadinessAsync().ConfigureAwait(false);
                return Results.Ok(report);
            });

            try
            {
                app.Start();
            }
            catch (IOException ex) when (ex.InnerException is AddressInUseException)
            {
                log.Error(
                    $"AC Cloud Mule: port {healthConfig.Port} on {healthConfig.BindAddress} is already in use; " +
                    "the world-boundary liveness endpoint did not start. Free the port or change " +
                    "CloudMule.HealthEndpoint.Port in Config.js.", ex);
                SafeDispose(app);
                return;
            }
            catch (AddressInUseException ex)
            {
                log.Error(
                    $"AC Cloud Mule: port {healthConfig.Port} on {healthConfig.BindAddress} is already in use; " +
                    "the world-boundary liveness endpoint did not start. Free the port or change " +
                    "CloudMule.HealthEndpoint.Port in Config.js.", ex);
                SafeDispose(app);
                return;
            }
            catch (Exception ex)
            {
                log.Error(
                    $"AC Cloud Mule: failed to start the world-boundary liveness endpoint on " +
                    $"{healthConfig.BindAddress}:{healthConfig.Port}.", ex);
                SafeDispose(app);
                return;
            }

            _app = app;
            ListenAddress = new Uri(app.Urls.First());
            log.Info($"AC Cloud Mule: world-boundary liveness endpoint listening at {ListenAddress}health/live");
        }

        /// <summary>
        /// Deterministic shutdown: called from Program.cs's OnProcessExit in both the containerized
        /// and non-containerized paths, before the process actually exits, so a companion service or
        /// the acceptance launcher observes this endpoint stop answering promptly rather than by
        /// connection-refused only after the OS reclaims the port.
        /// </summary>
        public static void Stop()
        {
            var app = _app;
            if (app is null)
            {
                return;
            }

            _app = null;
            ListenAddress = null;

            try
            {
                app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: error stopping the world-boundary liveness endpoint.", ex);
            }
            finally
            {
                SafeDispose(app);
            }
        }

        private static void SafeDispose(WebApplication app)
        {
            try
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error("AC Cloud Mule: error disposing the world-boundary liveness endpoint host.", ex);
            }
        }
    }
}
