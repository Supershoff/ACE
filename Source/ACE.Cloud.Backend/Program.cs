using ACE.Cloud.Backend;
using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var backendOptions = builder.Configuration.GetSection(CloudBackendOptions.SectionName).Get<CloudBackendOptions>()
    ?? throw new InvalidOperationException($"Missing required configuration section '{CloudBackendOptions.SectionName}'.");

var expectedVersions = new CloudComponentVersions(
    backendOptions.ExpectedAceExtensionVersion, CloudSchemaInfo.CurrentVersion, backendOptions.ExpectedContractProtocolVersion);

builder.Services.AddDbContext<CloudDbContext>(dbContextOptions => dbContextOptions.UseMySql(
    backendOptions.CloudConnectionString, new MariaDbServerVersion(backendOptions.DatabaseServerVersion)));
builder.Services.AddScoped<CloudGatewayDiagnostics>();

builder.Services.AddHttpClient<ICloudWorldBoundaryHealthProbe, HttpCloudWorldBoundaryHealthProbe>();
builder.Services.AddSingleton(new CloudWorldBoundaryProbeOptions { HealthEndpoint = backendOptions.WorldBoundaryHealthEndpoint });
builder.Services.AddScoped(serviceProvider =>
{
    var gatewayDiagnostics = serviceProvider.GetRequiredService<CloudGatewayDiagnostics>();
    var worldBoundaryProbe = serviceProvider.GetRequiredService<ICloudWorldBoundaryHealthProbe>();

    return new CloudStartupDiagnosticsService(
    [
        CloudStartupChecks.Database(gatewayDiagnostics),
        CloudStartupChecks.ShardIdentity(gatewayDiagnostics),
        CloudStartupChecks.SchemaAndProtocolCompatibility(gatewayDiagnostics, expectedVersions),
        CloudStartupChecks.WorldBoundary(worldBoundaryProbe),
    ]);
});

var app = builder.Build();

app.MapCloudDiagnosticsEndpoints(expectedVersions);

app.Run();
