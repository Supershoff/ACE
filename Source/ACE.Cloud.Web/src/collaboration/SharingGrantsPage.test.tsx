import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { SharingGrantsPage } from "./SharingGrantsPage";
import type { SharingGrantApi, CloudSharingGrantListResponse, CloudSharingGrantSummary } from "../api/sharingGrantApi";
import type { HttpResult } from "../api/httpClient";
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

const givenGrant: CloudSharingGrantSummary = {
  id: "22222222-2222-2222-2222-222222222222",
  ownerId: "owner-1",
  granteeId: "grantee-1",
  level: "ViewOnly",
  version: 1,
  updatedAtUtc: "2026-01-01T00:00:00Z",
};

function fakeApi(overrides: Partial<SharingGrantApi> = {}): SharingGrantApi {
  const listResponse: CloudSharingGrantListResponse = { given: [givenGrant], received: [] };
  return {
    list: vi.fn(async () => ({ ok: true, status: 200, data: listResponse }) as HttpResult<CloudSharingGrantListResponse>),
    set: vi.fn(async () => ({ ok: true, status: 200, data: givenGrant }) as HttpResult<CloudSharingGrantSummary>),
    ...overrides,
  };
}

function renderPage(sharingGrantApi: SharingGrantApi) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <SharingGrantsPage sharingGrantApi={sharingGrantApi} />
    </SessionContext.Provider>,
  );
}

describe("SharingGrantsPage", () => {
  it("lists given grants", async () => {
    renderPage(fakeApi());

    expect(await screen.findByText(/ViewOnly/)).toBeInTheDocument();
  });

  it("shows an empty state for received when there are none", async () => {
    renderPage(fakeApi());

    expect(await screen.findByText("No grants received.")).toBeInTheDocument();
  });

  it("setting a grant calls the API with the typed grantee and selected level, including None", async () => {
    const user = userEvent.setup();
    const api = fakeApi();
    renderPage(api);
    await screen.findByText(/ViewOnly/);

    await user.type(screen.getByLabelText("Grantee character name"), "SomeCharacter");
    await user.selectOptions(screen.getByLabelText("Access level"), "None");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(api.set).toHaveBeenCalledWith("SomeCharacter", "None");
  });

  it("has no detectable axe violations", async () => {
    const { container } = renderPage(fakeApi());
    await screen.findByText(/ViewOnly/);

    expect(await axe(container)).toHaveNoViolations();
  });
});
