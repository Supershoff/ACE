import { describe, expect, it, vi } from "vitest";
import { createInventoryApi } from "./inventoryApi";
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

describe("createInventoryApi", () => {
  it("queries /inventory/pages with no query string when no params are given", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const inventoryApi = createInventoryApi(fakeHttpClient({ get }));

    await inventoryApi.queryPages();

    expect(get).toHaveBeenCalledWith("/inventory/pages");
  });

  it("encodes category/page/sort into the query string", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const inventoryApi = createInventoryApi(fakeHttpClient({ get }));

    await inventoryApi.queryPages({ category: "Armor", page: 2, sortKey: "Value", sortDirection: "Descending" });

    expect(get).toHaveBeenCalledWith("/inventory/pages?category=Armor&page=2&sortKey=Value&sortDirection=Descending");
  });

  it("fetches a single item's appraisal by ID", async () => {
    const get = vi.fn(
      async () => ({ ok: true, status: 200, data: { itemName: "Ivory Buckler" } }) as HttpResult<unknown>,
    );
    const inventoryApi = createInventoryApi(fakeHttpClient({ get }));

    const result = await inventoryApi.fetchAppraisal(777);

    expect(get).toHaveBeenCalledWith("/inventory/items/777/appraisal");
    expect(result.data).toEqual({ itemName: "Ivory Buckler" });
  });

  it("builds a same-origin icon URL from a cache key", () => {
    const inventoryApi = createInventoryApi(fakeHttpClient());

    expect(inventoryApi.buildIconUrl("a".repeat(64))).toBe(`/inventory/icons/${"a".repeat(64)}`);
  });
});
