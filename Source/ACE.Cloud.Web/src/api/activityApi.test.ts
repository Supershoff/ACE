import { describe, expect, it, vi } from "vitest";
import { createActivityApi } from "./activityApi";
import type { HttpClient, HttpResult } from "./httpClient";

type FakeHttpClientOverrides = {
  get?: (...args: unknown[]) => Promise<HttpResult<unknown>>;
};

function fakeHttpClient(overrides: FakeHttpClientOverrides = {}): HttpClient {
  return {
    get: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    post: vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as HttpClient;
}

describe("createActivityApi", () => {
  it("queries /activity with no parameters by default", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const activityApi = createActivityApi(fakeHttpClient({ get }));

    await activityApi.queryLedger();

    expect(get).toHaveBeenCalledWith("/activity");
  });

  it("encodes page, pageSize, and vault into the query string", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const activityApi = createActivityApi(fakeHttpClient({ get }));

    await activityApi.queryLedger({ page: 2, pageSize: 10, vault: true });

    expect(get).toHaveBeenCalledWith("/activity?page=2&pageSize=10&vault=true");
  });

  it("omits vault from the query string when false", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const activityApi = createActivityApi(fakeHttpClient({ get }));

    await activityApi.queryLedger({ vault: false });

    expect(get).toHaveBeenCalledWith("/activity");
  });
});
