namespace ACE.Cloud.Domain;

/// <summary>
/// Pure account-linking eligibility (AUTH-005, AUTH-006, AUTH-009): whether one ownership group
/// may absorb a standalone source account, or release an already-Linked Account. Carries no
/// database access of its own; <c>CloudAccountLinkGateway</c> gathers every input fact under its
/// own locked commit-time revalidation and applies these exact rules there (transaction rule 9).
/// </summary>
public static class CloudAccountLinkPolicy
{
    /// <summary>
    /// Evaluates one link attempt. Checks run in a fixed precedence so retrying an identical,
    /// still-illegal request always reports the same exact reason (AUTH-006, AUTH-009): the
    /// mutation gate first, then the two same-account/tree-merge shapes that make the request
    /// nonsensical independent of any obligation state, then the source's own standalone and
    /// obligation requirements, and finally the marketplace self-dealing check.
    /// </summary>
    public static CloudAccountLinkDecision EvaluateLink(CloudAccountLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MutationGateState == CloudMutationGateState.Frozen)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.MutationsFrozen);
        }

        if (request.MainAccountId == request.SourceAccountId)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.SameAccount);
        }

        if (request.MainAccountIsLinkedElsewhere)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.MainAccountIsLinkedElsewhere);
        }

        if (request.SourceIsAlreadyLinked)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.SourceAlreadyLinked);
        }

        if (request.SourceHasLinkedAccounts)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.SourceHasLinkedAccounts);
        }

        if (request.SourceHasPendingObligations)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.SourceHasPendingObligations);
        }

        if (request.WouldCreateActiveAuctionConflict)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.WouldCreateAuctionConflict);
        }

        return CloudAccountLinkDecision.Approved();
    }

    /// <summary>
    /// Evaluates one unlink attempt: the mutation gate must be open, and the named link must
    /// currently be active. Unlinking never inspects pending obligations or auction conflicts --
    /// AUTH-005 permits unlinking unconditionally once the gate and link-state checks pass, since
    /// unlinking only stops future routing rather than transferring anything.
    /// </summary>
    public static CloudAccountLinkDecision EvaluateUnlink(bool linkIsActive, CloudMutationGateState mutationGateState)
    {
        if (mutationGateState == CloudMutationGateState.Frozen)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.MutationsFrozen);
        }

        if (!linkIsActive)
        {
            return CloudAccountLinkDecision.Rejected(CloudAccountLinkRejectionCode.LinkNotActive);
        }

        return CloudAccountLinkDecision.Approved();
    }
}
