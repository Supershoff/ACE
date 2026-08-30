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
});
