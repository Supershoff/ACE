namespace ACE.Cloud.Hosting;

/// <summary>
/// The HTTP header name every private-network Cloud Mule host uses to carry a
/// <c>CloudPrivateServiceRequestAuthenticator</c> signature (security baseline: "Private-service
/// authentication between Cloud backend, Auth Bridge, and ACE boundary endpoints"). Shared so the
/// Auth Bridge (which validates it) and the Cloud backend (which sends it) never drift onto
/// different header names.
/// </summary>
public static class CloudPrivateServiceHeaders
{
    public const string SignatureHeaderName = "X-Cloud-Service-Signature";
}
