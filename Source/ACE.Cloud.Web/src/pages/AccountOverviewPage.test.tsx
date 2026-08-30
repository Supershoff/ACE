import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountOverviewPage } from "./AccountOverviewPage";
import type { AccountApi } from "../api/accountApi";
import type { CloudAccountIdentityResponse } from "../api/types";
import type { WithdrawalApi } from "../api/withdrawalApi";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

function baseSession(): SessionContextValue {
  return {
    status: "authenticated",
    csrfToken: "csrf",
    accountKind: "Main",
    accountName: "MainPlayer",
    serviceAvailability: "Operational",
    login: vi.fn(),
    logout: vi.fn(),
    checkAdminAccess: vi.fn(async () => ({ checked: true, isAdmin: false, accessLevel: null })),
  };
}

function fakeWithdrawalApi(): WithdrawalApi {
  return {
    fetchLocations: vi.fn(async () => ({ ok: true, status: 200, data: { withdrawAnywhereEnabled: false, namedLandblocks: [] } }) as never),
    fetchCurrent: vi.fn(async () => ({ ok: true, status: 200, data: { active: false } }) as never),
    create: vi.fn(),
    cancel: vi.fn(),
    splitStackLot: vi.fn(),
  };
}

function renderPage(accountApi: AccountApi) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <AccountOverviewPage accountApi={accountApi} withdrawalApi={fakeWithdrawalApi()} />
    </SessionContext.Provider>,
  );
}

describe("AccountOverviewPage", () => {
  it("shows the account's Display Character once identity loads", async () => {
    const identity: CloudAccountIdentityResponse = {
      accountId: 42,
      accountKind: "Main",
      mainAccountId: 42,
      linkedAccounts: [],
      displayCharacter: { characterId: 1, characterName: "Sir Testalot" },
    };
    const accountApi: AccountApi = {
      fetchIdentity: vi.fn(async () => ({ ok: true, status: 200, data: identity }) as never),
      link: vi.fn(),
      unlink: vi.fn(),
    };

    renderPage(accountApi);

    await waitFor(() => expect(screen.getByText(/Sir Testalot/)).toBeInTheDocument());
    expect(screen.getByRole("heading", { name: /linked accounts/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /withdrawal token/i })).toBeInTheDocument();
  });

  it("shows an explicit error with retry when identity can't be loaded", async () => {
    const accountApi: AccountApi = {
      fetchIdentity: vi.fn(async () => ({ ok: false, status: 401, error: { error: "unauthenticated" } }) as never),
      link: vi.fn(),
      unlink: vi.fn(),
    };

    renderPage(accountApi);

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });
});
