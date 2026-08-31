import { useState } from "react";
import { iconGridTokens } from "../design-system/inventoryFidelityTokens";

export interface InventoryIconProps {
  readonly name: string;
  readonly iconCacheKeyHex: string | null;
  readonly buildIconUrl: (iconCacheKeyHex: string) => string;
}

const fallbackGlyphStyle = {
  width: iconGridTokens.iconNativeSize,
  height: iconGridTokens.iconNativeSize,
  backgroundColor: iconGridTokens.fallbackGlyphBackground,
  color: iconGridTokens.fallbackGlyphText,
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
} as const;

const imageStyle = {
  width: "100%",
  height: "100%",
  display: "block",
} as const;

/**
 * Icon Reconstruction's web-facing derivative (UI-005/UI-006). Stack quantity, selection, and
 * reservation badges are deliberately never drawn onto this element -- they are separate overlays a
 * caller layers on top (UI-006: "Stack quantity, selection, reservation, and web badges are separate
 * UI layers") -- so this component only ever renders the reconstructed source icon itself, or an
 * explicit neutral fallback glyph when no cache key exists yet or the composed derivative 404s.
 */
export function InventoryIcon({ name, iconCacheKeyHex, buildIconUrl }: InventoryIconProps) {
  const [failed, setFailed] = useState(false);

  if (!iconCacheKeyHex || failed) {
    return (
      <div className="inventory-icon inventory-icon--fallback" style={fallbackGlyphStyle} aria-hidden="true">
        {name.charAt(0).toUpperCase()}
      </div>
    );
  }

  return (
    <img
      className="inventory-icon"
      src={buildIconUrl(iconCacheKeyHex)}
      alt=""
      style={imageStyle}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  );
}
