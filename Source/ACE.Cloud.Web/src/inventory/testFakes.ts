import { vi } from "vitest";
import type { InventoryApi } from "../api/inventoryApi";
import type { CloudAppraisalPanel, CloudInventoryItem, CloudInventoryQueryResponse } from "../api/types";
import type { CloudTransferOfferSummary, TransferOfferApi } from "../api/transferOfferApi";
import type { WithdrawalApi } from "../api/withdrawalApi";

export function makeInventoryItem(overrides: Partial<CloudInventoryItem> = {}): CloudInventoryItem {
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

export function makeQueryResponse(items: readonly CloudInventoryItem[], overrides: Partial<CloudInventoryQueryResponse["page"]> = {}): CloudInventoryQueryResponse {
  return {
    page: {
      category: "Armor",
      pageName: "[Armor] Mule 1",
      pageNumber: 1,
      pageExists: true,
      totalItemsInScope: items.length,
      totalPages: 1,
      items,
      ...overrides,
    },
    asOfCustodyOutboxSequenceNumber: 0,
  };
}

export const samplePanel: CloudAppraisalPanel = {
  contractVersion: 1,
  itemName: "Ivory Buckler",
  sections: [
    { kind: "Header", lines: [{ text: "Ivory Buckler", style: "Title" }] },
    { kind: "ValueAndBurden", lines: [{ text: "Value: 100", style: "Body" }] },
  ],
};

export function fakeInventoryApi(overrides: Partial<InventoryApi> = {}): InventoryApi {
  return {
    queryPages: vi.fn(async () => ({ ok: true, status: 200, data: makeQueryResponse([makeInventoryItem()]) })),
    fetchAppraisal: vi.fn(async () => ({ ok: true, status: 200, data: samplePanel })),
    buildIconUrl: (hex: string) => `/inventory/icons/${hex}`,
    ...overrides,
  };
}

const sampleOffer: CloudTransferOfferSummary = {
  id: "offer-1",
  senderOwnerId: "owner-1",
  recipientOwnerId: "owner-2",
  status: "Pending",
  version: 1,
  createdAtUtc: "2026-01-01T00:00:00Z",
  expiresAtUtc: "2026-01-08T00:00:00Z",
  targets: [],
};

export function fakeTransferOfferApi(overrides: Partial<TransferOfferApi> = {}): TransferOfferApi {
  return {
    list: vi.fn(async () => ({ ok: true, status: 200, data: { sent: [], received: [] } })),
    create: vi.fn(async () => ({ ok: true, status: 200, data: sampleOffer })),
    accept: vi.fn(async () => ({ ok: true, status: 200, data: sampleOffer })),
    decline: vi.fn(async () => ({ ok: true, status: 200, data: sampleOffer })),
    cancel: vi.fn(async () => ({ ok: true, status: 200, data: sampleOffer })),
    ...overrides,
  };
}

export function fakeWithdrawalApi(overrides: Partial<WithdrawalApi> = {}): WithdrawalApi {
  return {
    fetchLocations: vi.fn(async () => ({ ok: true, status: 200, data: { withdrawAnywhereEnabled: false, namedLandblocks: [] } })),
    fetchCurrent: vi.fn(async () => ({ ok: true, status: 200, data: { active: false as const } })),
    create: vi.fn(async () => ({
      ok: true,
      status: 200,
      data: { secret: "SECRET-TOKEN", reservationId: "res-1", version: 1, expiresAtUtc: "2026-01-01T00:15:00Z" },
    })),
    cancel: vi.fn(async () => ({ ok: true, status: 200, data: { reservationId: "res-1", version: 2, status: "Released" as const } })),
    splitStackLot: vi.fn(async () => ({
      ok: true,
      status: 200,
      data: {
        remainingLot: { id: "lot-remaining", quantity: 1, version: 2 },
        newLot: { id: "lot-new", quantity: 1, version: 1 },
      },
    })),
    ...overrides,
  };
}
