using ACE.Cloud.AuthBridge;
using ACE.Cloud.Domain;

namespace ACE.Cloud.AuthBridge.Tests;

/// <summary>An in-memory <see cref="IAceAuthAccountReader"/> substitute, so endpoint tests never need a real MariaDB.</summary>
internal sealed class FakeAceAuthAccountReader : IAceAuthAccountReader
{
    private readonly Dictionary<string, CloudAceAccountSnapshot> _accountsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, CloudAceAccountSnapshot> _accountsById = new();

    public void Add(CloudAceAccountSnapshot account)
    {
        _accountsByName[account.AccountName] = account;
        _accountsById[account.AccountId] = account;
    }

    public Task<CloudAceAccountSnapshot?> FindByAccountNameAsync(string accountName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accountsByName.GetValueOrDefault(accountName));

    public Task<CloudAceAccountSnapshot?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accountsById.GetValueOrDefault(accountId));
}
