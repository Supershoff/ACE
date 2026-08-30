import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WithdrawalDialog, type WithdrawalSelectionEntry } from "./WithdrawalDialog";
import type { WithdrawalApi } from "../api/withdrawalApi";
import type { CloudInventoryItem } from "../api/types";
import type { HttpResult } from "../api/httpClient";

function makeItem(overrides: Partial<CloudInventoryItem> = {}): CloudInventoryItem {
  return {
    itemId: 1,
    stackLotId: null,
    name: "Ivory Buckler",
    category: "Armor",
    quantity: 1,
    value: 100,
    burden: 20,
    isReserved: false,
    version: 1,
    permittedActions: { canWithdraw: true, canList: true, canTransfer: true, canShare: true },
    iconCacheKeyHex: null,
    ...overrides,
  };
}

type FakeWithdrawalApiOverrides = Partial<{ [K in keyof WithdrawalApi]: (...args: unknown[]) => Promise<HttpResult<unknown>> }>;

function fakeWithdrawalApi(overrides: FakeWithdrawalApiOverrides = {}): WithdrawalApi {
  return {
    openReservation: vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret-token", version: 1, expiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString() },
        }) as HttpResult<unknown>,
    ),
    cancelReservation: vi.fn(async () => ({ ok: true, status: 200, data: { cancelled: true } }) as HttpResult<unknown>),
    ...overrides,
  } as unknown as WithdrawalApi;
}

describe("WithdrawalDialog", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("lists every selected item before a token is created", () => {
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem({ name: "Ivory Buckler" }), quantity: 1 }];
    render(
      <WithdrawalDialog open onClose={() => {}} selection={selection} withdrawalApi={fakeWithdrawalApi()} onSettled={() => {}} />,
    );

    expect(screen.getByText("Ivory Buckler")).toBeInTheDocument();
  });

  it("requests a whole-item target for a non-stack item", async () => {
    const openReservation = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: new Date(Date.now() + 900_000).toISOString() },
        }) as HttpResult<unknown>,
    );
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem({ itemId: 777 }), quantity: 1 }];
    render(
      <WithdrawalDialog
        open
        onClose={() => {}}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ openReservation })}
        onSettled={() => {}}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    expect(openReservation).toHaveBeenCalledWith([{ kind: "Item", itemId: 777 }]);
  });

  it("requests a full-quantity StackLot target when the selected quantity equals the lot's current quantity (INV-002)", async () => {
    const openReservation = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: new Date(Date.now() + 900_000).toISOString() },
        }) as HttpResult<unknown>,
    );
    const lotId = "11111111-1111-1111-1111-111111111111";
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem({ stackLotId: lotId, quantity: 10, version: 3 }), quantity: 10 }];
    render(
      <WithdrawalDialog
        open
        onClose={() => {}}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ openReservation })}
        onSettled={() => {}}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    expect(openReservation).toHaveBeenCalledWith([{ kind: "StackLot", stackLotId: lotId }]);
  });

  it("requests a partial StackLot target with quantity/expectedVersion for a partial withdrawal (INV-002)", async () => {
    const openReservation = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: new Date(Date.now() + 900_000).toISOString() },
        }) as HttpResult<unknown>,
    );
    const lotId = "11111111-1111-1111-1111-111111111111";
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem({ stackLotId: lotId, quantity: 10, version: 3 }), quantity: 4 }];
    render(
      <WithdrawalDialog
        open
        onClose={() => {}}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ openReservation })}
        onSettled={() => {}}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    expect(openReservation).toHaveBeenCalledWith([{ kind: "StackLot", stackLotId: lotId, quantity: 4, expectedVersion: 3 }]);
  });

  it("reveals the token secret exactly once after a successful open", async () => {
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem(), quantity: 1 }];
    render(
      <WithdrawalDialog open onClose={() => {}} selection={selection} withdrawalApi={fakeWithdrawalApi()} onSettled={() => {}} />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    expect(screen.getByTestId("withdrawal-token-secret")).toHaveTextContent("secret-token");
  });

  it("shows a service-unavailable message when ACE is down (WDR-008)", async () => {
    const openReservation = vi.fn(async () => ({ ok: false, status: 503, error: {} }) as HttpResult<unknown>);
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem(), quantity: 1 }];
    render(
      <WithdrawalDialog
        open
        onClose={() => {}}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ openReservation })}
        onSettled={() => {}}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    expect(screen.getByRole("alert")).toHaveTextContent(/ACE is currently offline/i);
    expect(screen.queryByTestId("withdrawal-token-secret")).not.toBeInTheDocument();
  });

  it("counts down toward expiry and marks the token expired at zero", async () => {
    const openReservation = vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          data: { reservationId: "r1", tokenSecret: "secret", version: 1, expiresAtUtc: new Date(Date.now() + 2000).toISOString() },
        }) as HttpResult<unknown>,
    );
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem(), quantity: 1 }];
    render(
      <WithdrawalDialog
        open
        onClose={() => {}}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ openReservation })}
        onSettled={() => {}}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });

    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(screen.getByRole("alert")).toHaveTextContent(/expired/i);
    expect(screen.getByRole("button", { name: /cancel withdrawal token/i })).toBeDisabled();
  });

  it("cancels the reservation and calls onSettled", async () => {
    const cancelReservation = vi.fn(async () => ({ ok: true, status: 200, data: { cancelled: true } }) as HttpResult<unknown>);
    const onSettled = vi.fn();
    const onClose = vi.fn();
    const selection: WithdrawalSelectionEntry[] = [{ item: makeItem(), quantity: 1 }];
    render(
      <WithdrawalDialog
        open
        onClose={onClose}
        selection={selection}
        withdrawalApi={fakeWithdrawalApi({ cancelReservation })}
        onSettled={onSettled}
      />,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /create withdrawal token/i }));
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /cancel withdrawal token/i }));
    });

    expect(cancelReservation).toHaveBeenCalledWith("r1", 1);
    expect(onSettled).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });
});
