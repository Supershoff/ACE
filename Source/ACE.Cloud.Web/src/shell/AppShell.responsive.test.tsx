import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "./AppShell";
import { mockMatchMedia } from "../test/mockMatchMedia";

const navItems = [{ to: "/", label: "Marketplace" }];

describe("AppShell responsive navigation", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows every nav link directly and no menu toggle at desktop widths", () => {
    mockMatchMedia([]);
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>content</AppShell>
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "Marketplace" })).toBeVisible();
    expect(screen.queryByRole("button", { name: /menu/i })).not.toBeInTheDocument();
  });

  it("collapses navigation behind an accessible toggle button on narrow viewports", () => {
    mockMatchMedia(["max-width"]);
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>content</AppShell>
      </MemoryRouter>,
    );

    const toggle = screen.getByRole("button", { name: /menu/i });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("expands the mobile nav panel when the toggle is activated", async () => {
    mockMatchMedia(["max-width"]);
    const { default: userEvent } = await import("@testing-library/user-event");
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>content</AppShell>
      </MemoryRouter>,
    );

    const toggle = screen.getByRole("button", { name: /menu/i });
    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "Marketplace" })).toBeVisible();
  });

  it("preserves every operation: the same nav links exist in mobile and desktop layouts", () => {
    mockMatchMedia(["max-width"]);
    const { unmount } = render(
      <MemoryRouter>
        <AppShell navItems={navItems}>content</AppShell>
      </MemoryRouter>,
    );
    const mobileLinkCount = screen.getAllByRole("link", { name: "Marketplace" }).length;
    unmount();

    mockMatchMedia([]);
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>content</AppShell>
      </MemoryRouter>,
    );
    const desktopLinkCount = screen.getAllByRole("link", { name: "Marketplace" }).length;

    expect(desktopLinkCount).toBe(mobileLinkCount);
  });
});
