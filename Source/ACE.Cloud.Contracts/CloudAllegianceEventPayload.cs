using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// An allegiance membership change published from an authoritative ACE seam (VAULT-001, ARCH-007):
/// swearing in, breaking away, or a monarch change (including the vassals-become-monarchs case a
/// monarch's own deletion can produce). The companion web uses this to refresh which Acting
/// Characters currently have Allegiance Vault access without maintaining a parallel membership
/// roster of its own (CONTEXT.md: "Membership derives live from ACE's allegiance tree. Do not create
/// a parallel guild roster."). Carries no <see cref="ICloudPublicContract"/> marker.
/// </summary>
public sealed record CloudAllegianceEventPayload
{
    public uint CharacterId { get; }

    public CloudIdentityEventType EventType { get; }

    /// <summary>This character's monarch immediately after the event, or null if they now have none.</summary>
    public uint? MonarchId { get; }

    /// <summary>This character's monarch immediately before the event, or null if they had none.</summary>
    public uint? PriorMonarchId { get; }

    public CloudAllegianceEventPayload(uint characterId, CloudIdentityEventType eventType, uint? monarchId, uint? priorMonarchId)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "An allegiance event requires a real character GUID.");
        }

        if (eventType != CloudIdentityEventType.AllegianceSworn
            && eventType != CloudIdentityEventType.AllegianceBroken
            && eventType != CloudIdentityEventType.AllegianceMonarchChanged)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not an allegiance event type.");
        }

        CharacterId = characterId;
        EventType = eventType;
        MonarchId = monarchId;
        PriorMonarchId = priorMonarchId;
    }
}
