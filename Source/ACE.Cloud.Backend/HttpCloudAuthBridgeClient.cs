using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;

namespace ACE.Cloud.Backend;

/// <summary>
/// Calls the private ACE Auth Bridge's internal endpoints over HTTP, signing every request with
/// this deployment's <see cref="CloudPrivateServiceKeyRing"/> (security baseline: "Private-service
/// authentication between Cloud backend, Auth Bridge, and ACE boundary endpoints"). Any connectivity
/// failure -- the Auth Bridge process down, DNS failure, timeout -- surfaces as
/// <see cref="CloudAuthBridgeGrantOutcomeKind.Unavailable"/> rather than throwing, so the login
/// endpoint can report a clean 503 instead of an unhandled exception.
/// </summary>
public sealed class HttpCloudAuthBridgeClient : ICloudAuthBridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly CloudPrivateServiceKeyRing _keyRing;

    public HttpCloudAuthBridgeClient(HttpClient httpClient, CloudPrivateServiceKeyRing keyRing)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
    }

    public async Task<CloudAuthBridgeGrantResult> IssueGrantAsync(
        string accountName, string password, string audience, CancellationToken cancellationToken = default)
    {
        const string path = "/internal/auth/grants";

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { accountName, password, audience }, options: JsonOptions),
        };
        Sign(request, "POST", path);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Unavailable, Grant: null);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await response.Content.ReadFromJsonAsync<IssueGrantResponseDto>(JsonOptions, cancellationToken);
                    return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, body!.Grant);
                case HttpStatusCode.Forbidden:
                    return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.AccountBanned, Grant: null);
                case HttpStatusCode.TooManyRequests:
                    return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.RateLimited, Grant: null);
                case HttpStatusCode.Unauthorized:
                    return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);
                default:
                    return new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Unavailable, Grant: null);
            }
        }
    }

    public async Task<uint?> GetFreshAccessLevelAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        var path = $"/internal/auth/access-level/{accountId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Sign(request, "GET", path);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<AccessLevelResponseDto>(JsonOptions, cancellationToken);
            return body?.AccessLevel;
        }
    }

    private void Sign(HttpRequestMessage request, string method, string path) =>
        request.Headers.Add(
            CloudPrivateServiceHeaders.SignatureHeaderName,
            CloudPrivateServiceRequestAuthenticator.Sign(method, path, DateTime.UtcNow, _keyRing));

    private sealed record IssueGrantResponseDto(string Grant, DateTime ExpiresAtUtc, uint AccountId, uint AccessLevel);

    private sealed record AccessLevelResponseDto(uint AccountId, uint AccessLevel);
}
