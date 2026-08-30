import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountUnlinkDialog } from "./AccountUnlinkDialog";
import type { AccountApi } from "../api/accountApi";
import type { HttpResult } from "../api/httpClient";

type FakeAccountApiOverrides = Partial<{ [K in keyof AccountApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

function fakeAccountApi(overrides: FakeAccountApiOverrides = {}): AccountApi {
  return {
    fetchOverview: vi.fn(async () => ({ ok: true, status: 200, data: { isLinkedAccount: false } }) as HttpResult<unknown>),
    link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    unlink: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as AccountApi;
}

describe("AccountUnlinkDialog", () => {
  it("shows the irreversibility warning", () => {
    render(
      <AccountUnlinkDialog open onClose={() => {}} onUnlinked={() => {}} linkedAccountId={77} accountApi={fakeAccountApi()} />,
    );

    expect(screen.getByText(/never moved back/i)).toBeInTheDocument();
  });

  it("calls unlink with the linked account ID and onUnlinked on success", async () => {
    const unlink = vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>);
    const onUnlinked = vi.fn();
    render(
      <AccountUnlinkDialog
        open
        onClose={() => {}}
        onUnlinked={onUnlinked}
        linkedAccountId={77}
        accountApi={fakeAccountApi({ unlink })}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /unlink account/i }));
    });

    expect(unlink).toHaveBeenCalledWith(77);
    expect(onUnlinked).toHaveBeenCalled();
  });

  it("shows an error and never calls onUnlinked when the server refuses", async () => {
    const unlink = vi.fn(async () => ({ ok: false, status: 403, error: {} }) as HttpResult<unknown>);
    const onUnlinked = vi.fn();
    render(
      <AccountUnlinkDialog
        open
        onClose={() => {}}
        onUnlinked={onUnlinked}
        linkedAccountId={77}
        accountApi={fakeAccountApi({ unlink })}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /unlink account/i }));
    });

    expect(screen.getByRole("alert")).toHaveTextContent(/only the main account/i);
    expect(onUnlinked).not.toHaveBeenCalled();
  });
});
