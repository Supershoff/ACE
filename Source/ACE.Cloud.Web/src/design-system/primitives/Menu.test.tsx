import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { Menu } from "./Menu";

function renderMenu(onSelect: (id: string) => void = vi.fn()) {
  return render(
    <Menu
      label="Item actions"
      items={[
        { id: "appraise", label: "Appraise" },
        { id: "list", label: "List for sale" },
        { id: "withdraw", label: "Withdraw" },
      ]}
      onSelect={onSelect}
    />,
  );
}

describe("Menu", () => {
  it("has no detectable axe violations when closed or open", async () => {
    const { container } = renderMenu();
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.setup().click(screen.getByRole("button", { name: "Item actions" }));
    expect(await axe(container)).toHaveNoViolations();
  });

  it("is closed by default with aria-expanded false", () => {
    renderMenu();
    expect(screen.getByRole("button", { name: "Item actions" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("opens on click and moves focus to the first menu item", async () => {
    const user = userEvent.setup();
    renderMenu();

    await user.click(screen.getByRole("button", { name: "Item actions" }));

    expect(screen.getByRole("button", { name: "Item actions" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("menuitem", { name: "Appraise" })).toHaveFocus();
  });

  it("moves focus down and wraps with ArrowDown", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Item actions" }));

    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("menuitem", { name: "List for sale" })).toHaveFocus();

    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("menuitem", { name: "Withdraw" })).toHaveFocus();

    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("menuitem", { name: "Appraise" })).toHaveFocus();
  });

  it("moves focus up and wraps with ArrowUp", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Item actions" }));

    await user.keyboard("{ArrowUp}");
    expect(screen.getByRole("menuitem", { name: "Withdraw" })).toHaveFocus();
  });

  it("selects the focused item on Enter, closes the menu, and returns focus to the trigger", async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    renderMenu(onSelect);
    await user.click(screen.getByRole("button", { name: "Item actions" }));

    await user.keyboard("{ArrowDown}{Enter}");

    expect(onSelect).toHaveBeenCalledWith("list");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Item actions" })).toHaveFocus();
  });

  it("closes on Escape and returns focus to the trigger", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Item actions" }));

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Item actions" })).toHaveFocus();
  });
});
