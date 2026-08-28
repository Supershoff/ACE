using ACE.Cloud.Domain;

namespace ACE.Cloud.AuthBridge;

/// <summary>Seam for reading <c>ace_auth.account</c> rows, so endpoint tests can substitute a fake instead of a real database.</summary>
public interface IAceAuthAccountReader
{
    Task<CloudAceAccountSnapshot?> FindByAccountNameAsync(string accountName, CancellationToken cancellationToken = default);

    Task<CloudAceAccountSnapshot?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default);
}
