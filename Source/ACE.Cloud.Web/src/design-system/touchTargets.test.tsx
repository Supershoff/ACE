import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { AppShell } from "../shell/AppShell";
import { Menu } from "./primitives/Menu";
import { touchTargetTokens } from "./tokens";

const touchTargetVar = "var(--touch-target-min-size)";

describe("interactive controls meet the shared minimum touch-target size (UI-008)", () => {
  it("every AppShell nav link reserves the minimum touch-target size", () => {
    render(
      <MemoryRouter>
        <AppShell navItems={[{ to: "/", label: "Marketplace" }]}>content</AppShell>
      </MemoryRouter>,
    );

    const link = screen.getByRole("link", { name: "Marketplace" });
    expect(link.style.minHeight).toBe(touchTargetVar);
  });

  it("the Menu trigger button reserves the minimum touch-target size", () => {
    render(<Menu label="Item actions" items={[{ id: "a", label: "Appraise" }]} onSelect={() => {}} />);

    expect(screen.getByRole("button", { name: "Item actions" }).style.minHeight).toBe(touchTargetVar);
  });

  it("each Menu item reserves the minimum touch-target size", async () => {
    const user = userEvent.setup();
    render(<Menu label="Item actions" items={[{ id: "a", label: "Appraise" }]} onSelect={() => {}} />);
    await user.click(screen.getByRole("button", { name: "Item actions" }));

    expect(screen.getByRole("menuitem", { name: "Appraise" }).style.minHeight).toBe(touchTargetVar);
  });

  it("the token itself meets the WCAG 2.2 minimum of 44 CSS pixels", () => {
    expect(parseInt(touchTargetTokens.minSize, 10)).toBeGreaterThanOrEqual(44);
  });
});
