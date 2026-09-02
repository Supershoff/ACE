import { afterEach, describe, expect, it, vi } from "vitest";
import { createHttpClient } from "./httpClient";

function jsonResponse(body: unknown, init?: { status?: number }) {
  return new Response(JSON.stringify(body), {
    status: init?.status ?? 200,
    headers: { "content-type": "application/json" },
  });
}

describe("createHttpClient", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("always sends credentials so the HttpOnly session cookie is included", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    await client.get("/version");

    expect(fetchMock).toHaveBeenCalledWith(
      "https://cloud.example/version",
      expect.objectContaining({ credentials: "include" }),
    );
  });

  it("attaches the CSRF header on mutating requests when a token is available", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => "token-123" });
    await client.post("/auth/logout", undefined);

    const [, requestInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(requestInit.headers);
    expect(headers.get("X-Csrf-Token")).toBe("token-123");
  });

  it("never attaches a CSRF header to GET requests", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => "token-123" });
    await client.get("/version");

    const [, requestInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(requestInit.headers);
    expect(headers.has("X-Csrf-Token")).toBe(false);
  });

  it("omits the CSRF header on mutating requests when no token is known yet", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    await client.post("/auth/login", { accountName: "a", password: "b" });

    const [, requestInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(requestInit.headers);
    expect(headers.has("X-Csrf-Token")).toBe(false);
  });

  it("returns a typed successful result for a 2xx JSON response", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ csrfToken: "abc" }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.post<{ csrfToken: string }>("/auth/login", { accountName: "a", password: "b" });

    expect(result).toEqual({ ok: true, status: 200, data: { csrfToken: "abc" } });
  });

  it("returns a typed failure result with the parsed error body for a non-2xx response", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ error: "invalid_credentials" }, { status: 401 }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.post("/auth/login", { accountName: "a", password: "wrong" });

    expect(result).toEqual({ ok: false, status: 401, error: { error: "invalid_credentials" } });
  });

  it("returns a status-0 failure result when the network request itself throws", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("network down"));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.get("/version");

    expect(result.ok).toBe(false);
    expect(result.status).toBe(0);
  });

  it("returns ok:false and never exposes the raw body when a 2xx response is SPA HTML instead of JSON", async () => {
    // Regression for issue #39 / PR #157: an unmatched API path behind the local acceptance proxy
    // returned 200 text/html (the SPA shell), which httpClient previously typed as `data: T` as-is.
    const html = "<!doctype html><html><body>SPA shell</body></html>";
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(html, { status: 200, headers: { "content-type": "text/html; charset=utf-8" } }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.get<{ offers: unknown[] }>("/transfer-offers");

    expect(result.ok).toBe(false);
    expect(result.status).toBe(200);
    expect(result.data).toBeUndefined();
    expect(result.error).not.toContain(html);
    expect(result.error).not.toContain("<html>");
  });

  it("still returns a typed successful result with undefined data for a legitimate empty 2xx body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.post("/allegiance-vault/contribute", { amount: 1 });

    expect(result).toEqual({ ok: true, status: 204, data: undefined });
  });

  it("still returns the parsed error body for a non-2xx JSON error response", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ error: "not_found" }, { status: 404 }));
    vi.stubGlobal("fetch", fetchMock);

    const client = createHttpClient({ baseUrl: "https://cloud.example", getCsrfToken: () => null });
    const result = await client.get("/allegiance-vault");

    expect(result).toEqual({ ok: false, status: 404, error: { error: "not_found" } });
  });
});
