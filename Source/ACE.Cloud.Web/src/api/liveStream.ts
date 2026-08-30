/**
 * A typed, transport-agnostic scaffold for the Live State Stream (EVT-007). No server endpoint
 * exists yet -- issues that add streamed Marketplace/inventory/notification data will supply a
 * real URL and wire this up. The envelope shape mirrors
 * `ACE.Cloud.Contracts.CloudEventEnvelope<T>` / `CloudPublicEventEnvelope<T>` (a monotonic
 * `version` plus a typed `payload`) so client reconciliation logic added later has a stable
 * contract to target.
 */

export interface CloudLiveStreamEnvelope<TPayload> {
  readonly version: number;
  readonly payload: TPayload;
}

export type CloudLiveStreamStatus = "idle" | "connecting" | "open" | "closed";

export interface CloudLiveStreamClient {
  readonly status: CloudLiveStreamStatus;
  connect(): void;
  disconnect(): void;
}

export interface EventSourceLike {
  onmessage: ((event: { data: string }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  onopen: (() => void) | null;
  close(): void;
}

export interface CloudLiveStreamOptions<TPayload> {
  readonly url: string;
  readonly onMessage: (envelope: CloudLiveStreamEnvelope<TPayload>) => void;
  readonly onStatusChange?: (status: CloudLiveStreamStatus) => void;
  readonly createEventSource?: (url: string) => EventSourceLike;
}

function defaultCreateEventSource(url: string): EventSourceLike {
  return new EventSource(url) as unknown as EventSourceLike;
}

function isEnvelope(value: unknown): value is CloudLiveStreamEnvelope<unknown> {
  if (typeof value !== "object" || value === null) {
    return false;
  }
  const record = value as Record<string, unknown>;
  return typeof record.version === "number" && "payload" in record;
}

export function createCloudLiveStreamClient<TPayload>(
  options: CloudLiveStreamOptions<TPayload>,
): CloudLiveStreamClient {
  let status: CloudLiveStreamStatus = "idle";
  let source: EventSourceLike | null = null;

  function setStatus(next: CloudLiveStreamStatus): void {
    status = next;
    options.onStatusChange?.(next);
  }

  return {
    get status() {
      return status;
    },
    connect() {
      const createEventSource = options.createEventSource ?? defaultCreateEventSource;
      source = createEventSource(options.url);
      setStatus("connecting");

      source.onopen = () => setStatus("open");
      source.onerror = () => {
        source?.close();
        setStatus("closed");
      };
      source.onmessage = (event) => {
        let parsed: unknown;
        try {
          parsed = JSON.parse(event.data);
        } catch {
          return;
        }
        if (isEnvelope(parsed)) {
          options.onMessage(parsed as CloudLiveStreamEnvelope<TPayload>);
        }
      };
    },
    disconnect() {
      source?.close();
      source = null;
      setStatus("idle");
    },
  };
}
