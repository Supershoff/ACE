using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// Issue #31's Green section: "Implement virtual sorted Mule Pages and spreadsheet UI over the
/// shared query contract" and "Render the versioned appraisal presentation contract." Issue #30 built
/// the query domain/contract but deliberately left its HTTP wire surface to whichever issue actually
/// needed a web client to call it (see <c>src/api/types.ts</c>'s "Inventory... HTTP contracts land
/// with their own issues"); this is that surface. Every route here reuses
/// <see cref="ICloudInventoryQueryReader"/>'s existing owner/admin authorization rule rather than
/// inventing a new one, and resolves "whose inventory" exclusively from the authenticated session
/// (never from a client-supplied field), matching the security baseline and
/// <see cref="CloudInventoryQueryRequest"/>'s own doc comment.
/// </summary>
public static class CloudInventoryEndpoints
{
    public static IEndpointRouteBuilder MapCloudInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inventory/pages", HandleQueryPagesAsync);
        endpoints.MapGet("/inventory/items/{itemId}/appraisal", HandleGetAppraisalAsync);
        endpoints.MapGet("/inventory/icons/{hex}", HandleGetIconAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleQueryPagesAsync(
        HttpContext httpContext,
        CloudInventoryCategory? category,
        int? page,
        CloudInventorySortKey? sortKey,
        CloudInventorySortDirection? sortDirection,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudInventoryQueryReader inventoryQueryReader,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var viewerResult = await TryResolveOwnMainAccountViewerAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        var pageNumber = page ?? 1;
        if (pageNumber <= 0)
        {
            return Results.Json(new { error = "invalid_page" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var request = new CloudInventoryQueryRequest
        {
            Category = category,
            Page = pageNumber,
            SortKey = sortKey ?? CloudInventorySortKey.Name,
            SortDirection = sortDirection ?? CloudInventorySortDirection.Ascending,
        };

        var response = await inventoryQueryReader.QueryAsync(options.ShardId, viewerResult.Viewer!, request, cancellationToken);
        return Results.Ok(ToWireResponse(response));
    }

    // Every enum here is written with .ToString() rather than serialized as its raw C# int, matching
    // CloudDiagnosticsEndpoints' existing convention: a wire enum must stay stable if a future domain
    // change reorders or inserts a member, and a plain number would silently renumber underneath it.
    private static object ToWireResponse(CloudInventoryQueryResponse response) => new
    {
        page = new
        {
            category = response.Page.Category?.ToString(),
            pageName = response.Page.PageName,
            pageNumber = response.Page.PageNumber,
            pageExists = response.Page.PageExists,
            totalItemsInScope = response.Page.TotalItemsInScope,
            totalPages = response.Page.TotalPages,
            items = response.Page.Items.Select(item => new
            {
                itemId = item.ItemId.Value,
                stackLotId = item.StackLotId?.Value,
                name = item.Name,
                category = item.Category.ToString(),
                quantity = item.Quantity,
                value = item.Value,
                burden = item.Burden,
                isReserved = item.IsReserved,
                version = item.Version.Value,
                permittedActions = new
                {
                    canWithdraw = item.PermittedActions.CanWithdraw,
                    canList = item.PermittedActions.CanList,
                    canTransfer = item.PermittedActions.CanTransfer,
                    canShare = item.PermittedActions.CanShare,
                },
                iconCacheKeyHex = item.IconCacheKeyHex,
            }),
        },
        asOfCustodyOutboxSequenceNumber = response.AsOfCustodyOutboxSequenceNumber,
    };

    private static object ToWireResponse(CloudAppraisalPanel panel) => new
    {
        contractVersion = panel.ContractVersion,
        itemName = panel.ItemName,
        sections = panel.Sections.Select(section => new
        {
            kind = section.Kind.ToString(),
            lines = section.Lines.Select(line => new { text = line.Text, style = line.Style.ToString() }),
        }),
    };

    private static async Task<IResult> HandleGetAppraisalAsync(
        HttpContext httpContext,
        uint itemId,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudInventoryQueryReader inventoryQueryReader,
        ICloudInventoryItemPropertiesGateway propertiesGateway,
        ICloudAppraisalSnapshotGateway appraisalSnapshotGateway,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        if (itemId == 0)
        {
            return Results.Json(new { error = "invalid_item_id" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var viewerResult = await TryResolveOwnMainAccountViewerAsync(httpContext, sessionStore, accountOwnershipResolver, options, cancellationToken);
        if (viewerResult.Error is not null)
        {
            return viewerResult.Error;
        }

        var cloudItemId = new CloudItemId(itemId);

        // A single generic "not found" for both "no such item" and "not authorized for it" -- exactly
        // the login endpoint's "never distinguish... that would let an attacker enumerate" discipline,
        // applied here to item IDs instead of account names.
        var notFound = Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);

        var isVisible = await inventoryQueryReader.IsItemVisibleToViewerAsync(options.ShardId, viewerResult.Viewer!, cloudItemId, cancellationToken);
        if (!isVisible)
        {
            return notFound;
        }

        var properties = await propertiesGateway.TryGetAsync(itemId, options.ShardId, cancellationToken);
        if (properties is null)
        {
            return notFound;
        }

        // Issue #34: ACE's world-boundary deposit/backfill code now captures the complete appraisal
        // snapshot (workmanship/material, spells, armor/weapon profiles, requirements, descriptions,
        // ...) into CloudAppraisalSnapshotProjection. A custody row deposited before this correction,
        // or one whose snapshot capture failed, has no row there yet; it falls back to the same
        // Name/Value/Burden-only panel this endpoint always served, which is still a complete,
        // character-independent, non-GM-field panel for exactly the data available, never a partially
        // revealed or skill-gated one.
        var snapshot = await appraisalSnapshotGateway.TryGetAsync(itemId, options.ShardId, cancellationToken)
            ?? new CloudAppraisalRawItemSnapshot
            {
                ItemId = cloudItemId,
                Name = properties.Name,
                Value = properties.Value,
                Burden = properties.Burden,
            };

        return Results.Ok(ToWireResponse(CloudAppraisalProjector.Build(snapshot)));
    }

    private static async Task<IResult> HandleGetIconAsync(
        HttpContext httpContext,
        string hex,
        ICloudWebSessionStore sessionStore,
        ICloudIconDerivativeReader iconDerivativeReader,
        CloudBackendOptions options,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        CloudIconCompositionCacheKey cacheKey;
        try
        {
            cacheKey = CloudIconCompositionCacheKey.FromHex(hex);
        }
        catch (ArgumentException)
        {
            return Results.Json(new { error = "invalid_cache_key" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var pngBytes = await iconDerivativeReader.TryReadAsync(cacheKey, cancellationToken);
        if (pngBytes is null)
        {
            // UI-006: "create admin diagnostics rather than silently showing a wrong icon." The
            // neutral fallback visual itself is a UI-layer concern (UI-006: "separate UI layers"), so
            // this endpoint reports the miss and lets the caller render its own fallback.
            logger.LogWarning("No composed icon derivative exists for cache key {CacheKeyHex}.", cacheKey.Hex);
            return Results.Json(new { error = "icon_unavailable" }, statusCode: StatusCodes.Status404NotFound);
        }

        httpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return Results.Bytes(pngBytes, "image/png");
    }

    private static async Task<(CloudLiveStreamViewer? Viewer, IResult? Error)> TryResolveOwnMainAccountViewerAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        CloudBackendOptions options,
        CancellationToken cancellationToken)
    {
        var session = await TryGetActiveSessionAsync(httpContext, sessionStore, options, cancellationToken);
        if (session is null)
        {
            return (null, Results.Json(new { error = "unauthenticated" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        // AUTH-004: "Only Main Account credentials can manage the unified Cloud Inventory. Linked
        // credentials... cannot view or mutate Main assets." Enforced server-side here, never only by
        // the web client's own RequireMainAccount route guard.
        var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, cancellationToken);
        if (effectiveMainAccountId != session.AccountId)
        {
            return (null, Results.Json(new { error = "linked_account_restricted" }, statusCode: StatusCodes.Status403Forbidden));
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, effectiveMainAccountId);
        return (CloudLiveStreamViewer.ForOwners([ownerId]), null);
    }

    private static async Task<CloudWebSession?> TryGetActiveSessionAsync(
        HttpContext httpContext, ICloudWebSessionStore sessionStore, CloudBackendOptions options, CancellationToken cancellationToken)
    {
        var secret = httpContext.Request.Cookies[options.SessionCookieName];
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        return await sessionStore.TryGetActiveSessionAsync(CloudWebSessionSecretHasher.Hash(secret), DateTime.UtcNow, cancellationToken);
    }
}
