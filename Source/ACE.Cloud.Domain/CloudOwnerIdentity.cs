using System.Security.Cryptography;
using System.Text;

namespace ACE.Cloud.Domain;

/// <summary>
/// Derives the opaque Cloud ownership identities a Cloud Custodian deposit needs (ARCH-002,
/// ARCH-006) until the Cloud authority's own Main/Linked Account and OwnershipGroup workstream
/// (IMPLEMENTATION-BRIEF.md AUTH-001..010) exists. Both derivations are pure functions of stable
/// ACE-side identifiers, so every deposit for the same account or the same native biota always
/// resolves to the same Cloud identity, without any additional ace_cloud lookup. Kept in this pure
/// domain project rather than ACE.Server (ARCH-012): it has no ACE.Server dependency, and the
/// self-contained Cloud Mule CI test-discovery filter only runs test projects whose name matches
/// "Cloud" (see .github/workflows/cloud-mule-ci.yml), which ACE.Server.Tests never will.
/// </summary>
public static class CloudOwnerIdentity
{
    /// <summary>
    /// A stable per-account Cloud owner ID (deterministic; never random). Every deposit from this
    /// ACE account, on this shard, resolves to the same <see cref="Guid"/>, so account-scoped
    /// custody accumulates correctly even before real OwnershipGroup/linking exists. This is a
    /// placeholder identity, not a substitute for AUTH-001..010: it does not merge Main/Linked
    /// accounts and must be superseded, not merely wrapped, once that workstream lands.
    /// </summary>
    public static Guid ForAccount(string shardId, uint accountId) =>
        DeterministicGuid($"ACE.Cloud.OwnerAccount:{shardId}:{accountId}");

    /// <summary>
    /// A stable idempotency key for depositing exactly this native biota (ARCH-006, transaction
    /// rule 4: "repeating a request must produce the same result, not another item"). A native
    /// biota can only ever be deposited once -- after a successful deposit it no longer has world
    /// possession, so it can never again resolve as a candidate row -- so keying off the biota ID
    /// rather than a fresh random value per attempt means any duplicate/replayed deposit attempt
    /// for the same biota always replays the original committed outcome instead of creating a
    /// second Cloud Custody Record.
    /// </summary>
    public static Guid DepositIdempotencyKey(string shardId, uint biotaId) =>
        DeterministicGuid($"ACE.Cloud.CustodianDeposit:{shardId}:{biotaId}");

    private static Guid DeterministicGuid(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }
}
