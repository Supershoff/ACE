namespace ACE.Cloud.Domain;

/// <summary>
/// Pure precondition check for VAULT-005's audited administrator recovery of an out-of-band monarch
/// deletion (CONTEXT.md line 407: "An out-of-band monarch deletion leaves the vault available only
/// for audited administrator recovery") together with ADM-002's own general administrator
/// intervention contract ("requires a written reason and delayed confirmation"). This never chooses
/// -- or suggests -- a destination itself (VAULT-005: "do not guess a successor vault"): the
/// destination is always whatever the administrator explicitly typed. It does still refuse an
/// obviously-empty or self-referential destination, and one that does not correspond to a real ACE
/// account (<see cref="CloudMonarchVaultRecoveryRejectionCode.DestinationAccountNotFound"/>) --
/// otherwise a single typo would permanently and irreversibly reassign the vault's contents, since a
/// committed recovery can never be re-applied. The actual item-by-item transfer is the persistence
/// layer's job, exactly like <see cref="CloudAllegianceVaultAbsorptionPolicy"/>, whose precedence
/// order (authorization/gate first, then request-shape facts) this mirrors.
/// </summary>
public static class CloudMonarchVaultRecoveryPolicy
{
    public static CloudMonarchVaultRecoveryResult Authorize(CloudMonarchVaultRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AdminAuthorized)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.Unauthorized,
                "This action requires a freshly revalidated ACE administrator (accessLevel 5).");
        }

        if (request.GateState == CloudMutationGateState.Frozen)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (!request.DiagnosticFound)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.DiagnosticNotFound,
                "No unresolved out-of-band monarch deletion diagnostic matches this request.");
        }

        if (request.AlreadyResolved)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.AlreadyResolved,
                "This Allegiance Vault recovery was already committed by an administrator decision and cannot be overridden.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.ReasonRequired,
                "An administrator Allegiance Vault recovery requires a written reason.");
        }

        if (!request.Confirmed)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.NotConfirmed,
                "An administrator Allegiance Vault recovery requires explicit delayed confirmation.");
        }

        if (request.DestinationOwnerId == Guid.Empty || request.DestinationOwnerId == request.SourceVaultOwnerId)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.InvalidDestination,
                "An administrator Allegiance Vault recovery requires a real destination different from the orphaned vault itself.");
        }

        if (!request.DestinationAccountExists)
        {
            return CloudMonarchVaultRecoveryResult.Failure(
                CloudMonarchVaultRecoveryRejectionCode.DestinationAccountNotFound,
                "The administrator-chosen destination account does not exist.");
        }

        return CloudMonarchVaultRecoveryResult.Success();
    }
}
