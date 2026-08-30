import { render, screen, waitFor } from "@testing-library/react";
import { axe, toHaveNoViolations } from "jest-axe";
import { describe, expect, it } from "vitest";
import { InventoryView } from "./InventoryView";
import { fakeInventoryApi } from "./testFakes";

expect.extend(toHaveNoViolations);

describe("InventoryView accessibility", () => {
  it("has no detectable accessibility violations once loaded", async () => {
    const api = fakeInventoryApi();
    const { container } = render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(await axe(container)).toHaveNoViolations();
  });

  it("has an accessible name for the category selector and view switcher", async () => {
    const api = fakeInventoryApi();
    render(<InventoryView inventoryApi={api} />);

    await waitFor(() => screen.getByRole("option", { name: "Ivory Buckler" }));

    expect(screen.getByLabelText("Category")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Grid" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Spreadsheet" })).toHaveAttribute("aria-pressed", "false");
  });
});
