import type { CloudInventoryItem, CloudInventorySortDirection, CloudInventorySortKey } from "../api/types";
import { VisuallyHidden } from "../design-system/primitives/VisuallyHidden";
import { InventoryIcon } from "./InventoryIcon";
import { inventoryItemKey } from "./selection";

export interface InventorySpreadsheetProps {
  readonly items: readonly CloudInventoryItem[];
  readonly sortKey: CloudInventorySortKey;
  readonly sortDirection: CloudInventorySortDirection;
  readonly onSortChange: (sortKey: CloudInventorySortKey, sortDirection: CloudInventorySortDirection) => void;
  readonly selectedKeys: ReadonlySet<string>;
  readonly onActivate: (item: CloudInventoryItem, additive: boolean) => void;
  readonly buildIconUrl: (iconCacheKeyHex: string) => string;
}

const SORTABLE_COLUMNS: ReadonlyArray<{ key: CloudInventorySortKey; label: string }> = [
  { key: "Name", label: "Name" },
  { key: "Value", label: "Value" },
  { key: "Burden", label: "Burden" },
];

/**
 * The spreadsheet view UI-003 requires to "share filters and deterministic sorting" with
 * `MulePageGrid` over the same query contract -- same items, same authorization, same stable
 * identity tie-break, just row-per-item instead of icon-per-cell.
 */
export function InventorySpreadsheet({
  items,
  sortKey,
  sortDirection,
  onSortChange,
  selectedKeys,
  onActivate,
  buildIconUrl,
}: InventorySpreadsheetProps) {
  function handleSortClick(column: CloudInventorySortKey) {
    if (column !== sortKey) {
      onSortChange(column, "Ascending");
      return;
    }
    onSortChange(column, sortDirection === "Ascending" ? "Descending" : "Ascending");
  }

  function ariaSortFor(column: CloudInventorySortKey): "ascending" | "descending" | "none" {
    if (column !== sortKey) {
      return "none";
    }
    return sortDirection === "Ascending" ? "ascending" : "descending";
  }

  return (
    <table className="inventory-spreadsheet">
      <caption>
        <VisuallyHidden>Cloud Inventory spreadsheet</VisuallyHidden>
      </caption>
      <thead>
        <tr>
          <th scope="col">Icon</th>
          {SORTABLE_COLUMNS.map((column) => (
            <th key={column.key} scope="col" aria-sort={ariaSortFor(column.key)}>
              <button type="button" onClick={() => handleSortClick(column.key)}>
                {column.label}
              </button>
            </th>
          ))}
          <th scope="col">Category</th>
          <th scope="col">Quantity</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {items.map((item) => {
          const key = inventoryItemKey(item);
          const isSelected = selectedKeys.has(key);
          return (
            <tr
              key={key}
              aria-selected={isSelected}
              tabIndex={0}
              onClick={(event) => onActivate(item, event.ctrlKey || event.metaKey)}
              onContextMenu={(event) => {
                event.preventDefault();
                onActivate(item, false);
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  onActivate(item, event.ctrlKey || event.metaKey);
                }
              }}
            >
              <td>
                <InventoryIcon name={item.name} iconCacheKeyHex={item.iconCacheKeyHex} buildIconUrl={buildIconUrl} />
              </td>
              <td>{item.name}</td>
              <td>{item.value ?? "—"}</td>
              <td>{item.burden ?? "—"}</td>
              <td>{item.category}</td>
              <td>{item.quantity}</td>
              <td>{item.isReserved ? "Reserved" : ""}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
