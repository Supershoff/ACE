import { describe, expect, it, vi } from "vitest";
import { createAccountApi } from "./accountApi";
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
  it("fetches /account/identity", async () => {
    const get = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { accountId: 42, accountKind: "Main", mainAccountId: 42, linkedAccounts: [], displayCharacter: null },
        }) as HttpResult<unknown>,
    );
    const accountApi = createAccountApi(fakeHttpClient({ get }));

    const result = await accountApi.fetchIdentity();

    expect(get).toHaveBeenCalledWith("/account/identity");
    expect(result.data).toMatchObject({ accountKind: "Main" });
  });

  it("posts the source account's own credentials to /account/link, never the Main account's", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as HttpResult<unknown>);
    const accountApi = createAccountApi(fakeHttpClient({ post }));

    await accountApi.link("mule1", "sourcepassword");

    expect(post).toHaveBeenCalledWith("/account/link", { sourceAccountName: "mule1", sourcePassword: "sourcepassword" });
  });

  it("posts the linked account ID to /account/unlink", async () => {
    const post = vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as HttpResult<unknown>);
    const accountApi = createAccountApi(fakeHttpClient({ post }));

    await accountApi.unlink(43);

    expect(post).toHaveBeenCalledWith("/account/unlink", { linkedAccountId: 43 });
  });
});
