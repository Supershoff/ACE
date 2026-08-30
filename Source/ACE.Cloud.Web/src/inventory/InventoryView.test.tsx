import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { InventoryView } from "./InventoryView";
import { fakeInventoryApi, makeInventoryItem, makeQueryResponse } from "./testFakes";
import type { WithdrawalApi } from "../api/withdrawalApi";
import type { HttpResult } from "../api/httpClient";

function fakeWithdrawalApi(overrides: Partial<WithdrawalApi> = {}): WithdrawalApi {
  return {
    openReservation: vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: new Date(Date.now() + 900_000).toISOString() },
        }) as HttpResult<unknown>,
    ),
    cancelReservation: vi.fn(async () => ({ ok: true, status: 200, data: { cancelled: true } }) as HttpResult<unknown>),
    ...overrides,
  } as WithdrawalApi;
}

describe("InventoryView", () => {
  it("shows a loading state and then the fetched Mule Page", async () => {
    const api = fakeInventoryApi();
    render(<InventoryView inventoryApi={api} />);

    expect(screen.getByRole("status")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("option", { name: "Ivory Buckler" })).toBeInTheDocument());
    expect(api.queryPages).toHaveBeenCalledWith(expect.objectContaining({ category: "Armor", page: 1 }));
  });

  it("shows a retryable error state when the query fails", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: false, status: 500, error: {} })),
    });
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
  });

  it("shows the Main Account-required message on a 403 from a linked-account session", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: false, status: 403, error: { error: "linked_account_restricted" } })),
    });
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/Main Account/));
  });

  it("shows an empty state when a Mule Page has no items", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({ ok: true, status: 200, data: makeQueryResponse([]) })),
    });
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => expect(screen.getByText("No items here")).toBeInTheDocument());
  });

  it("opens the Full Cloud Appraisal dialog when an item is activated", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(<InventoryView inventoryApi={api} />);

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
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: /Ivory Buckler/ }));
    await user.click(screen.getByRole("option", { name: /Ivory Buckler/ }));

    const quantityInput = await screen.findByLabelText("Quantity for Ivory Buckler");
    expect(quantityInput).toHaveValue(8);
  });

  it("switching to spreadsheet view still shows the same fetched items", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("button", { name: "Spreadsheet" }));

    expect(await screen.findByRole("table")).toHaveTextContent("Ivory Buckler");
  });

  it("changing category resets to page 1 and re-queries", async () => {
    const api = fakeInventoryApi();
    const user = userEvent.setup();
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.selectOptions(screen.getByLabelText("Category"), "Currency");

    await waitFor(() =>
      expect(api.queryPages).toHaveBeenLastCalledWith(expect.objectContaining({ category: "Currency", page: 1 })),
    );
  });

  it("opens the Withdrawal Token dialog for a withdrawable selection and creates a token", async () => {
    const api = fakeInventoryApi();
    const withdrawalApi = fakeWithdrawalApi();
    const user = userEvent.setup();
    render(<InventoryView inventoryApi={api} withdrawalApi={withdrawalApi} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("button", { name: /withdraw selected/i }));
    await user.click(screen.getByRole("button", { name: /create withdrawal token/i }));

    expect(withdrawalApi.openReservation).toHaveBeenCalledWith([{ kind: "Item", itemId: 1 }]);
    expect(await screen.findByTestId("withdrawal-token-secret")).toHaveTextContent("secret");
  });

  it("disables the Withdraw button when the selection includes a non-withdrawable item", async () => {
    const api = fakeInventoryApi({
      queryPages: vi.fn(async () => ({
        ok: true,
        status: 200,
        data: makeQueryResponse([makeInventoryItem({ permittedActions: { canWithdraw: false, canList: false, canTransfer: false, canShare: false } })]),
      })),
    });
    const user = userEvent.setup();
    render(<InventoryView inventoryApi={api} withdrawalApi={fakeWithdrawalApi()} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));
    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(screen.getByRole("button", { name: /withdraw selected/i })).toBeDisabled();
  });

  it("disables Previous page on the first page and Next page on the last page", async () => {
    const api = fakeInventoryApi();
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(screen.getByRole("button", { name: "Previous page" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
  });
});
