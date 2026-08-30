using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Issue #28's local-only fixture-generation tooling: `dotnet run --project Source/ACE.Cloud.Worker --
// generate-icon-fixture ...` (see docs/agents/fidelity-phase-gate.md). Dispatches and exits before any
// of the hosted worker's database/configuration requirements below are ever touched; ordinary worker
// startup (no arguments) is unaffected.
if (args.Length > 0 && CloudFixtureGeneratorCli.KnownCommands.Contains(args[0]))
{
    return await CloudFixtureGeneratorCli.RunAsync(args, Console.Out, Console.Error);
}

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

var assetStorageOptions = new CloudAssetStorageOptions
{
    RootDirectory = workerOptions.AssetStorageRootDirectory,
    MaxTotalBytes = workerOptions.AssetStorageMaxTotalBytes,
    MaxChunkSizeBytes = workerOptions.AssetStorageMaxChunkSizeBytes,
};
builder.Services.AddSingleton(assetStorageOptions);
builder.Services.AddSingleton<IProtectedAssetBlobStore>(new LocalProtectedAssetBlobStore(assetStorageOptions));
builder.Services.AddSingleton<IPortalDatAssetExtractor, PortalDatAssetExtractor>();
builder.Services.AddScoped(serviceProvider => new CloudAssetImportBoundary(
    serviceProvider.GetRequiredService<CloudDbContext>(),
    serviceProvider.GetRequiredService<IProtectedAssetBlobStore>(),
    serviceProvider.GetRequiredService<CloudAssetStorageOptions>()));
builder.Services.AddHostedService<CloudAssetImportStagingWorker>();

builder.Services.AddHostedService<CloudCustodyProjectionConsumerWorker>();
builder.Services.AddHostedService<CloudIdentityProjectionConsumerWorker>();
builder.Services.AddHostedService<CloudNotificationProjectionConsumerWorker>();

var host = builder.Build();
host.Run();
return 0;
