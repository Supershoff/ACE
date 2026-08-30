import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AccountLinkDialog } from "./AccountLinkDialog";
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

function fillForm(mainName: string, sourceName: string, sourcePassword: string) {
  fireEvent.change(screen.getByLabelText(/type your main account name/i), { target: { value: mainName } });
  fireEvent.change(screen.getByLabelText(/account name to link/i), { target: { value: sourceName } });
  fireEvent.change(screen.getByLabelText(/that account's password/i), { target: { value: sourcePassword } });
}

describe("AccountLinkDialog", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("keeps the confirm button disabled until the Main Account name is typed exactly", () => {
    render(
      <AccountLinkDialog
        open
        onClose={() => {}}
        onLinked={() => {}}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi()}
      />,
    );

    act(() => {
      vi.advanceTimersByTime(10_000);
    });

    const confirmButton = screen.getByRole("button", { name: /link account/i });
    expect(confirmButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/type your main account name/i), { target: { value: "WrongName" } });
    expect(confirmButton).toBeDisabled();
  });

  it("keeps the confirm button disabled for the first 10 seconds even with a correct name and password (AUTH-007)", () => {
    render(
      <AccountLinkDialog
        open
        onClose={() => {}}
        onLinked={() => {}}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi()}
      />,
    );

    fillForm("MainPlayer", "SourcePlayer", "hunter2");

    expect(screen.getByRole("button", { name: /link account/i })).toBeDisabled();

    act(() => {
      vi.advanceTimersByTime(9_000);
    });
    expect(screen.getByRole("button", { name: /link account/i })).toBeDisabled();

    act(() => {
      vi.advanceTimersByTime(1_000);
    });
    expect(screen.getByRole("button", { name: /^link account$/i })).toBeEnabled();
  });

  it("submits the source credentials and calls onLinked on success", async () => {
    const link = vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>);
    const onLinked = vi.fn();
    render(
      <AccountLinkDialog
        open
        onClose={() => {}}
        onLinked={onLinked}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi({ link })}
      />,
    );

    fillForm("MainPlayer", "SourcePlayer", "hunter2");
    act(() => {
      vi.advanceTimersByTime(10_000);
    });

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /^link account$/i }));
    });

    expect(link).toHaveBeenCalledWith("SourcePlayer", "hunter2");
    expect(onLinked).toHaveBeenCalled();
  });

  it("shows the exact rejection reason and never calls onLinked when the server refuses the link", async () => {
    const link = vi.fn(
      async () =>
        ({ ok: false, status: 409, error: { error: "link_rejected", reason: "SourceHasPendingObligations" } }) as HttpResult<unknown>,
    );
    const onLinked = vi.fn();
    render(
      <AccountLinkDialog
        open
        onClose={() => {}}
        onLinked={onLinked}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi({ link })}
      />,
    );

    fillForm("MainPlayer", "SourcePlayer", "hunter2");
    act(() => {
      vi.advanceTimersByTime(10_000);
    });

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /^link account$/i }));
    });

    expect(screen.getByRole("alert")).toHaveTextContent(/pending reservation/i);
    expect(onLinked).not.toHaveBeenCalled();
  });

  it("resets the delay and every field each time the dialog reopens", () => {
    const { rerender } = render(
      <AccountLinkDialog
        open={false}
        onClose={() => {}}
        onLinked={() => {}}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi()}
      />,
    );

    rerender(
      <AccountLinkDialog
        open
        onClose={() => {}}
        onLinked={() => {}}
        mainAccountName="MainPlayer"
        accountApi={fakeAccountApi()}
      />,
    );

    expect(screen.getByRole("button", { name: /link account \(10\)/i })).toBeDisabled();
  });
});
