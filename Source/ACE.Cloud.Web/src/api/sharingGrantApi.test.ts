import { describe, expect, it, vi } from "vitest";
import { createSharingGrantApi } from "./sharingGrantApi";
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

describe("createSharingGrantApi", () => {
  it("lists from /sharing-grants", async () => {
    const get = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createSharingGrantApi(fakeHttpClient({ get }));

    await api.list();

    expect(get).toHaveBeenCalledWith("/sharing-grants");
  });

  it("sets a grant with the grantee name and level", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createSharingGrantApi(fakeHttpClient({ post }));

    await api.set("Grantee", "ViewAndWithdraw");

    expect(post).toHaveBeenCalledWith("/sharing-grants", { granteeCharacterName: "Grantee", level: "ViewAndWithdraw" });
  });

  it("sets None as a real revocation, not a special-cased route", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const api = createSharingGrantApi(fakeHttpClient({ post }));

    await api.set("Grantee", "None");

    expect(post).toHaveBeenCalledWith("/sharing-grants", { granteeCharacterName: "Grantee", level: "None" });
  });
});
