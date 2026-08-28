namespace ACE.Cloud.Domain;

/// <summary>
/// Pure decision rule for VAULT-005: "ACE blocks deletion of a monarch character while that
/// monarch's Allegiance Vault is nonempty" (CONTEXT.md line 406). Deleting a monarch character can
/// split their allegiance among several new monarchs (each vassal with vassals of their own becomes
/// a monarch in turn), which would leave a nonempty vault with no single successor -- this guard
/// exists precisely so ACE never has to guess one (CONTEXT.md line 407: "do not guess a successor
/// vault"). Ordinary (non-monarch) character deletion, and monarch deletion once the vault has been
/// emptied by its members, are both always allowed.
/// </summary>
public static class CloudMonarchDeletionGuard
{
    /// <summary>
    /// <paramref name="isMonarch"/> is the character's own live ACE allegiance state (whether they
    /// currently lead an allegiance); <paramref name="vaultIsEmpty"/> is whether their Allegiance
    /// Vault owner identity (<see cref="CloudOwnerIdentity.ForAllegianceVault"/>) currently has any
    /// Cloud Custody Records or Cloud Stack Lots. Only the combination of both being true/false
    /// respectively blocks deletion; every other combination is allowed.
    /// </summary>
    public static CloudMonarchDeletionDecision Evaluate(bool isMonarch, bool vaultIsEmpty)
    {
        if (isMonarch && !vaultIsEmpty)
        {
            return CloudMonarchDeletionDecision.Block(
                "This character leads an allegiance whose Allegiance Vault is not empty. The vault must be "
                    + "emptied by its members before this monarch can be deleted.");
        }

        return CloudMonarchDeletionDecision.Allow();
    }
}
