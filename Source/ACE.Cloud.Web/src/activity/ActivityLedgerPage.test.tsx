import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { ActivityLedgerPage } from "./ActivityLedgerPage";
import type { ActivityApi } from "../api/activityApi";
import type { HttpResult } from "../api/httpClient";
import type { CloudActivityLedgerEntry, CloudActivityLedgerPageResponse } from "../api/types";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

function baseSession(): SessionContextValue {
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
  };
}

const sampleEntry: CloudActivityLedgerEntry = {
  id: "e1",
  correlationId: "c1",
  category: "CustodyBoundary",
  eventType: "Deposit",
  ownerId: "owner-1",
  itemBiotaId: 777,
  outcome: "Committed",
  reason: null,
  occurredAtUtc: "2026-01-01T00:00:00Z",
};

function fakeActivityApi(overrides: Partial<ActivityApi> = {}): ActivityApi {
  const response: CloudActivityLedgerPageResponse = {
    entries: [sampleEntry],
    pageNumber: 1,
    pageSize: 25,
    totalCount: 1,
    totalPages: 1,
  };
  return {
    queryLedger: vi.fn(async () => ({ ok: true, status: 200, data: response }) as HttpResult<CloudActivityLedgerPageResponse>),
    ...overrides,
  };
}

function renderPage(activityApi: ActivityApi) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <ActivityLedgerPage activityApi={activityApi} />
    </SessionContext.Provider>,
  );
}

describe("ActivityLedgerPage", () => {
  it("lists the viewer's ledger entries", async () => {
    renderPage(fakeActivityApi());

    expect(await screen.findByText("Deposit")).toBeInTheDocument();
    expect(screen.getByText("777")).toBeInTheDocument();
  });

  it("shows an empty state when there is no activity yet", async () => {
    const api = fakeActivityApi({
      queryLedger: vi.fn(
        async () =>
          ({ ok: true, status: 200, data: { entries: [], pageNumber: 1, pageSize: 25, totalCount: 0, totalPages: 0 } }) as HttpResult<CloudActivityLedgerPageResponse>,
      ),
    });
    renderPage(api);

    expect(await screen.findByText("No activity yet.")).toBeInTheDocument();
  });

  it("shows a retry-capable error state when the query fails", async () => {
    const api = fakeActivityApi({
      queryLedger: vi.fn(async () => ({ ok: false, status: 500 }) as HttpResult<CloudActivityLedgerPageResponse>),
    });
    renderPage(api);

    expect(await screen.findByRole("alert")).toHaveTextContent("Activity unavailable");
  });

  it("requeries with vault=true when the Allegiance Vault toggle is checked", async () => {
    const user = userEvent.setup();
    const api = fakeActivityApi();
    renderPage(api);

    await screen.findByText("Deposit");
    await user.click(screen.getByRole("checkbox", { name: /include allegiance vault activity/i }));

    await waitFor(() => expect(api.queryLedger).toHaveBeenLastCalledWith({ page: 1, vault: true }));
  });

  it("has no detectable axe violations", async () => {
    const { container } = renderPage(fakeActivityApi());
    await screen.findByText("Deposit");

    expect(await axe(container)).toHaveNoViolations();
  });
});
