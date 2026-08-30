import { describe, expect, it, vi } from "vitest";
import { createAccountApi } from "./accountApi";
import { IdempotencyKeyHeaderName } from "./constants";
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

describe("createAccountApi", () => {
  it("fetches /account/overview", async () => {
    const get = vi.fn(
      async () => ({ ok: true, status: 200, data: { isLinkedAccount: false, mainAccountId: 42 } }) as HttpResult<unknown>,
    );
    const accountApi = createAccountApi(fakeHttpClient({ get }));

    const result = await accountApi.fetchOverview();

    expect(get).toHaveBeenCalledWith("/account/overview");
    expect(result.data).toEqual({ isLinkedAccount: false, mainAccountId: 42 });
  });

  it("posts source credentials to /account/link with a fresh idempotency key", async () => {
    const post = vi.fn(async (..._args: unknown[]) => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>);
    const accountApi = createAccountApi(fakeHttpClient({ post }));

    await accountApi.link("SourceAccount", "hunter2");

    expect(post).toHaveBeenCalledTimes(1);
    const [path, body, headers] = post.mock.calls[0] as [string, unknown, Record<string, string>];
    expect(path).toBe("/account/link");
    expect(body).toEqual({ sourceAccountName: "SourceAccount", sourcePassword: "hunter2" });
    expect(headers[IdempotencyKeyHeaderName]).toBeTruthy();
  });

  it("uses a different idempotency key on every link call, so retries never silently dedupe two distinct attempts", async () => {
    const post = vi.fn(async (..._args: unknown[]) => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>);
    const accountApi = createAccountApi(fakeHttpClient({ post }));

    await accountApi.link("SourceAccount", "hunter2");
    await accountApi.link("SourceAccount", "hunter2");

    const firstHeaders = post.mock.calls[0]![2] as Record<string, string>;
    const secondHeaders = post.mock.calls[1]![2] as Record<string, string>;
    expect(firstHeaders[IdempotencyKeyHeaderName]).not.toBe(secondHeaders[IdempotencyKeyHeaderName]);
  });

  it("posts the linked account ID to /account/unlink", async () => {
    const post = vi.fn(async (..._args: unknown[]) => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>);
    const accountApi = createAccountApi(fakeHttpClient({ post }));

    await accountApi.unlink(77);

    const [path, body] = post.mock.calls[0] as [string, unknown];
    expect(path).toBe("/account/unlink");
    expect(body).toEqual({ linkedAccountId: 77 });
  });
});
