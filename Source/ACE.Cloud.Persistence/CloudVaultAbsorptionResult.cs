namespace ACE.Cloud.Persistence;

/// <summary>
/// The outcome of <see cref="CloudAllegianceVaultGateway.AbsorbAsync"/>: how many
/// whole-item Cloud Custody Records and Cloud Stack Lot claims moved from the former monarch's
/// Allegiance Vault to the new monarch's (VAULT-004). Both may legitimately be zero when the source
/// vault was already empty -- absorbing an empty vault is a valid no-op, not an error.
/// </summary>
public sealed record CloudVaultAbsorptionResult(int CustodyRecordsMoved, int StackLotsMoved)
{
    public int TotalItemsMoved => CustodyRecordsMoved + StackLotsMoved;
}
