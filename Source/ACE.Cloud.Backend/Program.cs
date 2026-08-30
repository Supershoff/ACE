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

builder.Services.AddSingleton(backendOptions);

builder.Services.AddDbContext<CloudDbContext>(dbContextOptions => dbContextOptions.UseMySql(
    backendOptions.CloudConnectionString, new MariaDbServerVersion(backendOptions.DatabaseServerVersion)));
builder.Services.AddScoped<CloudGatewayDiagnostics>();
builder.Services.AddScoped<ICloudWebSessionStore>(serviceProvider => new CloudSessionGateway(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<CloudAccountLinkGateway>(serviceProvider => new CloudAccountLinkGateway(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudAccountOwnershipResolver>(serviceProvider => serviceProvider.GetRequiredService<CloudAccountLinkGateway>());
builder.Services.AddScoped<ICloudAccountLinkGateway>(serviceProvider => serviceProvider.GetRequiredService<CloudAccountLinkGateway>());
builder.Services.AddScoped<ICloudDisplayCharacterGateway>(serviceProvider => new CloudDisplayCharacterGateway(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudCharacterIdentityReader>(serviceProvider => new CloudCharacterIdentityReader(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudWithdrawalReservationGateway>(serviceProvider => new CloudWithdrawalReservationGateway(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudStackLotSplitGateway>(serviceProvider => new CloudStackLotTransactionAuthority(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudInventoryQueryReader>(serviceProvider => new CloudInventoryQueryReader(serviceProvider.GetRequiredService<CloudDbContext>()));
builder.Services.AddScoped<ICloudInventoryItemPropertiesGateway>(serviceProvider => new CloudInventoryItemPropertiesGateway(serviceProvider.GetRequiredService<CloudDbContext>()));

var protectedAssetStorageOptions = new CloudAssetStorageOptions { RootDirectory = backendOptions.ProtectedAssetStorageRootDirectory };
builder.Services.AddSingleton<IProtectedAssetBlobStore>(new LocalProtectedAssetBlobStore(protectedAssetStorageOptions));
builder.Services.AddSingleton<ICloudIconDerivativeReader>(serviceProvider => new CloudIconDerivativeReader(serviceProvider.GetRequiredService<IProtectedAssetBlobStore>()));

builder.Services.AddSingleton(_ => CloudPrivateServiceKeyRingFactory.Create(
    backendOptions.ActiveServiceKeyId, backendOptions.ActiveServiceKeySecret,
    backendOptions.PreviousServiceKeyId, backendOptions.PreviousServiceKeySecret));
builder.Services.AddSingleton(_ => new CloudLoginAttemptRateLimiter(
    backendOptions.MaxLoginAttemptsPerWindow, TimeSpan.FromSeconds(backendOptions.LoginRateLimitWindowSeconds)));
builder.Services.AddSingleton(_ => new CloudAccountLinkAttemptRateLimiter(
    backendOptions.MaxLoginAttemptsPerWindow, TimeSpan.FromSeconds(backendOptions.LoginRateLimitWindowSeconds)));

builder.Services.AddHttpClient<ICloudAuthBridgeClient, HttpCloudAuthBridgeClient>(
    httpClient => httpClient.BaseAddress = backendOptions.AuthBridgeBaseAddress);

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
app.MapAuthSessionEndpoints();
app.MapCloudInventoryEndpoints();
app.MapAccountEndpoints();
app.MapWithdrawalEndpoints();

app.Run();

// Exposed so ACE.Cloud.Backend.Tests can host this app in-memory via WebApplicationFactory<Program>.
public partial class Program;
