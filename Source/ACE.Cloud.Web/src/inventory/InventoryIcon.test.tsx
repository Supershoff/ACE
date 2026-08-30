import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { InventoryIcon } from "./InventoryIcon";

const buildIconUrl = (hex: string) => `/inventory/icons/${hex}`;

describe("InventoryIcon", () => {
  it("renders the composed icon by its cache key", () => {
    render(<InventoryIcon name="Ivory Buckler" iconCacheKeyHex={"a".repeat(64)} buildIconUrl={buildIconUrl} />);

    const img = document.querySelector("img.inventory-icon");
    expect(img).toHaveAttribute("src", `/inventory/icons/${"a".repeat(64)}`);
  });

  it("shows a neutral fallback glyph when no icon cache key exists yet", () => {
    render(<InventoryIcon name="Ivory Buckler" iconCacheKeyHex={null} buildIconUrl={buildIconUrl} />);

    expect(document.querySelector("img.inventory-icon")).not.toBeInTheDocument();
    expect(screen.getByText("I")).toBeInTheDocument();
  });

  it("falls back to the neutral glyph rather than a broken image when the composed derivative fails to load", () => {
    render(<InventoryIcon name="Ivory Buckler" iconCacheKeyHex={"b".repeat(64)} buildIconUrl={buildIconUrl} />);

    const img = document.querySelector("img.inventory-icon")!;
    fireEvent.error(img);

    expect(document.querySelector("img.inventory-icon")).not.toBeInTheDocument();
    expect(screen.getByText("I")).toBeInTheDocument();
  });
});
