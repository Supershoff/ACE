import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import { AppShell } from "./AppShell";

const navItems = [
  { to: "/", label: "Marketplace" },
  { to: "/dashboard", label: "Dashboard" },
];

function renderShell(children: React.ReactNode = <p>Page content</p>) {
  return render(
    <MemoryRouter>
      <AppShell navItems={navItems}>{children}</AppShell>
    </MemoryRouter>,
  );
}

describe("AppShell landmarks and accessible names", () => {
  it("has no detectable axe violations", async () => {
    const { container } = renderShell();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("exposes exactly one banner landmark", () => {
    renderShell();
    expect(screen.getAllByRole("banner")).toHaveLength(1);
  });

  it("exposes a primary navigation landmark with an accessible name", () => {
    renderShell();
    expect(screen.getByRole("navigation", { name: /primary/i })).toBeInTheDocument();
  });

  it("exposes exactly one main landmark containing the page content", () => {
    renderShell();
    const main = screen.getByRole("main");
    expect(main).toContainElement(screen.getByText("Page content"));
  });

  it("renders a skip link targeting the main landmark's id", () => {
    renderShell();
    const skipLink = screen.getByText(/skip to main content/i);
    const main = screen.getByRole("main");
    expect(skipLink.getAttribute("href")).toBe(`#${main.id}`);
  });

  it("renders every nav item as an accessible link", () => {
    renderShell();
    for (const item of navItems) {
      expect(screen.getByRole("link", { name: item.label })).toBeInTheDocument();
    }
  });
});
