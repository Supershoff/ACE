import { vi } from "vitest";
import type { InventoryApi } from "../api/inventoryApi";
import type { CloudAppraisalPanel, CloudInventoryItem, CloudInventoryQueryResponse } from "../api/types";

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
