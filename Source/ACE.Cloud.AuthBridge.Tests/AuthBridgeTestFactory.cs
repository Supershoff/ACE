using ACE.Cloud.AuthBridge;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ACE.Cloud.AuthBridge.Tests;

/// <summary>
/// Hosts the real Auth Bridge <c>Program</c> in-memory with its DB-backed
/// <see cref="IAceAuthAccountReader"/> replaced by <see cref="FakeAceAuthAccountReader"/>, so
/// endpoint tests exercise real routing/middleware/domain logic without requiring MariaDB.
///
/// Configuration is supplied via process environment variables set in the static constructor,
/// rather than <c>IWebHostBuilder.ConfigureAppConfiguration</c>: Program.cs reads
/// <c>AuthBridgeOptions</c> from <c>builder.Configuration</c> before <c>builder.Build()</c> (so it
/// can fail fast on missing configuration), and <c>WebApplicationFactory</c>'s
/// <c>ConfigureAppConfiguration</c> hook only takes effect once the underlying host builder runs --
/// too late for that early read to observe it. Environment variables are already present by the
/// time <c>WebApplication.CreateBuilder(args)</c> runs, so they are visible to that early read.
/// </summary>
internal sealed class AuthBridgeTestFactory : WebApplicationFactory<Program>
{
    public const string ActiveServiceKeyId = "test-active-key";
    public const string ActiveServiceKeySecretBase64 = "dGVzdC1hY3RpdmUtc2VjcmV0LTMyLWJ5dGVzLWxvbmc=";

    static AuthBridgeTestFactory()
    {
        Environment.SetEnvironmentVariable("AuthBridge__AceAuthConnectionString", "Server=unused;Database=ace_auth;");
        Environment.SetEnvironmentVariable("AuthBridge__ComponentVersion", "test");
        Environment.SetEnvironmentVariable("AuthBridge__WorldBoundaryHealthEndpoint", "http://127.0.0.1:1/health/live");
        Environment.SetEnvironmentVariable("AuthBridge__GrantTimeToLiveSeconds", "30");
        Environment.SetEnvironmentVariable("AuthBridge__ActiveServiceKeyId", ActiveServiceKeyId);
        Environment.SetEnvironmentVariable("AuthBridge__ActiveServiceKeySecret", ActiveServiceKeySecretBase64);
        Environment.SetEnvironmentVariable("AuthBridge__PreviousServiceKeyId", "");
        Environment.SetEnvironmentVariable("AuthBridge__PreviousServiceKeySecret", "");
        Environment.SetEnvironmentVariable("AuthBridge__PrivateServiceRequestMaxClockSkewSeconds", "30");
        Environment.SetEnvironmentVariable("AuthBridge__MaxLoginAttemptsPerWindow", "3");
        Environment.SetEnvironmentVariable("AuthBridge__LoginRateLimitWindowSeconds", "60");
    }

    public FakeAceAuthAccountReader AccountReader { get; } = new();

    public CapturingLoggerProvider CapturedLogs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(CapturedLogs));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAceAuthAccountReader>();
            services.AddSingleton<IAceAuthAccountReader>(AccountReader);
        });
    }
}
