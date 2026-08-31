import { render, screen, waitFor } from "@testing-library/react";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { InventoryView } from "./InventoryView";
import { fakeInventoryApi } from "./testFakes";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

expect.extend(toHaveNoViolations);

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

describe("InventoryView accessibility", () => {
  it("has no detectable accessibility violations once loaded", async () => {
    const api = fakeInventoryApi();
    const { container } = render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has an accessible name for the category selector and view switcher", async () => {
    const api = fakeInventoryApi();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(screen.getByLabelText("Category")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Grid" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Spreadsheet" })).toHaveAttribute("aria-pressed", "false");
  });
});
