import { useRef, type KeyboardEvent, type MouseEvent } from "react";
import { iconGridTokens } from "../design-system/inventoryFidelityTokens";
import { touchTargetStyle } from "../design-system/touchTarget";
import type { CloudInventoryItem } from "../api/types";
import { InventoryIcon } from "./InventoryIcon";
import { inventoryItemKey } from "./selection";

export interface MulePageGridProps {
  readonly pageName: string;
  readonly items: readonly CloudInventoryItem[];
  readonly columns: number;
  readonly selectedKeys: ReadonlySet<string>;
  readonly activeKey: string | null;
  /** `additive` is true for a ctrl/cmd-click or ctrl/cmd+Enter: toggle selection without opening the appraisal. */
  readonly onActivate: (item: CloudInventoryItem, additive: boolean) => void;
  readonly onFocusItem: (item: CloudInventoryItem) => void;
  readonly buildIconUrl: (iconCacheKeyHex: string) => string;
}

const gridStyle = (columns: number) =>
  ({
    display: "grid",
    gridTemplateColumns: `repeat(${columns}, ${iconGridTokens.cellSize})`,
    gap: iconGridTokens.cellGap,
  }) as const;

function cellStyle() {
  return {
    width: iconGridTokens.cellSize,
    height: iconGridTokens.cellSize,
    backgroundColor: iconGridTokens.cellBackground,
    borderColor: iconGridTokens.cellBorderLight,
    position: "relative",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  } as const;
}

/**
 * The classic AC bright-green selection border, drawn as its own absolutely positioned overlay
 * (UI-006: "Stack quantity, selection, reservation, and web badges are separate UI layers") --
 * never a style applied to the cell or to `InventoryIcon` itself, so selecting an item can never
 * alter the composed source icon underneath it.
 */
const selectionOutlineStyle = {
  position: "absolute",
  inset: 0,
  borderColor: iconGridTokens.selectionOutlineColor,
  borderWidth: iconGridTokens.selectionOutlineWidth,
  borderStyle: "solid",
  pointerEvents: "none",
} as const;

const quantityBadgeStyle = {
  position: "absolute",
  backgroundColor: iconGridTokens.quantityBadgeBackground,
  color: iconGridTokens.quantityBadgeText,
} as const;

const reservedBadgeStyle = {
  position: "absolute",
  backgroundColor: iconGridTokens.reservedBadgeBackground,
  color: iconGridTokens.reservedBadgeText,
} as const;

function accessibleLabel(item: CloudInventoryItem): string {
  const parts = [item.name];
  if (item.quantity > 1) {
    parts.push(`quantity ${item.quantity}`);
  }
  if (item.isReserved) {
    parts.push("reserved");
  }
  return parts.join(", ");
}

/**
 * The AC-style 6×17 desktop Mule Page grid (UI-002/UI-003): a virtual, automatically sorted page over
 * the shared inventory query contract, reflowing at `columns` without ever changing which items
 * belong to this page (`columns` is a caller-supplied layout concern only, never re-queried). Uses
 * the ARIA listbox pattern (`role="listbox"`/`role="option"`) with roving tabindex arrow-key
 * navigation across `columns`-wide rows, so the same operations (open appraisal, multi-select) work
 * identically by mouse, keyboard, and touch (UI-008).
 */
export function MulePageGrid({
  pageName,
  items,
  columns,
  selectedKeys,
  activeKey,
  onActivate,
  onFocusItem,
  buildIconUrl,
}: MulePageGridProps) {
  const cellRefs = useRef<Map<string, HTMLDivElement>>(new Map());

  const effectiveActiveKey = activeKey ?? (items.length > 0 ? inventoryItemKey(items[0]!) : null);

  function focusIndex(index: number) {
    const clamped = Math.min(Math.max(index, 0), items.length - 1);
    const item = items[clamped];
    if (!item) {
      return;
    }
    onFocusItem(item);
    cellRefs.current.get(inventoryItemKey(item))?.focus();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>, index: number, item: CloudInventoryItem) {
    switch (event.key) {
      case "ArrowRight":
        event.preventDefault();
        focusIndex(index + 1);
        break;
      case "ArrowLeft":
        event.preventDefault();
        focusIndex(index - 1);
        break;
      case "ArrowDown":
        event.preventDefault();
        focusIndex(index + columns);
        break;
      case "ArrowUp":
        event.preventDefault();
        focusIndex(index - columns);
        break;
      case "Home":
        event.preventDefault();
        focusIndex(0);
        break;
      case "End":
        event.preventDefault();
        focusIndex(items.length - 1);
        break;
      case "Enter":
      case " ":
        event.preventDefault();
        onActivate(item, event.ctrlKey || event.metaKey);
        break;
      default:
        break;
    }
  }

  function handleContextMenu(event: MouseEvent<HTMLDivElement>, item: CloudInventoryItem) {
    event.preventDefault();
    onActivate(item, false);
  }

  return (
    <div role="listbox" aria-label={pageName} aria-multiselectable="true" style={gridStyle(columns)}>
      {items.map((item, index) => {
        const key = inventoryItemKey(item);
        const isSelected = selectedKeys.has(key);
        return (
          <div
            key={key}
            role="option"
            aria-selected={isSelected}
            aria-label={accessibleLabel(item)}
            tabIndex={key === effectiveActiveKey ? 0 : -1}
            className="mule-page-grid__cell"
            style={{ ...cellStyle(), ...touchTargetStyle }}
            ref={(element) => {
              if (element) {
                cellRefs.current.set(key, element);
              } else {
                cellRefs.current.delete(key);
              }
            }}
            onClick={(event) => onActivate(item, event.ctrlKey || event.metaKey)}
            onContextMenu={(event) => handleContextMenu(event, item)}
            onKeyDown={(event) => handleKeyDown(event, index, item)}
            onFocus={() => onFocusItem(item)}
          >
            <InventoryIcon name={item.name} iconCacheKeyHex={item.iconCacheKeyHex} buildIconUrl={buildIconUrl} />
            {isSelected ? (
              <span className="mule-page-grid__selection-outline" style={selectionOutlineStyle} aria-hidden="true" />
            ) : null}
            {item.quantity > 1 ? (
              <span className="mule-page-grid__quantity-badge" style={quantityBadgeStyle}>
                {item.quantity}
              </span>
            ) : null}
            {item.isReserved ? (
              <span className="mule-page-grid__reserved-badge" style={reservedBadgeStyle}>
                Reserved
              </span>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
