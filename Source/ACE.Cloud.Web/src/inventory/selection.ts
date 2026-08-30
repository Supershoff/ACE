import type { CloudInventoryItem } from "../api/types";

/** Stable identity for one Mule Page row (UI-003: "stable item identity as the final tie-break"). */
export function inventoryItemKey(item: CloudInventoryItem): string {
  return item.stackLotId ? `${item.itemId}:${item.stackLotId}` : String(item.itemId);
}
