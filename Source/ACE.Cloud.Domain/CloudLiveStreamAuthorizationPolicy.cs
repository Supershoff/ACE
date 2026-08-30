namespace ACE.Cloud.Domain;

/// <summary>
/// Whether a given <see cref="CloudLiveStreamViewer"/> may receive one Live State Stream event
/// (EVT-007, MKT-201). A public event (the not-yet-built Marketplace's published-listing/settlement
/// stream) is visible to everyone, including an anonymous viewer, exactly like the Public Marketplace
/// itself requires no login. A private event (every custody/inventory change this issue's outbox
/// consumers actually publish today) is visible only to an authorized owner or a revalidated
/// administrator -- this is the "scoped before data leaves the server" enforcement point: a caller
/// must never filter private events client-side after already receiving them.
/// </summary>
public static class CloudLiveStreamAuthorizationPolicy
{
    public static bool IsVisibleTo(bool isPublic, Guid? scopeOwnerId, CloudLiveStreamViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (isPublic)
        {
            return true;
        }

        if (scopeOwnerId is null)
        {
            throw new ArgumentException("A private Live State Stream event requires a scope owner.", nameof(scopeOwnerId));
        }

        return viewer.IsAdmin || viewer.AuthorizedOwnerIds.Contains(scopeOwnerId.Value);
    }
}
