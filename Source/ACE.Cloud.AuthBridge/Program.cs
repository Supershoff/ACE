using ACE.Cloud.AuthBridge;
using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;

var builder = WebApplication.CreateBuilder(args);

var authBridgeOptions = builder.Configuration.GetSection(AuthBridgeOptions.SectionName).Get<AuthBridgeOptions>()
    ?? throw new InvalidOperationException($"Missing required configuration section '{AuthBridgeOptions.SectionName}'.");

// The Auth Bridge has no Cloud schema of its own (AUTH-002), so its "/version" handshake simply
// echoes its own build version in every field rather than reporting an unrelated Cloud schema/ACE
// extension version triplet.
var selfVersion = new CloudComponentVersions(
    authBridgeOptions.ComponentVersion, authBridgeOptions.ComponentVersion, authBridgeOptions.ComponentVersion);

builder.Services.AddHttpClient<ICloudWorldBoundaryHealthProbe, HttpCloudWorldBoundaryHealthProbe>();
builder.Services.AddSingleton(new CloudWorldBoundaryProbeOptions { HealthEndpoint = authBridgeOptions.WorldBoundaryHealthEndpoint });
builder.Services.AddScoped(serviceProvider =>
{
    var worldBoundaryProbe = serviceProvider.GetRequiredService<ICloudWorldBoundaryHealthProbe>();

    return new CloudStartupDiagnosticsService(
    [
        CloudStartupChecks.RawConnectionAvailability(authBridgeOptions.AceAuthConnectionString),
        CloudStartupChecks.WorldBoundary(worldBoundaryProbe),
    ]);
});

var app = builder.Build();

app.MapCloudDiagnosticsEndpoints(selfVersion);

app.Run();
