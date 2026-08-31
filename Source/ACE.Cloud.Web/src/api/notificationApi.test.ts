import { describe, expect, it, vi } from "vitest";
import { createNotificationApi } from "./notificationApi";
import type { HttpClient, HttpResult } from "./httpClient";

type FakeHttpClientOverrides = {
  get?: (...args: unknown[]) => Promise<HttpResult<unknown>>;
  post?: (...args: unknown[]) => Promise<HttpResult<unknown>>;
};

function fakeHttpClient(overrides: FakeHttpClientOverrides = {}): HttpClient {
  return {
    get: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    post: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as HttpClient;
}

describe("createNotificationApi", () => {
  it("fetches /notifications", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: { notifications: [] } }) as HttpResult<unknown>);
    const notificationApi = createNotificationApi(fakeHttpClient({ get }));

    await notificationApi.list();

    expect(get).toHaveBeenCalledWith("/notifications");
  });

  it("fetches /notifications/unread-count", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: { unreadCount: 3 } }) as HttpResult<unknown>);
    const notificationApi = createNotificationApi(fakeHttpClient({ get }));

    const result = await notificationApi.fetchUnreadCount();

    expect(get).toHaveBeenCalledWith("/notifications/unread-count");
    expect(result.data).toMatchObject({ unreadCount: 3 });
  });

  it("posts to /notifications/{id}/read", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200 }) as HttpResult<unknown>);
    const notificationApi = createNotificationApi(fakeHttpClient({ post }));

    await notificationApi.markRead("abc-123");

    expect(post).toHaveBeenCalledWith("/notifications/abc-123/read");
  });
});
