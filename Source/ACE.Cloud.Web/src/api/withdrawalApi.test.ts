import { describe, expect, it, vi } from "vitest";
import { IdempotencyKeyHeaderName } from "./constants";
import type { HttpClient, HttpResult } from "./httpClient";
import { createWithdrawalApi } from "./withdrawalApi";

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
  it("posts the requested targets to /withdrawal/reservations with a fresh idempotency key", async () => {
    const post = vi.fn(
      async (..._args: unknown[]) =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: "2026-01-01T00:15:00Z" },
        }) as HttpResult<unknown>,
    );
    const withdrawalApi = createWithdrawalApi(fakeHttpClient({ post }));

    const result = await withdrawalApi.openReservation([{ kind: "Item", itemId: 1234 }]);

    const [path, body, headers] = post.mock.calls[0] as [string, unknown, Record<string, string>];
    expect(path).toBe("/withdrawal/reservations");
    expect(body).toEqual({ targets: [{ kind: "Item", itemId: 1234 }] });
    expect(headers[IdempotencyKeyHeaderName]).toBeTruthy();
    expect(result.data?.tokenSecret).toBe("secret");
  });

  it("posts the expected version to the reservation's cancel route", async () => {
    const post = vi.fn(async (..._args: unknown[]) => ({ ok: true, status: 200, data: { cancelled: true } }) as HttpResult<unknown>);
    const withdrawalApi = createWithdrawalApi(fakeHttpClient({ post }));

    await withdrawalApi.cancelReservation("r1", 1);

    const [path, body] = post.mock.calls[0] as [string, unknown];
    expect(path).toBe("/withdrawal/reservations/r1/cancel");
    expect(body).toEqual({ expectedVersion: 1 });
  });
});
