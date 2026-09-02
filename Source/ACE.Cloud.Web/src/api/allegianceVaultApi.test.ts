import { describe, expect, it, vi } from "vitest";
import { createAllegianceVaultApi } from "./allegianceVaultApi";
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

describe("createAllegianceVaultApi", () => {
  it("lists acting characters", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createAllegianceVaultApi(fakeHttpClient({ get }));

    await api.listActingCharacters();

    expect(get).toHaveBeenCalledWith("/allegiance-vault/acting-characters");
  });

  it("gets a character's vault by ID", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createAllegianceVaultApi(fakeHttpClient({ get }));

    await api.getVault(7001);

    expect(get).toHaveBeenCalledWith("/allegiance-vault?characterId=7001");
  });

  it("includes the page number when provided", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createAllegianceVaultApi(fakeHttpClient({ get }));

    await api.getVault(7001, 2);

    expect(get).toHaveBeenCalledWith("/allegiance-vault?characterId=7001&page=2");
  });

  it("contributes with the acting character and target", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createAllegianceVaultApi(fakeHttpClient({ post }));

    await api.contribute({ actingCharacterId: 7001, kind: "Item", itemBiotaId: 555 });

    expect(post).toHaveBeenCalledWith("/allegiance-vault/contribute", { actingCharacterId: 7001, kind: "Item", itemBiotaId: 555 });
  });

  it("takes with the acting character and target", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createAllegianceVaultApi(fakeHttpClient({ post }));

    await api.take({ actingCharacterId: 7001, kind: "Item", itemBiotaId: 555 });

    expect(post).toHaveBeenCalledWith("/allegiance-vault/take", { actingCharacterId: 7001, kind: "Item", itemBiotaId: 555 });
  });
});
