namespace ACE.Cloud.Domain;

/// <summary>
/// The authorization context a Live State Stream reader evaluates a candidate event against
/// (EVT-007, security baseline: "Search indexes and live streams must be scoped before data leaves
/// the server"). <see cref="AuthorizedOwnerIds"/> is deliberately supplied by the caller rather than
/// computed here: building "every owner ID this viewer may currently see" is the composition of the
/// viewer's own Main/Linked ownership group plus any live Sharing Grants (SHARE-001..004), which
/// belongs to the modules that own that authority, not to the stream itself. This type only asks
/// that the caller revalidate and pass in the current set on every request (ADM-001's "revalidate on
/// every sensitive request" discipline applied to reads), so a revoked grant or unlinked account stops
/// appearing in the very next read without this type needing to know why.
/// </summary>
public sealed class CloudLiveStreamViewer
{
    private readonly HashSet<Guid> _authorizedOwnerIds;

    private CloudLiveStreamViewer(bool isAdmin, IEnumerable<Guid> authorizedOwnerIds)
    {
        IsAdmin = isAdmin;
        _authorizedOwnerIds = [.. authorizedOwnerIds];
    }

    /// <summary>ADM-001: an ACE administrator (access level 5) may see every private event.</summary>
    public bool IsAdmin { get; }

    public IReadOnlySet<Guid> AuthorizedOwnerIds => _authorizedOwnerIds;

    /// <summary>An unauthenticated viewer: sees only public events (MKT-201).</summary>
    public static CloudLiveStreamViewer Anonymous() => new(isAdmin: false, authorizedOwnerIds: []);

    /// <summary>
    /// An authenticated viewer currently authorized to see private events scoped to any of
    /// <paramref name="authorizedOwnerIds"/> (their own ownership group, plus any Main/Linked account
    /// currently sharing View access with them).
    /// </summary>
    public static CloudLiveStreamViewer ForOwners(IEnumerable<Guid> authorizedOwnerIds)
    {
        ArgumentNullException.ThrowIfNull(authorizedOwnerIds);
        return new CloudLiveStreamViewer(isAdmin: false, authorizedOwnerIds);
    }

    /// <summary>A revalidated ACE administrator: sees every public and private event (ADM-001).</summary>
    public static CloudLiveStreamViewer ForAdmin() => new(isAdmin: true, authorizedOwnerIds: []);
}
