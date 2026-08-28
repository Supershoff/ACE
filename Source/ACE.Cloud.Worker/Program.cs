using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<CloudWorkerOptions>(builder.Configuration.GetSection(CloudWorkerOptions.SectionName));

var workerOptions = builder.Configuration.GetSection(CloudWorkerOptions.SectionName).Get<CloudWorkerOptions>()
    ?? throw new InvalidOperationException($"Missing required configuration section '{CloudWorkerOptions.SectionName}'.");

var expectedVersions = new CloudComponentVersions(
    workerOptions.ExpectedAceExtensionVersion, CloudSchemaInfo.CurrentVersion, workerOptions.ExpectedContractProtocolVersion);

builder.Services.AddDbContext<CloudDbContext>(dbContextOptions => dbContextOptions.UseMySql(
    workerOptions.CloudConnectionString, new MariaDbServerVersion(workerOptions.DatabaseServerVersion)));
builder.Services.AddScoped<CloudGatewayDiagnostics>();

builder.Services.AddHttpClient<ICloudWorldBoundaryHealthProbe, HttpCloudWorldBoundaryHealthProbe>();
builder.Services.AddSingleton(new CloudWorldBoundaryProbeOptions { HealthEndpoint = workerOptions.WorldBoundaryHealthEndpoint });
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

builder.Services.AddHostedService<CloudWorkerDiagnosticsHostedService>();

var host = builder.Build();
host.Run();
