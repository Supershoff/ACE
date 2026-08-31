import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { InventoryView } from "./InventoryView";
import { mockMatchMedia } from "../test/mockMatchMedia";
import { fakeInventoryApi, makeInventoryItem, makeQueryResponse } from "./testFakes";
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

function renderInventoryView(api: ReturnType<typeof fakeInventoryApi>) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <InventoryView inventoryApi={api} />
    </SessionContext.Provider>,
  );
}

/**
 * UI-003: "Narrow layouts may reflow without changing page membership under the current
 * sort/filter." Reflow must never re-query or change which items are on this page -- only the
 * visual column count changes.
 */
describe("InventoryView responsive reflow", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("requests the same page contents at narrow and desktop widths", async () => {
    const items = [makeInventoryItem({ itemId: 1, name: "First" }), makeInventoryItem({ itemId: 2, name: "Second" })];
    const api = fakeInventoryApi({ queryPages: vi.fn(async () => ({ ok: true, status: 200, data: makeQueryResponse(items) })) });

    mockMatchMedia([]);
    const { unmount } = renderInventoryView(api);
    await waitFor(() => screen.getByRole("option", { name: "First" }));
    const desktopOptionCount = screen.getAllByRole("option").length;
    unmount();

    mockMatchMedia(["max-width"]);
    renderInventoryView(api);
    await waitFor(() => screen.getByRole("option", { name: "First" }));
    const narrowOptionCount = screen.getAllByRole("option").length;

    expect(narrowOptionCount).toBe(desktopOptionCount);
    expect(screen.getByRole("option", { name: "Second" })).toBeInTheDocument();
  });

  it("preserves every operation at a narrow width: activating an item still opens its appraisal", async () => {
    const api = fakeInventoryApi();
    mockMatchMedia(["max-width"]);
    renderInventoryView(api);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    const { default: userEvent } = await import("@testing-library/user-event");
    const user = userEvent.setup();
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));

    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());
  });
});
