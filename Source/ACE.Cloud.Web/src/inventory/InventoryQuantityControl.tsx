import { useId } from "react";

export interface InventoryQuantityControlProps {
  readonly itemName: string;
  readonly maxQuantity: number;
  readonly value: number;
  readonly onChange: (nextValue: number) => void;
}

/**
 * INV-002: "Selecting one stack reveals an inline quantity control defaulted to full." The caller
 * owns the actual default-to-full initialization (this component is a plain controlled input); this
 * component only enforces the 1..maxQuantity clamp so a caller can never end up with an invalid
 * partial-operation quantity through direct typing.
 */
export function InventoryQuantityControl({ itemName, maxQuantity, value, onChange }: InventoryQuantityControlProps) {
  const inputId = useId();

  function handleChange(rawValue: string) {
    const parsed = Number.parseInt(rawValue, 10);
    if (Number.isNaN(parsed)) {
      return;
    }
    onChange(Math.min(Math.max(parsed, 1), maxQuantity));
  }

  return (
    <div className="inventory-quantity-control">
      <label htmlFor={inputId}>Quantity for {itemName}</label>
      <input
        id={inputId}
        type="number"
        min={1}
        max={maxQuantity}
        value={value}
        onChange={(event) => handleChange(event.target.value)}
      />
      <span className="inventory-quantity-control__max">of {maxQuantity}</span>
    </div>
  );
}
