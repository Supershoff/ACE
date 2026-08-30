import type { CloudLiveStreamMessage, CloudServiceAvailabilityMode } from "./types";

/**
 * "custody" covers every non-notification eventKind (inventory/Activity Ledger both project from the
 * same Custody Outbox events, per `CloudCustodyProjectionConsumer`); "notification" is the single
 * `eventKind: "Notification"` published by `CloudNotificationProjectionConsumer`.
 */
export type CloudLiveStreamRefreshScope = "custody" | "notification";

export interface CloudLiveStreamReconcilerOptions {
  readonly onRefresh: (scope: CloudLiveStreamRefreshScope) => void;
  readonly onModeChange?: (mode: CloudServiceAvailabilityMode) => void;
  /** Coalesces bursts of same-scope events into one refresh; defaults to 250ms. */
  readonly debounceMs?: number;
  /** Bounds the dedupe set's memory so a long-lived connection cannot grow it unboundedly; defaults to 500. */
  readonly maxTrackedEventIds?: number;
  readonly setTimeout?: typeof setTimeout;
  readonly clearTimeout?: typeof clearTimeout;
}

export interface CloudLiveStreamReconciler {
  /** Feed one parsed SSE message from the Live State Stream through reconciliation. */
  handleMessage(message: CloudLiveStreamMessage): void;
  /** The highest event sequenceNumber observed so far. */
  readonly lastSequenceNumber: number;
  dispose(): void;
}

/**
 * Idempotently reconciles Live State Stream messages (EVT-007) into coalesced, deduplicated refresh
 * signals for the inventory/Activity Ledger/Notification Center clients. A duplicate outbox delivery
 * must never trigger a second refresh or regress unread state (issue #34 acceptance criteria), so
 * every event is deduped by `sourceEventId` before it can schedule work.
 */
export function createCloudLiveStreamReconciler(options: CloudLiveStreamReconcilerOptions): CloudLiveStreamReconciler {
  const debounceMs = options.debounceMs ?? 250;
  const maxTrackedEventIds = options.maxTrackedEventIds ?? 500;
  const scheduleTimeout = options.setTimeout ?? setTimeout;
  const cancelTimeout = options.clearTimeout ?? clearTimeout;

  const seenEventIds = new Set<string>();
  const seenEventIdOrder: string[] = [];
  const pendingTimers = new Map<CloudLiveStreamRefreshScope, ReturnType<typeof setTimeout>>();
  let lastSequenceNumber = 0;

  function rememberEventId(sourceEventId: string): boolean {
    if (seenEventIds.has(sourceEventId)) {
      return false;
    }
    seenEventIds.add(sourceEventId);
    seenEventIdOrder.push(sourceEventId);
    if (seenEventIdOrder.length > maxTrackedEventIds) {
      const oldest = seenEventIdOrder.shift();
      if (oldest !== undefined) {
        seenEventIds.delete(oldest);
      }
    }
    return true;
  }

  function scheduleRefresh(scope: CloudLiveStreamRefreshScope): void {
    if (pendingTimers.has(scope)) {
      return;
    }
    const timer = scheduleTimeout(() => {
      pendingTimers.delete(scope);
      options.onRefresh(scope);
    }, debounceMs);
    pendingTimers.set(scope, timer);
  }

  return {
    handleMessage(message) {
      if (message.kind === "state") {
        options.onModeChange?.(message.mode);
        return;
      }

      if (message.sequenceNumber > lastSequenceNumber) {
        lastSequenceNumber = message.sequenceNumber;
      }

      if (!rememberEventId(message.sourceEventId)) {
        return;
      }

      const scope: CloudLiveStreamRefreshScope = message.eventKind === "Notification" ? "notification" : "custody";
      scheduleRefresh(scope);
    },
    get lastSequenceNumber() {
      return lastSequenceNumber;
    },
    dispose() {
      for (const timer of pendingTimers.values()) {
        cancelTimeout(timer);
      }
      pendingTimers.clear();
    },
  };
}
