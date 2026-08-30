import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { WithdrawalTokenPanel } from "./WithdrawalTokenPanel";
import type { HttpResult } from "../api/httpClient";
import type { InventoryApi } from "../api/inventoryApi";
import type { CloudInventoryItem, CloudInventoryQueryResponse } from "../api/types";
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

const sampleItem: CloudInventoryItem = {
  itemId: 777,
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
};

function fakeInventoryApi(items: readonly CloudInventoryItem[] = [sampleItem]): InventoryApi {
  const response: CloudInventoryQueryResponse = {
    page: { category: "Armor", pageName: "Armor Mule 1", pageNumber: 1, pageExists: true, totalItemsInScope: items.length, totalPages: 1, items },
    asOfCustodyOutboxSequenceNumber: 1,
  };
  return {
    queryPages: vi.fn(async () => ({ ok: true, status: 200, data: response }) as HttpResult<CloudInventoryQueryResponse>),
    fetchAppraisal: vi.fn(),
    buildIconUrl: (hex) => `/inventory/icons/${hex}`,
  };
}

function fakeWithdrawalApi(overrides: Partial<WithdrawalApi> = {}): WithdrawalApi {
  return {
    fetchLocations: vi.fn(
      async () => ({ ok: true, status: 200, data: { withdrawAnywhereEnabled: false, namedLandblocks: [] } }) as never,
    ),
    fetchCurrent: vi.fn(async () => ({ ok: true, status: 200, data: { active: false } }) as never),
    create: vi.fn(
      async () =>
        ({ ok: true, status: 200, data: { secret: "top-secret-token", reservationId: "r1", version: 1, expiresAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString() } }) as never,
    ),
    cancel: vi.fn(async () => ({ ok: true, status: 200, data: { reservationId: "r1", version: 2, status: "Released" } }) as never),
    splitStackLot: vi.fn(),
    ...overrides,
  };
}

function renderPanel(withdrawalApi: WithdrawalApi, inventoryApi: InventoryApi = fakeInventoryApi()) {
  return render(
    <SessionContext.Provider value={baseSession()}>
      <WithdrawalTokenPanel withdrawalApi={withdrawalApi} inventoryApi={inventoryApi} />
    </SessionContext.Provider>,
  );
}

describe("WithdrawalTokenPanel", () => {
  it("shows the minted secret exactly once after creation, with a copy affordance", async () => {
    const user = userEvent.setup();
    const withdrawalApi = fakeWithdrawalApi({
      // The initial mount reads no active reservation; after creation, the server's own status
      // view immediately reflects the reservation this test's `create` mock just opened.
      fetchCurrent: vi
        .fn()
        .mockResolvedValueOnce({ ok: true, status: 200, data: { active: false } })
        .mockResolvedValue({
          ok: true,
          status: 200,
          data: { active: true, reservationId: "r1", version: 1, expiresAtUtc: new Date(Date.now() + 60000).toISOString(), targets: [{ kind: "Item", itemBiotaId: 777, stackLotId: null, quantity: null }] },
        }),
    });
    renderPanel(withdrawalApi);

    await waitFor(() => expect(screen.getByText("Ivory Buckler")).toBeInTheDocument());
    await user.click(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("button", { name: /create withdrawal token/i }));

    await waitFor(() => expect(screen.getByText("top-secret-token")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /copy token/i })).toBeInTheDocument();
    expect(withdrawalApi.create).toHaveBeenCalledWith([{ kind: "Item", itemBiotaId: 777 }]);
  });

  it("never re-shows the secret after reconciling from the server (a reload/second tab)", async () => {
    const withdrawalApi = fakeWithdrawalApi({
      fetchCurrent: vi.fn(
        async () =>
          ({
            ok: true,
            status: 200,
            data: { active: true, reservationId: "r1", version: 1, expiresAtUtc: new Date(Date.now() + 60000).toISOString(), targets: [{ kind: "Item", itemBiotaId: 777, stackLotId: null, quantity: null }] },
          }) as never,
      ),
    });
    renderPanel(withdrawalApi);

    await waitFor(() => expect(screen.getByLabelText(/active withdrawal token/i)).toBeInTheDocument());
    expect(screen.queryByText("top-secret-token")).not.toBeInTheDocument();
    expect(screen.getByText(/no longer shown here/i)).toBeInTheDocument();
  });

  it("cancels the active reservation and returns to the creation form", async () => {
    const withdrawalApi = fakeWithdrawalApi({
      fetchCurrent: vi
        .fn()
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          data: { active: true, reservationId: "r1", version: 1, expiresAtUtc: new Date(Date.now() + 60000).toISOString(), targets: [] },
        })
        .mockResolvedValue({ ok: true, status: 200, data: { active: false } }),
    });
    renderPanel(withdrawalApi);

    await waitFor(() => expect(screen.getByRole("button", { name: /cancel withdrawal token/i })).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: /cancel withdrawal token/i }));

    await waitFor(() => expect(withdrawalApi.cancel).toHaveBeenCalledWith("r1", 1));
    await waitFor(() => expect(screen.getByRole("button", { name: /^create withdrawal token$/i })).toBeInTheDocument());
  });

  it("shows an explicit message and blocks creation when the ACE world process is unavailable", async () => {
    const user = userEvent.setup();
    const withdrawalApi = fakeWithdrawalApi({
      create: vi.fn(async () => ({ ok: false, status: 503, error: { error: "world_boundary_unavailable" } }) as never),
    });
    renderPanel(withdrawalApi);

    await waitFor(() => expect(screen.getByText("Ivory Buckler")).toBeInTheDocument());
    await user.click(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("button", { name: /create withdrawal token/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/ace is currently offline/i));
  });

  it("splits a partial stack-lot selection into a new lot before reserving it", async () => {
    const user = userEvent.setup();
    const stackItem: CloudInventoryItem = {
      ...sampleItem,
      itemId: 0,
      stackLotId: "lot-1",
      name: "Peas",
      quantity: 10,
      version: 1,
    };
    const withdrawalApi = fakeWithdrawalApi({
      splitStackLot: vi.fn(async () => ({ ok: true, status: 200, data: { remainingLot: { id: "lot-1", quantity: 5, version: 2 }, newLot: { id: "lot-2", quantity: 5, version: 1 } } }) as never),
    });
    renderPanel(withdrawalApi, fakeInventoryApi([stackItem]));

    await waitFor(() => expect(screen.getByText(/Peas/)).toBeInTheDocument());
    await user.click(screen.getByRole("checkbox"));
    fireEvent.change(screen.getByLabelText(/quantity of peas to withdraw/i), { target: { value: "5" } });

    await user.click(screen.getByRole("button", { name: /create withdrawal token/i }));

    await waitFor(() => expect(withdrawalApi.splitStackLot).toHaveBeenCalledWith("lot-1", 1, 5));
    expect(withdrawalApi.create).toHaveBeenCalledWith([{ kind: "StackLot", stackLotId: "lot-2" }]);
  });
});
