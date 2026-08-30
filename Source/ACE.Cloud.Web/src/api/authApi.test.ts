import { describe, expect, it, vi } from "vitest";
import { createAuthApi } from "./authApi";
import type { HttpClient, HttpResult } from "./httpClient";

/**
 * `HttpClient.get`/`post` are genuinely generic (`<T>(path) => Promise<HttpResult<T>>`), so a
 * fixed-return mock can never structurally satisfy them for every possible `T`. Test doubles are
 * intentionally loosely typed here and cast once at the boundary instead of fighting that.
 */
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

describe("createAuthApi", () => {
  it("posts credentials to /auth/login and returns the CSRF token on success", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: { csrfToken: "csrf-token" } }) as HttpResult<unknown>);
    const authApi = createAuthApi(fakeHttpClient({ post }));

    const result = await authApi.login("PlayerOne", "hunter2");

    expect(post).toHaveBeenCalledWith("/auth/login", { accountName: "PlayerOne", password: "hunter2" });
    expect(result).toEqual({ ok: true, status: 200, data: { csrfToken: "csrf-token" } });
  });

  it("surfaces a failed login without leaking which part of the credential was wrong", async () => {
    const post = vi.fn(
      async () => ({ ok: false, status: 401, error: { error: "invalid_credentials" } }) as HttpResult<unknown>,
    );
    const authApi = createAuthApi(fakeHttpClient({ post }));

    const result = await authApi.login("PlayerOne", "wrong-password");

    expect(result.ok).toBe(false);
  });

  it("posts to /auth/logout with no body", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: {} }) as HttpResult<unknown>);
    const authApi = createAuthApi(fakeHttpClient({ post }));

    await authApi.logout();

    expect(post).toHaveBeenCalledWith("/auth/logout", undefined);
  });

  it("fetches /admin/whoami and passes through the account/access-level payload", async () => {
    const get = vi.fn(
      async () => ({ ok: true, status: 200, data: { accountId: 42, accessLevel: 5 } }) as HttpResult<unknown>,
    );
    const authApi = createAuthApi(fakeHttpClient({ get }));

    const result = await authApi.fetchAdminWhoAmI();

    expect(get).toHaveBeenCalledWith("/admin/whoami");
    expect(result).toEqual({ ok: true, status: 200, data: { accountId: 42, accessLevel: 5 } });
  });

  it("fetches /health/ready and passes through the service availability mode", async () => {
    const get = vi.fn(
      async () =>
        ({ ok: true, status: 200, data: { mode: "Operational", results: [] } }) as HttpResult<unknown>,
    );
    const authApi = createAuthApi(fakeHttpClient({ get }));

    const result = await authApi.fetchHealthReady();

    expect(get).toHaveBeenCalledWith("/health/ready");
    expect(result).toEqual({ ok: true, status: 200, data: { mode: "Operational", results: [] } });
  });

  it("fetches /version", async () => {
    const get = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { aceExtensionVersion: "1", cloudSchemaVersion: "1", contractProtocolVersion: "1" },
        }) as HttpResult<unknown>,
    );
    const authApi = createAuthApi(fakeHttpClient({ get }));

    await authApi.fetchVersion();

    expect(get).toHaveBeenCalledWith("/version");
  });
});
