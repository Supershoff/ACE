using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Representative public Live State Stream / Public Marketplace payload (MKT-201): a listing's
/// publicly visible state through its seller's Display Character. Deliberately carries no ACE
/// account name, no maximum bid, no credential, and no private ledger detail, and is proven so by
/// the public-contract privacy tests, not merely by convention.
/// </summary>
public sealed record CloudListingPublicSnapshot : ICloudPublicContract
{
    public CloudShardId ShardId { get; }

    public string SellerDisplayCharacter { get; }

    public int CurrentPriceUnits { get; }

    public CloudListingPublicSnapshot(CloudShardId shardId, string sellerDisplayCharacter, int currentPriceUnits)
    {
        ArgumentNullException.ThrowIfNull(shardId);

        if (string.IsNullOrWhiteSpace(sellerDisplayCharacter))
        {
            throw new ArgumentException("A public listing snapshot requires the seller's Display Character.", nameof(sellerDisplayCharacter));
        }

        if (currentPriceUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPriceUnits), "A public listing snapshot requires a positive current price.");
        }

        ShardId = shardId;
        SellerDisplayCharacter = sellerDisplayCharacter;
        CurrentPriceUnits = currentPriceUnits;
    }
}
