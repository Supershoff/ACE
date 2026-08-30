import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountOverviewPage } from "./AccountOverviewPage";
import type { AccountApi } from "../api/accountApi";
import type { HttpResult } from "../api/httpClient";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

type FakeAccountApiOverrides = Partial<{ [K in keyof AccountApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

function fakeAccountApi(overrides: FakeAccountApiOverrides = {}): AccountApi {
  return {
    fetchOverview: vi.fn(async () => ({ ok: true, status: 200, data: { isLinkedAccount: false, mainAccountId: 42, linkedAccountIds: [] } }) as HttpResult<unknown>),
    link: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    unlink: vi.fn(async () => ({ ok: true, status: 200, data: { approved: true } }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as AccountApi;
}

function baseSessionValue(overrides: Partial<SessionContextValue> = {}): SessionContextValue {
  return {
    status: "authenticated",
    csrfToken: "csrf",
    accountKind: "Main",
    mainAccountName: "MainPlayer",
    serviceAvailability: "Operational",
    login: vi.fn(async () => ({ ok: true })),
    logout: vi.fn(async () => {}),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
    ...overrides,
  };
}

function renderPage(accountApi: AccountApi, session: SessionContextValue = baseSessionValue()) {
  return render(
    <SessionContext.Provider value={session}>
      <AccountOverviewPage accountApi={accountApi} />
    </SessionContext.Provider>,
  );
}

describe("AccountOverviewPage", () => {
  it("shows the Main Account's linked accounts and Display Character once loaded", async () => {
    const accountApi = fakeAccountApi({
      fetchOverview: vi.fn(
        async () =>
          ({
            ok: true,
            status: 200,
            data: { isLinkedAccount: false, mainAccountId: 42, linkedAccountIds: [77], displayCharacter: { characterId: 900, characterName: "Bob", totalLogins: 12 } },
          }) as HttpResult<unknown>,
      ),
    });
    renderPage(accountApi);

    await waitFor(() => expect(screen.getByText("Bob")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /unlink/i })).toBeInTheDocument();
  });

  it("shows a restricted message for a Linked Account session instead of Main-only content", async () => {
    const accountApi = fakeAccountApi({
      fetchOverview: vi.fn(async () => ({ ok: true, status: 200, data: { isLinkedAccount: true } }) as HttpResult<unknown>),
    });
    renderPage(accountApi);

    await waitFor(() => expect(screen.getByText(/linked account/i)).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /link another account/i })).not.toBeInTheDocument();
  });

  it("shows an error state with retry when the overview fails to load", async () => {
    const accountApi = fakeAccountApi({
      fetchOverview: vi.fn(async () => ({ ok: false, status: 500, error: {} }) as HttpResult<unknown>),
    });
    renderPage(accountApi);

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/unavailable/i));
  });
});
