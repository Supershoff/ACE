using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Hosts the real Cloud backend <c>Program</c> in-memory with its DB-backed
/// <see cref="ICloudWebSessionStore"/> and HTTP-backed <see cref="ICloudAuthBridgeClient"/> replaced
/// by in-memory fakes, so endpoint tests exercise real routing/middleware/domain logic (cookie
/// flags, CSRF, origin checks, ADM-001 revalidation) without requiring MariaDB or a real Auth
/// Bridge process.
///
/// Configuration is supplied via process environment variables set in the static constructor -- see
/// AuthBridgeTestFactory's doc comment in the sibling AuthBridge test project for why
/// ConfigureAppConfiguration cannot be used instead (Program.cs reads options from
/// builder.Configuration before builder.Build()).
/// </summary>
internal sealed class BackendTestFactory : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://cloud.example.test";

    static BackendTestFactory()
    {
        Environment.SetEnvironmentVariable("CloudBackend__CloudConnectionString", "Server=unused;Database=ace_cloud;");
        Environment.SetEnvironmentVariable("CloudBackend__ExpectedAceExtensionVersion", "test");
        Environment.SetEnvironmentVariable("CloudBackend__ExpectedContractProtocolVersion", "test");
        Environment.SetEnvironmentVariable("CloudBackend__WorldBoundaryHealthEndpoint", "http://127.0.0.1:1/health/live");
        Environment.SetEnvironmentVariable("CloudBackend__ShardId", "us1");
        Environment.SetEnvironmentVariable("CloudBackend__AuthBridgeBaseAddress", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("CloudBackend__ActiveServiceKeyId", "test-active-key");
        Environment.SetEnvironmentVariable("CloudBackend__ActiveServiceKeySecret", "dGVzdC1hY3RpdmUtc2VjcmV0LTMyLWJ5dGVzLWxvbmc=");
        Environment.SetEnvironmentVariable("CloudBackend__SessionTimeToLiveMinutes", "60");
        Environment.SetEnvironmentVariable("CloudBackend__SessionCookieName", "ace_cloud_session");
        Environment.SetEnvironmentVariable("CloudBackend__AllowedOrigins__0", AllowedOrigin);
        Environment.SetEnvironmentVariable("CloudBackend__MaxLoginAttemptsPerWindow", "3");
        Environment.SetEnvironmentVariable("CloudBackend__LoginRateLimitWindowSeconds", "60");
    }

    public FakeCloudAuthBridgeClient AuthBridgeClient { get; } = new();

    public FakeCloudWebSessionStore SessionStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICloudAuthBridgeClient>();
            services.AddSingleton<ICloudAuthBridgeClient>(AuthBridgeClient);

            services.RemoveAll<ICloudWebSessionStore>();
            services.AddSingleton<ICloudWebSessionStore>(SessionStore);
        });
    }
}
