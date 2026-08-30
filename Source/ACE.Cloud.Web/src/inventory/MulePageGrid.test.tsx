import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { MulePageGrid } from "./MulePageGrid";
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

describe("MulePageGrid", () => {
  it("renders one option per item with an accessible name", () => {
    const items = [makeItem({ itemId: 1, name: "Ivory Buckler" }), makeItem({ itemId: 2, name: "Steel Sword" })];
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={items}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={() => {}}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(screen.getByRole("listbox", { name: "[Armor] Mule 1" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Ivory Buckler" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Steel Sword" })).toBeInTheDocument();
  });

  it("shows a quantity badge for stacks and a reserved badge for reserved items", () => {
    const items = [makeItem({ itemId: 1, quantity: 5 }), makeItem({ itemId: 2, isReserved: true })];
    render(
      <MulePageGrid
        pageName="[Currency] Mule 1"
        items={items}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={() => {}}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("Reserved")).toBeInTheDocument();
  });

  it("clicking an item activates it non-additively", async () => {
    const onActivate = vi.fn();
    const user = userEvent.setup();
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={[makeItem()]}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={onActivate}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    await user.click(screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ itemId: 1 }), false);
  });

  it("ctrl+click activates additively (multi-select without opening appraisal)", () => {
    const onActivate = vi.fn();
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={[makeItem()]}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={onActivate}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    fireEvent.click(screen.getByRole("option", { name: "Ivory Buckler" }), { ctrlKey: true });

    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ itemId: 1 }), true);
  });

  it("right-clicking (context menu) activates the item non-additively instead of opening the browser menu", () => {
    const onActivate = vi.fn();
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={[makeItem()]}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={onActivate}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    const option = screen.getByRole("option", { name: "Ivory Buckler" });
    const event = new MouseEvent("contextmenu", { bubbles: true, cancelable: true });
    const preventDefaultSpy = vi.spyOn(event, "preventDefault");
    option.dispatchEvent(event);

    expect(preventDefaultSpy).toHaveBeenCalled();
    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ itemId: 1 }), false);
  });

  it("marks a selected item with aria-selected", () => {
    const items = [makeItem({ itemId: 1 })];
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={items}
        columns={6}
        selectedKeys={new Set(["1"])}
        activeKey={null}
        onActivate={() => {}}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(screen.getByRole("option", { name: "Ivory Buckler" })).toHaveAttribute("aria-selected", "true");
  });

  it("Enter opens/activates the focused item", async () => {
    const onActivate = vi.fn();
    const user = userEvent.setup();
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={[makeItem()]}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={onActivate}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    screen.getByRole("option", { name: "Ivory Buckler" }).focus();
    await user.keyboard("{Enter}");

    expect(onActivate).toHaveBeenCalledWith(expect.objectContaining({ itemId: 1 }), false);
  });

  it("ArrowRight moves roving focus to the next item in the same row", async () => {
    const onFocusItem = vi.fn();
    const user = userEvent.setup();
    const items = [makeItem({ itemId: 1, name: "First" }), makeItem({ itemId: 2, name: "Second" })];
    render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={items}
        columns={6}
        selectedKeys={new Set()}
        activeKey="1"
        onActivate={() => {}}
        onFocusItem={onFocusItem}
        buildIconUrl={buildIconUrl}
      />,
    );

    screen.getByRole("option", { name: "First" }).focus();
    await user.keyboard("{ArrowRight}");

    expect(onFocusItem).toHaveBeenCalledWith(expect.objectContaining({ itemId: 2 }));
    expect(screen.getByRole("option", { name: "Second" })).toHaveFocus();
  });

  it("has no detectable accessibility violations", async () => {
    const items = [makeItem({ itemId: 1 }), makeItem({ itemId: 2, name: "Steel Sword" })];
    const { container } = render(
      <MulePageGrid
        pageName="[Armor] Mule 1"
        items={items}
        columns={6}
        selectedKeys={new Set()}
        activeKey={null}
        onActivate={() => {}}
        onFocusItem={() => {}}
        buildIconUrl={buildIconUrl}
      />,
    );

    expect(await axe(container)).toHaveNoViolations();
  });
});
