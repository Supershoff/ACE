using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>The committed result of one <c>CloudAccountLinkGateway.LinkAsync</c>/<c>UnlinkAsync</c> call.</summary>
public sealed record CloudAccountLinkOutcome
{
    public bool IsApproved { get; }

    public CloudAccountLinkRejectionCode RejectionCode { get; }

    public Guid? AccountLinkId { get; }

    public Guid? OwnershipGroupId { get; }

    private CloudAccountLinkOutcome(bool isApproved, CloudAccountLinkRejectionCode rejectionCode, Guid? accountLinkId, Guid? ownershipGroupId)
    {
        IsApproved = isApproved;
        RejectionCode = rejectionCode;
        AccountLinkId = accountLinkId;
        OwnershipGroupId = ownershipGroupId;
    }

    public static CloudAccountLinkOutcome Approved(Guid accountLinkId, Guid ownershipGroupId) =>
        new(true, CloudAccountLinkRejectionCode.None, accountLinkId, ownershipGroupId);

    public static CloudAccountLinkOutcome Rejected(CloudAccountLinkRejectionCode rejectionCode) =>
        new(false, rejectionCode, accountLinkId: null, ownershipGroupId: null);
}
