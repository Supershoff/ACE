import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { InventoryView } from "./InventoryView";
import { fakeInventoryApi, fakeTransferOfferApi, fakeWithdrawalApi, makeInventoryItem, makeQueryResponse } from "./testFakes";
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

describe("InventoryView", () => {
  it("shows a loading state and then the fetched Mule Page", async () => {
    const api = fakeInventoryApi();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    expect(screen.getByRole("status")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("option", { name: "Ivory Buckler" })).toBeInTheDocument());
    expect(api.queryPages).toHaveBeenCalledWith(expect.objectContaining({ category: "Armor", page: 1 }));
  });

  it("shows a retryable error state when the query fails", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: false, status: 500, error: {} })),
    });
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
  });

  it("shows the Main Account-required message on a 403 from a linked-account session", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: false, status: 403, error: { error: "linked_account_restricted" } })),
    });
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/Main Account/));
  });

  it("shows an empty state when a Mule Page has no items", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: true, status: 200, data: makeQueryResponse([]) })),
    });
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => expect(screen.getByText("No items here")).toBeInTheDocument());
  });

  it("opens the Full Cloud Appraisal dialog when an item is activated", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));

    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());
    expect(api.fetchAppraisal).toHaveBeenCalledWith(1);
    await waitFor(() => expect(screen.getByText("Value: 100")).toBeInTheDocument());
  });

  it("shows an inline quantity control defaulted to full when a stack is selected", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: true, status: 200, data: makeQueryResponse([makeInventoryItem({ quantity: 8 })]) })),
    });
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: /Ivory Buckler/ }));
    await user.click(screen.getByRole("option", { name: /Ivory Buckler/ }));

    const quantityInput = await screen.findByLabelText("Quantity for Ivory Buckler");
    expect(quantityInput).toHaveValue(8);
  });

  it("switching to spreadsheet view still shows the same fetched items", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("button", { name: "Spreadsheet" }));

    expect(await screen.findByRole("table")).toHaveTextContent("Ivory Buckler");
  });

  it("changing category resets to page 1 and re-queries", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.selectOptions(screen.getByLabelText("Category"), "Currency");

    await waitFor(() =>
      expect(api.queryPages).toHaveBeenLastCalledWith(expect.objectContaining({ category: "Currency", page: 1 })),
    );
  });

  it("offers Transfer/Withdraw contextual actions for a Main Account with permitted actions, and supplies the item ID from state, never a typed value", async () => {
    const api = fakeInventoryApi();
    const transferOfferApi = fakeTransferOfferApi();
    const withdrawalApi = fakeWithdrawalApi();
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} transferOfferApi={transferOfferApi} withdrawalApi={withdrawalApi} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Actions" }));
    await user.click(screen.getByRole("menuitem", { name: "Send Transfer Offer…" }));

    expect(screen.queryByLabelText(/item id/i)).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("Recipient character name"), "Aluvia");
    await user.click(screen.getByRole("button", { name: "Send offer" }));

    expect(transferOfferApi.create).toHaveBeenCalledWith("Aluvia", [{ kind: "Item", itemBiotaId: 1 }]);
  });

  it("hides contextual Transfer/Withdraw actions entirely for a Linked account, even with an item selected", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession({ accountKind: "Linked" })}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    expect(screen.queryByRole("button", { name: "Actions" })).not.toBeInTheDocument();
  });

  it("only offers the action(s) the item's own permittedActions allow", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({
        ok: true,
        status: 200,
        data: makeQueryResponse([
          makeInventoryItem({ permittedActions: { canWithdraw: false, canList: true, canTransfer: true, canShare: true } }),
        ]),
      })),
    });
    const user = userEvent.setup();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Actions" }));
    expect(screen.getByRole("menuitem", { name: "Send Transfer Offer…" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Create Withdrawal Token…" })).not.toBeInTheDocument();
  });

  it("disables Previous page on the first page and Next page on the last page", async () => {
    const api = fakeInventoryApi();
    render(
      <SessionContext.Provider value={baseSession()}>
        <InventoryView inventoryApi={api} />
      </SessionContext.Provider>,
    );

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(screen.getByRole("button", { name: "Previous page" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
  });
});
