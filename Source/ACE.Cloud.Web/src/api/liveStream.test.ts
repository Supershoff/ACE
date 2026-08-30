import { describe, expect, it, vi } from "vitest";
import { createCloudLiveStreamClient } from "./liveStream";

interface FakeEventSource {
  onmessage: ((event: { data: string }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  onopen: (() => void) | null;
  close: () => void;
}

function fakeEventSourceFactory() {
  const instances: FakeEventSource[] = [];
  const factory = vi.fn((_url: string) => {
    const instance: FakeEventSource = {
      onmessage: null,
      onerror: null,
      onopen: null,
      close: vi.fn(),
    };
    instances.push(instance);
    return instance;
  });
  return { factory, instances };
}

describe("createCloudLiveStreamClient", () => {
  it("starts idle and does not connect until connect() is called", () => {
    const { factory } = fakeEventSourceFactory();
    const client = createCloudLiveStreamClient({
      url: "https://cloud.example/stream",
      onMessage: vi.fn(),
      createEventSource: factory,
    });

    expect(client.status).toBe("idle");
    expect(factory).not.toHaveBeenCalled();
  });

  it("transitions to connecting then open, invoking onStatusChange", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onStatusChange = vi.fn();
    const client = createCloudLiveStreamClient({
      url: "https://cloud.example/stream",
      onMessage: vi.fn(),
      onStatusChange,
      createEventSource: factory,
    });

    client.connect();
    expect(client.status).toBe("connecting");

    instances[0]!.onopen?.();
    expect(client.status).toBe("open");
    expect(onStatusChange).toHaveBeenCalledWith("connecting");
    expect(onStatusChange).toHaveBeenCalledWith("open");
  });

  it("parses a versioned JSON envelope and forwards it to onMessage", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onMessage = vi.fn();
    const client = createCloudLiveStreamClient<{ listingId: string }>({
      url: "https://cloud.example/stream",
      onMessage,
      createEventSource: factory,
    });

    client.connect();
    instances[0]!.onmessage?.({ data: JSON.stringify({ version: 3, payload: { listingId: "abc" } }) });

    expect(onMessage).toHaveBeenCalledWith({ version: 3, payload: { listingId: "abc" } });
  });

  it("ignores a malformed message instead of throwing", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onMessage = vi.fn();
    const client = createCloudLiveStreamClient({
      url: "https://cloud.example/stream",
      onMessage,
      createEventSource: factory,
    });

    client.connect();
    expect(() => instances[0]!.onmessage?.({ data: "not json" })).not.toThrow();
    expect(onMessage).not.toHaveBeenCalled();
  });

  it("closes the underlying source and becomes closed on error", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const onStatusChange = vi.fn();
    const client = createCloudLiveStreamClient({
      url: "https://cloud.example/stream",
      onMessage: vi.fn(),
      onStatusChange,
      createEventSource: factory,
    });

    client.connect();
    instances[0]!.onerror?.(new Event("error"));

    expect(client.status).toBe("closed");
    expect(instances[0]!.close).toHaveBeenCalled();
  });

  it("disconnect() closes the source and returns to idle", () => {
    const { factory, instances } = fakeEventSourceFactory();
    const client = createCloudLiveStreamClient({
      url: "https://cloud.example/stream",
      onMessage: vi.fn(),
      createEventSource: factory,
    });

    client.connect();
    client.disconnect();

    expect(instances[0]!.close).toHaveBeenCalled();
    expect(client.status).toBe("idle");
  });
});
