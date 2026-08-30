import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { InventoryQuantityControl } from "./InventoryQuantityControl";

describe("InventoryQuantityControl", () => {
  it("labels the input with the item name and shows the maximum quantity", () => {
    render(<InventoryQuantityControl itemName="Trade Note" maxQuantity={12} value={12} onChange={() => {}} />);

    expect(screen.getByLabelText("Quantity for Trade Note")).toHaveValue(12);
    expect(screen.getByText("of 12")).toBeInTheDocument();
  });

  it("clamps a typed value to at most the maximum quantity", () => {
    const onChange = vi.fn();
    render(<InventoryQuantityControl itemName="Trade Note" maxQuantity={12} value={12} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Quantity for Trade Note"), { target: { value: "99" } });

    expect(onChange).toHaveBeenLastCalledWith(12);
  });

  it("clamps a typed value to at least one", () => {
    const onChange = vi.fn();
    render(<InventoryQuantityControl itemName="Trade Note" maxQuantity={12} value={12} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Quantity for Trade Note"), { target: { value: "0" } });

    expect(onChange).toHaveBeenLastCalledWith(1);
  });
});
