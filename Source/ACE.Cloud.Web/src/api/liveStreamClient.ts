import { createCloudLiveStreamClient, type CloudLiveStreamStatus, type EventSourceLike } from "./liveStream";
import type { CloudLiveStreamMessage } from "./types";

export interface CloudResumableLiveStreamOptions {
  readonly url: string;
  readonly onMessage: (message: CloudLiveStreamMessage) => void;
  readonly onStatusChange?: (status: CloudLiveStreamStatus, stale: boolean) => void;
  readonly createEventSource?: (url: string) => EventSourceLike;
  readonly initialBackoffMs?: number;
  readonly maxBackoffMs?: number;
  readonly setTimeout?: typeof setTimeout;
  readonly clearTimeout?: typeof clearTimeout;
}

export interface CloudResumableLiveStreamClient {
  readonly status: CloudLiveStreamStatus;
  /** True whenever the connection is not currently open, so consumers can show cached data as possibly out of date. */
  readonly stale: boolean;
  connect(): void;
  disconnect(): void;
}

/**
 * Wraps the transport-only `createCloudLiveStreamClient` (`./liveStream.ts`) with the reconnection
 * behavior the `/live-stream` endpoint's resumable design (EVT-007, `CloudLiveStreamEndpoints`)
 * actually needs: `EventSourceLike.onerror` never re-opens itself, so left alone the plain client dies
 * on the server's own ~55s forced disconnect. This wrapper reconnects with exponential backoff and
 * resumes from the highest `sequenceNumber` it has seen via the server's documented `?since=` query
 * fallback (the same cursor the server also derives from a real browser `EventSource`'s native
 * `Last-Event-ID` header on a true reconnect; a fresh `EventSource` instance has no such header, so an
 * explicit resume cursor is required here).
 */
export function createResumableCloudLiveStreamClient(
  options: CloudResumableLiveStreamOptions,
): CloudResumableLiveStreamClient {
  const initialBackoffMs = options.initialBackoffMs ?? 1000;
  const maxBackoffMs = options.maxBackoffMs ?? 30000;
  const scheduleTimeout = options.setTimeout ?? setTimeout;
  const cancelTimeout = options.clearTimeout ?? clearTimeout;

  let status: CloudLiveStreamStatus = "idle";
  let stale = true;
  let lastSequenceNumber = 0;
  let backoffMs = initialBackoffMs;
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  let disposed = true;
  let inner: ReturnType<typeof createCloudLiveStreamClient<CloudLiveStreamMessage>> | null = null;

  function setStatus(next: CloudLiveStreamStatus, nextStale: boolean): void {
    status = next;
    stale = nextStale;
    options.onStatusChange?.(next, nextStale);
  }

  function resumeUrl(): string {
    if (lastSequenceNumber <= 0) {
      return options.url;
    }
    const separator = options.url.includes("?") ? "&" : "?";
    return `${options.url}${separator}since=${lastSequenceNumber}`;
  }

  function scheduleReconnect(): void {
    if (disposed || reconnectTimer !== null) {
      return;
    }
    reconnectTimer = scheduleTimeout(() => {
      reconnectTimer = null;
      openConnection();
    }, backoffMs);
    backoffMs = Math.min(backoffMs * 2, maxBackoffMs);
  }

  function openConnection(): void {
    inner = createCloudLiveStreamClient<CloudLiveStreamMessage>({
      url: resumeUrl(),
      createEventSource: options.createEventSource,
      onStatusChange: (innerStatus) => {
        if (innerStatus === "open") {
          backoffMs = initialBackoffMs;
          setStatus("open", false);
          return;
        }
        if (innerStatus === "closed") {
          setStatus("closed", true);
          scheduleReconnect();
          return;
        }
        setStatus(innerStatus, true);
      },
      onMessage: (envelope) => {
        const message = envelope.payload;
        if (message.kind === "event" && message.sequenceNumber > lastSequenceNumber) {
          lastSequenceNumber = message.sequenceNumber;
        }
        options.onMessage(message);
      },
    });
    inner.connect();
  }

  return {
    get status() {
      return status;
    },
    get stale() {
      return stale;
    },
    connect() {
      disposed = false;
      openConnection();
    },
    disconnect() {
      disposed = true;
      if (reconnectTimer !== null) {
        cancelTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      inner?.disconnect();
      inner = null;
      backoffMs = initialBackoffMs;
      setStatus("idle", true);
    },
  };
}
