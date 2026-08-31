import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createCloudLiveStreamReconciler } from "./liveStreamReconciler";
import type { CloudLiveStreamMessage } from "./types";

function eventMessage(overrides: Partial<Extract<CloudLiveStreamMessage, { kind: "event" }>> = {}): CloudLiveStreamMessage {
  return {
    kind: "event",
    eventKind: "Deposit",
    sequenceNumber: 1,
    sourceEventId: "11111111-1111-1111-1111-111111111111",
    ...overrides,
  };
}

describe("createCloudLiveStreamReconciler", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("forwards a state message's mode without treating it as a refreshable event", () => {
    const onModeChange = vi.fn();
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, onModeChange });

    reconciler.handleMessage({ kind: "state", mode: "ReadOnly" });

    expect(onModeChange).toHaveBeenCalledWith("ReadOnly");
    vi.runAllTimers();
    expect(onRefresh).not.toHaveBeenCalled();
  });

  it("schedules a debounced custody refresh for a non-Notification eventKind", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 200 });

    reconciler.handleMessage(eventMessage({ eventKind: "Deposit" }));
    expect(onRefresh).not.toHaveBeenCalled();

    vi.advanceTimersByTime(200);
    expect(onRefresh).toHaveBeenCalledExactlyOnceWith("custody");
  });

  it("routes eventKind Notification to the notification scope", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 50 });

    reconciler.handleMessage(eventMessage({ eventKind: "Notification", sourceEventId: "22222222-2222-2222-2222-222222222222" }));
    vi.advanceTimersByTime(50);

    expect(onRefresh).toHaveBeenCalledExactlyOnceWith("notification");
  });

  it("coalesces several rapid events of the same scope into a single refresh (no duplicate refresh storm)", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 200 });

    reconciler.handleMessage(eventMessage({ sequenceNumber: 1, sourceEventId: "a" }));
    vi.advanceTimersByTime(50);
    reconciler.handleMessage(eventMessage({ sequenceNumber: 2, sourceEventId: "b" }));
    vi.advanceTimersByTime(50);
    reconciler.handleMessage(eventMessage({ sequenceNumber: 3, sourceEventId: "c" }));
    vi.advanceTimersByTime(200);

    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(onRefresh).toHaveBeenCalledWith("custody");
  });

  it("does not schedule a second refresh for a duplicate sourceEventId (idempotent reconciliation)", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 10 });

    reconciler.handleMessage(eventMessage({ sequenceNumber: 5, sourceEventId: "dup" }));
    vi.advanceTimersByTime(10);
    expect(onRefresh).toHaveBeenCalledTimes(1);

    reconciler.handleMessage(eventMessage({ sequenceNumber: 5, sourceEventId: "dup" }));
    vi.advanceTimersByTime(10);

    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it("tracks the highest sequenceNumber seen as lastSequenceNumber and ignores a lower out-of-order one", () => {
    const reconciler = createCloudLiveStreamReconciler({ onRefresh: vi.fn(), debounceMs: 10 });

    reconciler.handleMessage(eventMessage({ sequenceNumber: 7, sourceEventId: "x" }));
    expect(reconciler.lastSequenceNumber).toBe(7);

    reconciler.handleMessage(eventMessage({ sequenceNumber: 3, sourceEventId: "y" }));
    expect(reconciler.lastSequenceNumber).toBe(7);

    reconciler.handleMessage(eventMessage({ sequenceNumber: 9, sourceEventId: "z" }));
    expect(reconciler.lastSequenceNumber).toBe(9);
  });

  it("evicts the oldest tracked event id once maxTrackedEventIds is exceeded, bounding memory use", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 10, maxTrackedEventIds: 2 });

    reconciler.handleMessage(eventMessage({ sequenceNumber: 1, sourceEventId: "first" }));
    reconciler.handleMessage(eventMessage({ sequenceNumber: 2, sourceEventId: "second" }));
    reconciler.handleMessage(eventMessage({ sequenceNumber: 3, sourceEventId: "third" }));
    vi.advanceTimersByTime(10);
    onRefresh.mockClear();

    // "first" was evicted once the tracked set exceeded maxTrackedEventIds, so it is treated as new again.
    reconciler.handleMessage(eventMessage({ sequenceNumber: 1, sourceEventId: "first" }));
    vi.advanceTimersByTime(10);

    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it("dispose() cancels a pending debounced refresh", () => {
    const onRefresh = vi.fn();
    const reconciler = createCloudLiveStreamReconciler({ onRefresh, debounceMs: 200 });

    reconciler.handleMessage(eventMessage());
    reconciler.dispose();
    vi.advanceTimersByTime(200);

    expect(onRefresh).not.toHaveBeenCalled();
  });
});
