import { describe, expect, it, vi } from "vitest";
import { createWithdrawalApi } from "./withdrawalApi";
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

describe("createWithdrawalApi", () => {
  it("fetches /withdrawal-locations", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createWithdrawalApi(fakeHttpClient({ get }));

    await api.fetchLocations();

    expect(get).toHaveBeenCalledWith("/withdrawal-locations");
  });

  it("fetches /withdrawals/current", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: { active: false } }) as HttpResult<unknown>);
    const api = createWithdrawalApi(fakeHttpClient({ get }));

    const result = await api.fetchCurrent();

    expect(get).toHaveBeenCalledWith("/withdrawals/current");
    expect(result.data).toEqual({ active: false });
  });

  it("posts the selected targets to /withdrawals", async () => {
    const post = vi.fn(
      async () =>
        ({ ok: true, status: 200, data: { secret: "s3cr3t", reservationId: "r1", version: 1, expiresAtUtc: "later" } }) as HttpResult<unknown>,
    );
    const api = createWithdrawalApi(fakeHttpClient({ post }));

    const result = await api.create([{ kind: "Item", itemBiotaId: 777 }]);

    expect(post).toHaveBeenCalledWith("/withdrawals", { targets: [{ kind: "Item", itemBiotaId: 777 }] });
    expect(result.data?.secret).toBe("s3cr3t");
  });

  it("posts the expected version to /withdrawals/{id}/cancel", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: { reservationId: "r1", version: 2, status: "Released" } }) as HttpResult<unknown>);
    const api = createWithdrawalApi(fakeHttpClient({ post }));

    await api.cancel("r1", 1);

    expect(post).toHaveBeenCalledWith("/withdrawals/r1/cancel", { expectedVersion: 1 });
  });

  it("posts the split quantity to /inventory/stack-lots/{id}/split", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createWithdrawalApi(fakeHttpClient({ post }));

    await api.splitStackLot("lot1", 3, 5);

    expect(post).toHaveBeenCalledWith("/inventory/stack-lots/lot1/split", { expectedVersion: 3, quantity: 5 });
  });
});
