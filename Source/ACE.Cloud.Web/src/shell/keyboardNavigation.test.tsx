import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { AppShell } from "./AppShell";

const navItems = [
  { to: "/", label: "Marketplace" },
  { to: "/dashboard", label: "Dashboard" },
];

describe("AppShell keyboard-only navigation", () => {
  it("reaches the skip link first, then every nav link, then the main landmark in tab order", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>
          <button>page action</button>
        </AppShell>
      </MemoryRouter>,
    );

    await user.tab();
    expect(screen.getByText(/skip to main content/i)).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("link", { name: "Marketplace" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("link", { name: "Dashboard" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "page action" })).toHaveFocus();
  });

  it("moves focus to the main landmark when the skip link is activated", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AppShell navItems={navItems}>
          <button>page action</button>
        </AppShell>
      </MemoryRouter>,
    );

    await user.tab();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("main")).toHaveFocus();
  });
});
