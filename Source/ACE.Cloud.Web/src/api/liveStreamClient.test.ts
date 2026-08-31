import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createResumableCloudLiveStreamClient } from "./liveStreamClient";

interface FakeEventSource {
  onmessage: ((event: { data: string }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  onopen: (() => void) | null;
  close: () => void;
}

function fakeEventSourceFactory() {
  const urls: string[] = [];
  const instances: FakeEventSource[] = [];
  const factory = vi.fn((url: string) => {
    urls.push(url);
    const instance: FakeEventSource = { onmessage: null, onerror: null, onopen: null, close: vi.fn() };
    instances.push(instance);
    return instance;
  });
  return { factory, instances, urls };
}

function eventEnvelope(sequenceNumber: number, sourceEventId = `evt-${sequenceNumber}`) {
  return {
    data: JSON.stringify({
      version: sequenceNumber,
      payload: { kind: "event", eventKind: "Deposit", sequenceNumber, sourceEventId },
    }),
  };
}

describe("createResumableCloudLiveStreamClient", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("connects to the plain URL with no since= param on the first connect", () => {
    const { factory, urls } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
    });

    client.connect();

    expect(urls).toEqual(["https://cloud.example/live-stream"]);
  });

  it("is stale before the connection opens and stops being stale once open", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
    });

    client.connect();
    expect(client.stale).toBe(true);

    instances[0]!.onopen?.();
    expect(client.stale).toBe(false);
    expect(client.status).toBe("open");
  });

  it("reconnects using ?since=<lastSequenceNumber> after having received an event", () => {
    const { factory, instances, urls } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
      initialBackoffMs: 1000,
    });

    client.connect();
    instances[0]!.onopen?.();
    instances[0]!.onmessage?.(eventEnvelope(42));

    instances[0]!.onerror?.(new Event("error"));
    vi.advanceTimersByTime(1000);

    expect(urls).toEqual(["https://cloud.example/live-stream", "https://cloud.example/live-stream?since=42"]);
  });

  it("marks the connection stale again after an error and while waiting to reconnect", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
      initialBackoffMs: 1000,
    });

    client.connect();
    instances[0]!.onopen?.();
    expect(client.stale).toBe(false);

    instances[0]!.onerror?.(new Event("error"));
    expect(client.stale).toBe(true);
    expect(client.status).toBe("closed");
  });

  it("doubles the backoff delay on repeated failures and resets it after a successful open", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onStatusChange = vi.fn();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      onStatusChange,
      createEventSource: factory,
      initialBackoffMs: 1000,
      maxBackoffMs: 8000,
    });

    client.connect();
    instances[0]!.onerror?.(new Event("error"));
    vi.advanceTimersByTime(1000);
    expect(factory).toHaveBeenCalledTimes(2);

    instances[1]!.onerror?.(new Event("error"));
    vi.advanceTimersByTime(1000);
    // Second failure without ever opening: backoff should have doubled, so 1000ms is not yet enough.
    expect(factory).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1000);
    expect(factory).toHaveBeenCalledTimes(3);

    instances[2]!.onopen?.();
    instances[2]!.onerror?.(new Event("error"));
    vi.advanceTimersByTime(1000);
    // Backoff reset to the initial delay after the successful open.
    expect(factory).toHaveBeenCalledTimes(4);
  });

  it("does not schedule overlapping reconnects for repeated errors on the same connection attempt", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
      initialBackoffMs: 1000,
    });

    client.connect();
    instances[0]!.onerror?.(new Event("error"));
    instances[0]!.onerror?.(new Event("error"));
    vi.advanceTimersByTime(1000);

    expect(factory).toHaveBeenCalledTimes(2);
  });

  it("disconnect() cancels a pending scheduled reconnect and does not reconnect", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage: vi.fn(),
      createEventSource: factory,
      initialBackoffMs: 1000,
    });

    client.connect();
    instances[0]!.onerror?.(new Event("error"));
    client.disconnect();
    vi.advanceTimersByTime(5000);

    expect(factory).toHaveBeenCalledTimes(1);
    expect(client.status).toBe("idle");
  });

  it("forwards parsed messages to onMessage", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onMessage = vi.fn();
    const client = createResumableCloudLiveStreamClient({
      url: "https://cloud.example/live-stream",
      onMessage,
      createEventSource: factory,
    });

    client.connect();
    instances[0]!.onmessage?.(eventEnvelope(5));

    expect(onMessage).toHaveBeenCalledWith({ kind: "event", eventKind: "Deposit", sequenceNumber: 5, sourceEventId: "evt-5" });
  });
});
