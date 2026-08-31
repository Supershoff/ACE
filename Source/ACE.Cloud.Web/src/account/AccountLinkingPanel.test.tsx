import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountLinkingPanel } from "./AccountLinkingPanel";
import type { AccountApi } from "../api/accountApi";
import type { HttpResult } from "../api/httpClient";
import type { CloudAccountIdentityResponse } from "../api/types";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

function baseSession(overrides: Partial<SessionContextValue> = {}): SessionContextValue {
  return {
    status: "authenticated",
    csrfToken: "csrf",
    accountKind: "Main",
    accountName: "MainPlayer",
    serviceAvailability: "Operational",
    liveStream: { status: "idle", stale: false },
    login: vi.fn(),
    logout: vi.fn(),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
    subscribeLiveStream: vi.fn(() => vi.fn()),
    ...overrides,
  };
}

function identity(overrides: Partial<CloudAccountIdentityResponse> = {}): CloudAccountIdentityResponse {
  return {
    accountId: 42,
    accountKind: "Main",
    mainAccountId: 42,
    linkedAccounts: [],
    displayCharacter: null,
    ...overrides,
  };
}

function fakeAccountApi(overrides: Partial<AccountApi> = {}): AccountApi {
  return {
    fetchIdentity: vi.fn(async () => ({ ok: true, status: 200, data: identity() }) as HttpResult<CloudAccountIdentityResponse>),
    link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as never),
    unlink: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true, rejectionCode: "None" } }) as never),
    ...overrides,
  };
}

function renderPanel(identityValue: CloudAccountIdentityResponse, accountApi: AccountApi, session = baseSession()) {
  return render(
    <SessionContext.Provider value={session}>
      <AccountLinkingPanel identity={identityValue} accountApi={accountApi} onChanged={vi.fn()} />
    </SessionContext.Provider>,
  );
}

function openLinkConfirmation() {
  fireEvent.change(screen.getByLabelText(/account name to link/i), { target: { value: "mule1" } });
  fireEvent.change(screen.getByLabelText(/that account's password/i), { target: { value: "sourcepassword" } });
  fireEvent.click(screen.getByRole("button", { name: /^link account$/i }));
}

describe("AccountLinkingPanel", () => {
  it("lists every currently active linked account with an Unlink action", () => {
    renderPanel(identity({ linkedAccounts: [{ accountId: 43, linkedAtUtc: "2026-01-01T00:00:00Z" }] }), fakeAccountApi());

    expect(screen.getByText(/#43/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /unlink/i })).toBeInTheDocument();
  });

  it("keeps the destructive link confirmation disabled until the delay elapses and the Main account name is typed exactly", () => {
    vi.useFakeTimers();
    renderPanel(identity(), fakeAccountApi());

    openLinkConfirmation();

    const confirmButton = screen.getByRole("button", { name: /confirm link/i });
    expect(confirmButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/your main account name/i), { target: { value: "MainPlayer" } });
    expect(confirmButton).toBeDisabled(); // delay has not elapsed yet

    // AUTH-007 requires an approximately 10-second delay, not merely a token cooldown.
    act(() => vi.advanceTimersByTime(9000));
    expect(confirmButton).toBeDisabled();

    act(() => vi.advanceTimersByTime(1000));
    expect(confirmButton).not.toBeDisabled();

    vi.useRealTimers();
  });

  it("requires the typed name to exactly match the session's own Main account name", () => {
    vi.useFakeTimers();
    renderPanel(identity(), fakeAccountApi());

    openLinkConfirmation();
    fireEvent.change(screen.getByLabelText(/your main account name/i), { target: { value: "NotTheMainAccount" } });
    act(() => vi.advanceTimersByTime(10000));

    expect(screen.getByRole("button", { name: /confirm link/i })).toBeDisabled();

    vi.useRealTimers();
  });

  it("shows the exact blocked-link reason when the server rejects", async () => {
    vi.useFakeTimers();
    const accountApi = fakeAccountApi({
      link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: false, rejectionCode: "SourceAlreadyLinked" } }) as never),
    });
    renderPanel(identity(), accountApi);

    openLinkConfirmation();
    fireEvent.change(screen.getByLabelText(/your main account name/i), { target: { value: "MainPlayer" } });
    act(() => vi.advanceTimersByTime(10000));

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /confirm link/i }));
    });

    expect(screen.getByRole("alert")).toHaveTextContent(/already linked/i);

    vi.useRealTimers();
  });

  it("never sends the Main account's own password when linking -- only the source account's", async () => {
    vi.useFakeTimers();
    const accountApi = fakeAccountApi();
    renderPanel(identity(), accountApi);

    openLinkConfirmation();
    fireEvent.change(screen.getByLabelText(/your main account name/i), { target: { value: "MainPlayer" } });
    act(() => vi.advanceTimersByTime(10000));

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /confirm link/i }));
    });

    expect(accountApi.link).toHaveBeenCalledWith("mule1", "sourcepassword");

    vi.useRealTimers();
  });

  it("keeps the unlink confirmation disabled until the delay elapses, warning that it is irreversible", () => {
    vi.useFakeTimers();
    renderPanel(identity({ linkedAccounts: [{ accountId: 43, linkedAtUtc: "2026-01-01T00:00:00Z" }] }), fakeAccountApi());

    fireEvent.click(screen.getByRole("button", { name: /unlink/i }));

    expect(screen.getByRole("dialog")).toHaveTextContent(/does not restore/i);
    const confirmUnlink = screen.getByRole("button", { name: /confirm unlink/i });
    expect(confirmUnlink).toBeDisabled();

    // AUTH-007 requires an approximately 10-second delay, not merely a token cooldown.
    act(() => vi.advanceTimersByTime(9000));
    expect(confirmUnlink).toBeDisabled();

    act(() => vi.advanceTimersByTime(1000));
    expect(confirmUnlink).not.toBeDisabled();

    vi.useRealTimers();
  });
});
