import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { InventorySpreadsheet } from "./InventorySpreadsheet";
import type { CloudInventoryItem } from "../api/types";

expect.extend(toHaveNoViolations);

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

const buildIconUrl = (hex: string) => `/inventory/icons/${hex}`;

describe("InventorySpreadsheet", () => {
  it("renders one row per item with its category, quantity, value, and burden", () => {
    render(
      <InventorySpreadsheet
        items={[makeItem()]}
        sortKey="Name"
        sortDirection="Ascending"
        onSortChange={() => {}}
        selectedKeys={new Set()}
        onActivate={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    const row = screen.getByText("Ivory Buckler").closest("tr")!;
    expect(row).toHaveTextContent("Armor");
    expect(row).toHaveTextContent("100");
    expect(row).toHaveTextContent("20");
  });

  it("clicking a sortable column header toggling ascending/descending for the same key", async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();
    render(
      <InventorySpreadsheet
        items={[makeItem()]}
        sortKey="Value"
        sortDirection="Ascending"
        onSortChange={onSortChange}
        selectedKeys={new Set()}
        onActivate={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Value" }));

    expect(onSortChange).toHaveBeenCalledWith("Value", "Descending");
  });

  it("clicking a different column header sorts ascending by that column", async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();
    render(
      <InventorySpreadsheet
        items={[makeItem()]}
        sortKey="Name"
        sortDirection="Ascending"
        onSortChange={onSortChange}
        selectedKeys={new Set()}
        onActivate={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Burden" }));

    expect(onSortChange).toHaveBeenCalledWith("Burden", "Ascending");
  });

  it("clicking a row activates it non-additively", async () => {
    const onActivate = vi.fn();
    const user = userEvent.setup();
    render(
      <InventorySpreadsheet
        items={[makeItem()]}
        sortKey="Name"
        sortDirection="Ascending"
        onSortChange={() => {}}
        selectedKeys={new Set()}
        onActivate={onActivate}
        buildIconUrl={buildIconUrl}
      />,
    );

    await user.click(screen.getByText("Ivory Buckler"));

    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ itemId: 1 }), false);
  });

  it("marks the current sort column with aria-sort", () => {
    render(
      <InventorySpreadsheet
        items={[makeItem()]}
        sortKey="Value"
        sortDirection="Descending"
        onSortChange={() => {}}
        selectedKeys={new Set()}
        onActivate={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(screen.getByRole("columnheader", { name: "Value" })).toHaveAttribute("aria-sort", "descending");
    expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute("aria-sort", "none");
  });

  it("has no detectable accessibility violations", async () => {
    const { container } = render(
      <InventorySpreadsheet
        items={[makeItem(), makeItem({ itemId: 2, name: "Steel Sword" })]}
        sortKey="Name"
        sortDirection="Ascending"
        onSortChange={() => {}}
        selectedKeys={new Set()}
        onActivate={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(await axe(container)).toHaveNoViolations();
  });
});
