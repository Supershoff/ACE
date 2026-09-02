import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { ItemActionDialog } from "./ItemActionDialog";
import { fakeTransferOfferApi, fakeWithdrawalApi, makeInventoryItem } from "./testFakes";
import { SessionContext, type SessionContextValue } from "../session/SessionContext";

expect.extend(toHaveNoViolations);

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

function renderDialog(
  props: Partial<Parameters<typeof ItemActionDialog>[0]> = {},
  session: Partial<SessionContextValue> = {},
) {
  const transferOfferApi = fakeTransferOfferApi();
  const withdrawalApi = fakeWithdrawalApi();
  const onClose = vi.fn();
  const onCompleted = vi.fn();

  const utils = render(
    <SessionContext.Provider value={baseSession(session)}>
      <ItemActionDialog
        kind="transfer"
        item={makeInventoryItem()}
        transferOfferApi={transferOfferApi}
        withdrawalApi={withdrawalApi}
        onClose={onClose}
        onCompleted={onCompleted}
        {...props}
      />
    </SessionContext.Provider>,
  );

  return { ...utils, transferOfferApi, withdrawalApi, onClose, onCompleted };
}

describe("ItemActionDialog", () => {
  it("never shows or requires typing a raw item ID -- only the item's own name", () => {
    renderDialog({ item: makeInventoryItem({ itemId: 4242, name: "Ivory Buckler" }) });

    expect(screen.getByRole("heading", { name: "Send Ivory Buckler" })).toBeInTheDocument();
    expect(screen.queryByLabelText(/item id/i)).not.toBeInTheDocument();
    expect(screen.queryByText("4242")).not.toBeInTheDocument();
  });

  it("sends a Transfer Offer using the item's own ID from application state, not a typed value", async () => {
    const user = userEvent.setup();
    const { transferOfferApi, onClose, onCompleted } = renderDialog({
      kind: "transfer",
      item: makeInventoryItem({ itemId: 7, name: "Ivory Buckler" }),
    });

    await user.type(screen.getByLabelText("Recipient character name"), "Aluvia");
    await user.click(screen.getByRole("button", { name: "Send offer" }));

    expect(transferOfferApi.create).toHaveBeenCalledWith("Aluvia", [{ kind: "Item", itemBiotaId: 7 }]);
    await waitFor(() => expect(onCompleted).toHaveBeenCalled());
    expect(onClose).toHaveBeenCalled();
  });

  it("requires a recipient name before sending an offer", async () => {
    const user = userEvent.setup();
    const { transferOfferApi } = renderDialog({ kind: "transfer" });

    await user.click(screen.getByRole("button", { name: "Send offer" }));

    expect(screen.getByText("Enter a recipient character name.")).toBeInTheDocument();
    expect(transferOfferApi.create).not.toHaveBeenCalled();
  });

  it("creates a Withdrawal Token for a whole item and reveals the one-time secret", async () => {
    const user = userEvent.setup();
    const { withdrawalApi } = renderDialog({
      kind: "withdraw",
      item: makeInventoryItem({ itemId: 9, name: "Trade Note" }),
    });

    await user.click(screen.getByRole("button", { name: "Create Withdrawal Token" }));

    expect(withdrawalApi.create).toHaveBeenCalledWith([{ kind: "Item", itemBiotaId: 9 }]);
    expect(await screen.findByText("SECRET-TOKEN")).toBeInTheDocument();
  });

  it("splits a new stack lot for a partial quantity before withdrawing, never touching the original lot", async () => {
    const user = userEvent.setup();
    const { withdrawalApi } = renderDialog({
      kind: "withdraw",
      item: makeInventoryItem({ itemId: 9, stackLotId: "lot-1", quantity: 10, version: 3, name: "Trade Note" }),
    });

    const quantityInput = screen.getByLabelText("Quantity for Trade Note");
    fireEvent.change(quantityInput, { target: { value: "4" } });
    await user.click(screen.getByRole("button", { name: "Create Withdrawal Token" }));

    expect(withdrawalApi.splitStackLot).toHaveBeenCalledWith("lot-1", 3, 4);
    expect(withdrawalApi.create).toHaveBeenCalledWith([{ kind: "StackLot", stackLotId: "lot-new" }]);
  });

  it("sends the whole stack lot without splitting when the full quantity is requested", async () => {
    const user = userEvent.setup();
    const { withdrawalApi } = renderDialog({
      kind: "withdraw",
      item: makeInventoryItem({ itemId: 9, stackLotId: "lot-1", quantity: 10, version: 3, name: "Trade Note" }),
    });

    await user.click(screen.getByRole("button", { name: "Create Withdrawal Token" }));

    expect(withdrawalApi.splitStackLot).not.toHaveBeenCalled();
    expect(withdrawalApi.create).toHaveBeenCalledWith([{ kind: "StackLot", stackLotId: "lot-1" }]);
  });

  it("shows an actionable error instead of an unhandled failure when creation is rejected", async () => {
    const user = userEvent.setup();
    const withdrawalApi = fakeWithdrawalApi({
      create: vi.fn(async () => ({ ok: false, status: 409, error: { error: "conflict" } })),
    });
    render(
      <SessionContext.Provider value={baseSession()}>
        <ItemActionDialog
          kind="withdraw"
          item={makeInventoryItem()}
          transferOfferApi={fakeTransferOfferApi()}
          withdrawalApi={withdrawalApi}
          onClose={vi.fn()}
          onCompleted={vi.fn()}
        />
      </SessionContext.Provider>,
    );

    await user.click(screen.getByRole("button", { name: "Create Withdrawal Token" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/already has a pending action/i);
  });

  it("closes on Escape", async () => {
    const user = userEvent.setup();
    const { onClose } = renderDialog();

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("has no detectable accessibility violations", async () => {
    const { container } = renderDialog();
    expect(await axe(container)).toHaveNoViolations();
  });
});
