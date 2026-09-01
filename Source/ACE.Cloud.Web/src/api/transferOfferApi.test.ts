import { describe, expect, it, vi } from "vitest";
import { createTransferOfferApi } from "./transferOfferApi";
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

describe("createTransferOfferApi", () => {
  it("lists from /transfer-offers", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createTransferOfferApi(fakeHttpClient({ get }));

    await api.list();

    expect(get).toHaveBeenCalledWith("/transfer-offers");
  });

  it("creates with the recipient name and targets", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createTransferOfferApi(fakeHttpClient({ post }));

    await api.create("Recipient", [{ kind: "Item", itemBiotaId: 123 }]);

    expect(post).toHaveBeenCalledWith("/transfer-offers", {
      recipientCharacterName: "Recipient",
      targets: [{ kind: "Item", itemBiotaId: 123 }],
    });
  });

  it("accepts with the expected version", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createTransferOfferApi(fakeHttpClient({ post }));

    await api.accept("offer-1", 3);

    expect(post).toHaveBeenCalledWith("/transfer-offers/offer-1/accept", { expectedVersion: 3 });
  });

  it("declines with the expected version", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createTransferOfferApi(fakeHttpClient({ post }));

    await api.decline("offer-1", 3);

    expect(post).toHaveBeenCalledWith("/transfer-offers/offer-1/decline", { expectedVersion: 3 });
  });

  it("cancels with the expected version", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createTransferOfferApi(fakeHttpClient({ post }));

    await api.cancel("offer-1", 3);

    expect(post).toHaveBeenCalledWith("/transfer-offers/offer-1/cancel", { expectedVersion: 3 });
  });
});
