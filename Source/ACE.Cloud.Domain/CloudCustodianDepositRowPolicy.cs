namespace ACE.Cloud.Domain;

/// <summary>
/// The single pure decision for one Cloud Custodian sell-window row (DEP-002: "Rows use familiar
/// vendor partial success: validate and commit each independently. Valid rows deposit even when
/// other rows fail."). Reuses <see cref="CloudItemEligibilityPolicy"/> for the DEP-003/DEP-004
/// eligibility corpus and adds the batch-shape rules (duplicate rows, invalid/mismatched quantities,
/// a stale sell window) that are specific to a Custodian sale rather than eligibility itself. Kept
/// independent of ACE.Server so every row-decision case can run in a unit test without a live
/// landblock (ARCH-012).
/// </summary>
public static class CloudCustodianDepositRowPolicy
{
    public static CloudCustodianDepositRowDecision Decide(
        CloudCustodianDepositRowRequest request, CloudCustodianSaleWindowValidation saleWindow)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(saleWindow);

        if (!saleWindow.IsCurrent)
        {
            return CloudCustodianDepositRowDecision.Reject(request.ItemId, saleWindow.StaleReason!);
        }

        if (request.IsDuplicateInSubmission)
        {
            return CloudCustodianDepositRowDecision.Reject(request.ItemId, "That item was already included in this deposit.");
        }

        if (request.SubmittedAmount <= 0)
        {
            return CloudCustodianDepositRowDecision.Reject(request.ItemId, "That is not a valid quantity to deposit.");
        }

        // Cloud Custodian deposits are all-or-nothing per submitted WorldObject instance: a player
        // who wants to deposit part of a stack must first split it (ordinary ACE stack-split, unrelated
        // to Cloud code) into its own instance before opening the sell window, exactly like selling a
        // partial stack to an ordinary vendor already requires. This policy never partially consumes
        // a live stack itself.
        if (request.SubmittedAmount != request.CurrentStackSize)
        {
            return CloudCustodianDepositRowDecision.Reject(
                request.ItemId, "The quantity submitted does not match that item's current stack size.");
        }

        var eligibility = CloudItemEligibilityPolicy.Evaluate(request.Snapshot);

        if (!eligibility.IsEligible)
        {
            return CloudCustodianDepositRowDecision.Reject(request.ItemId, eligibility.PlayerMessage!, eligibility.RejectionCode);
        }

        return request.IsStackable
            ? CloudCustodianDepositRowDecision.DepositStack(request.ItemId, request.CurrentStackSize, eligibility.PreservationRequirements)
            : CloudCustodianDepositRowDecision.DepositWhole(request.ItemId, eligibility.PreservationRequirements);
    }
}
