using System.Text.Json;

using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACE.Cloud.Backend;

/// <summary>
/// The resumable private Live State Stream HTTP surface (issue #34, EVT-007: "connect inventory/
/// activity/notification clients to resumable private Live State Streams with explicit stale/
/// read-only states"). Serves Server-Sent Events so the browser's native <c>EventSource</c> handles
/// reconnection and cursor resumption on its own: every event is written with an <c>id:</c> field
/// equal to its <see cref="CloudLiveStreamEvent.SequenceNumber"/>, so a dropped connection
/// automatically resumes via the browser's own <c>Last-Event-ID</c> request header -- the same cursor
/// contract <see cref="CloudLiveStreamReader.ReadAfterAsync"/> already exposes to any caller. A
/// leading and then periodic "state" message (never a numbered <c>id:</c>, since it has no sequence
/// number of its own) reports the current <see cref="CloudServiceAvailabilityMode"/> so a connected
/// client can render an explicit stale/read-only banner instead of silently going quiet during an
/// outage (ARCH-008/ARCH-009).
/// </summary>
public static class CloudLiveStreamEndpoints
{
    private const string LastEventIdHeaderName = "Last-Event-ID";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromSeconds(55);

    public static IEndpointRouteBuilder MapCloudLiveStreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/live-stream", HandleStreamAsync);
        return endpoints;
    }

    private static async Task HandleStreamAsync(
        HttpContext httpContext,
        ICloudWebSessionStore sessionStore,
        ICloudAccountOwnershipResolver accountOwnershipResolver,
        ICloudLiveStreamReader liveStreamReader,
        ICloudServiceAvailabilityReader serviceAvailabilityReader,
        CloudBackendOptions options)
    {
        var requestAborted = httpContext.RequestAborted;

        var session = await CloudEndpointSessionHelpers.TryGetActiveSessionAsync(httpContext, sessionStore, options, requestAborted);
        if (session is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var effectiveMainAccountId = await accountOwnershipResolver.ResolveEffectiveOwnerAccountIdAsync(options.ShardId, session.AccountId, requestAborted);
        if (effectiveMainAccountId != session.AccountId)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var ownerId = CloudOwnerIdentity.ForAccount(options.ShardId, effectiveMainAccountId);
        var viewer = CloudLiveStreamViewer.ForOwners([ownerId]);

        var cursor = ParseResumeCursor(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        var deadlineUtc = DateTime.UtcNow.Add(ConnectionLifetime);
        CloudServiceAvailabilityMode? lastReportedMode = null;

        while (!requestAborted.IsCancellationRequested && DateTime.UtcNow < deadlineUtc)
        {
            var mode = await serviceAvailabilityReader.GetCurrentModeAsync(requestAborted);
            if (mode != lastReportedMode)
            {
                await WriteStateMessageAsync(httpContext.Response, mode, requestAborted);
                lastReportedMode = mode;
            }

            var events = await liveStreamReader.ReadAfterAsync(viewer, cursor, maxCount: 100, requestAborted);
            foreach (var streamEvent in events)
            {
                await WriteEventMessageAsync(httpContext.Response, streamEvent, requestAborted);
                cursor = streamEvent.SequenceNumber;
            }

            await httpContext.Response.Body.FlushAsync(requestAborted);

            try
            {
                await Task.Delay(PollInterval, requestAborted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Resumes from the browser's own <c>Last-Event-ID</c> reconnection header when present,
    /// otherwise a caller-supplied <c>since</c> query parameter for the very first connect (an
    /// <c>EventSource</c> has no way to set a custom header on its initial request), otherwise from
    /// the very beginning.
    /// </summary>
    private static long ParseResumeCursor(HttpContext httpContext)
    {
        var lastEventId = httpContext.Request.Headers[LastEventIdHeaderName].ToString();
        if (long.TryParse(lastEventId, out var fromHeader) && fromHeader >= 0)
        {
            return fromHeader;
        }

        var since = httpContext.Request.Query["since"].ToString();
        if (long.TryParse(since, out var fromQuery) && fromQuery >= 0)
        {
            return fromQuery;
        }

        return 0;
    }

    private static async Task WriteStateMessageAsync(HttpResponse response, CloudServiceAvailabilityMode mode, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { version = 0, payload = new { kind = "state", mode = mode.ToString() } });
        await response.WriteAsync($"data: {payload}\n\n", cancellationToken);
    }

    private static async Task WriteEventMessageAsync(HttpResponse response, CloudLiveStreamEvent streamEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            version = streamEvent.SequenceNumber,
            payload = new
            {
                kind = "event",
                eventKind = streamEvent.EventKind,
                sequenceNumber = streamEvent.SequenceNumber,
                sourceEventId = streamEvent.SourceEventId,
            },
        });
        await response.WriteAsync($"id: {streamEvent.SequenceNumber}\ndata: {payload}\n\n", cancellationToken);
    }
}
