using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// The Notification Center HTTP surface (issue #34, EVT-003): list, unread badge, and mark-read
/// ("visiting an event's destination may mark its notification read automatically" -- the web client
/// calls the mark-read endpoint itself once it navigates to a notification's destination, matching
/// Progressive Interface's "no persistently visible advanced controls" rather than this endpoint
/// guessing navigation intent server-side). Every route resolves "whose notifications" exclusively
/// from the authenticated session, matching <see cref="CloudInventoryEndpoints"/>'s own discipline.
/// </summary>
public static class CloudNotificationEndpoints
{
    public static IEndpointRouteBuilder MapCloudNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/notifications", HandleListAsync);
        endpoints.MapGet("/notifications/unread-count", HandleGetUnreadCountAsync);
        endpoints.MapPost("/notifications/{notificationId}/read", HandleMarkReadAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudNotificationReader notificationReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var viewerResult = await TryResolveViewerAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        var notifications = await notificationReader.ListAsync(options.ShardId, viewerResult.Viewer!, cancellationToken);
        return Results.Ok(new
        {
            notifications = notifications.Select(notification => new
            {
                id = notification.Id,
                kind = notification.Kind.ToString(),
                destination = notification.Destination,
                count = notification.OccurrenceCount,
                isRead = notification.IsRead,
                firstOccurredAtUtc = notification.FirstOccurredAtUtc,
                lastOccurredAtUtc = notification.LastOccurredAtUtc,
            }),
        });
    }

    private static async Task<IResult> HandleGetUnreadCountAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudNotificationReader notificationReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var viewerResult = await TryResolveViewerAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        var summary = await notificationReader.GetUnreadSummaryAsync(options.ShardId, viewerResult.Viewer!, cancellationToken);
        return Results.Ok(new { unreadCount = summary.UnreadCount });
    }

    private static async Task<IResult> HandleMarkReadAsync(
        HttpContext httpContext,
        Guid notificationId,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudNotificationWriter notificationWriter,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var viewerResult = await TryResolveViewerAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        if (!CloudEndpointSessionHelpers.CsrfMatches(httpContext, viewerResult.Session!))
        {
            return Results.Json(new { error = "csrf_mismatch" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var marked = await notificationWriter.TryMarkReadAsync(options.ShardId, viewerResult.Viewer!, notificationId, cancellationToken);

        // A revoked-permission or nonexistent notification report identically (issue #34 Red:
        // "revoked permissions"), matching the existing item-visibility "generic not_found" precedent.
        return marked
            ? Results.Ok()
            : Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<(CloudWebSession? Session, CloudLiveStreamViewer? Viewer, IResult? Error)> TryResolveViewerAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await CloudEndpointSessionHelpers.TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return (null, null, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            return (null, null, Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden));
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, effectiveMainAccountId);
        return (session, CloudLiveStreamViewer.ForOwners([ownerId]), null);
    }
}
