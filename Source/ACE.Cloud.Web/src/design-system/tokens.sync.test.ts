import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  borderTokens,
  colorTokens,
  focusTokens,
  motionTokens,
  spacingTokens,
  touchTargetTokens,
  typographyTokens,
  zIndexTokens,
} from "./tokens";

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const cssPath = join(currentDirectory, "tokens.css");
const cssText = readFileSync(cssPath, "utf-8");

function cssValueOf(customPropertyName: string): string | undefined {
  const match = cssText.match(new RegExp(`${customPropertyName}:\\s*([^;]+);`));
  return match?.[1]?.trim();
}

const expectedPairs: ReadonlyArray<readonly [string, string]> = [
  ["--color-background-canvas", colorTokens.backgroundCanvas],
  ["--color-background-panel", colorTokens.backgroundPanel],
  ["--color-background-panel-raised", colorTokens.backgroundPanelRaised],
  ["--color-border-bevel-light", colorTokens.borderBevelLight],
  ["--color-border-bevel-dark", colorTokens.borderBevelDark],
  ["--color-text-primary", colorTokens.textPrimary],
  ["--color-text-secondary", colorTokens.textSecondary],
  ["--color-text-on-accent", colorTokens.textOnAccent],
  ["--color-accent-brass", colorTokens.accentBrass],
  ["--color-accent-brass-hover", colorTokens.accentBrassHover],
  ["--color-focus-ring", colorTokens.focusRing],
  ["--color-status-danger", colorTokens.statusDanger],
  ["--color-status-danger-text", colorTokens.statusDangerText],
  ["--color-status-success", colorTokens.statusSuccess],
  ["--color-status-success-text", colorTokens.statusSuccessText],
  ["--color-status-info", colorTokens.statusInfo],
  ["--color-status-info-text", colorTokens.statusInfoText],
  ["--color-overlay-scrim", colorTokens.overlayScrim],
  ["--space-xs", spacingTokens.xs],
  ["--space-sm", spacingTokens.sm],
  ["--space-md", spacingTokens.md],
  ["--space-lg", spacingTokens.lg],
  ["--space-xl", spacingTokens.xl],
  ["--space-xxl", spacingTokens.xxl],
  ["--space-xxxl", spacingTokens.xxxl],
  ["--font-family-fidelity", typographyTokens.fontFamilyFidelity],
  ["--font-family-ui", typographyTokens.fontFamilyUi],
  ["--font-size-sm", typographyTokens.fontSizeSm],
  ["--font-size-md", typographyTokens.fontSizeMd],
  ["--font-size-lg", typographyTokens.fontSizeLg],
  ["--font-size-xl", typographyTokens.fontSizeXl],
  ["--line-height-base", typographyTokens.lineHeightBase],
  ["--border-width-thin", borderTokens.widthThin],
  ["--border-width-bevel", borderTokens.widthBevel],
  ["--border-radius-panel", borderTokens.radiusPanel],
  ["--border-radius-control", borderTokens.radiusControl],
  ["--focus-ring-width", focusTokens.ringWidth],
  ["--focus-ring-offset", focusTokens.ringOffset],
  ["--focus-ring-color", focusTokens.ringColor],
  ["--motion-duration-fast", motionTokens.durationFast],
  ["--motion-duration-base", motionTokens.durationBase],
  ["--motion-duration-slow", motionTokens.durationSlow],
  ["--motion-easing-standard", motionTokens.easingStandard],
  ["--z-index-dropdown", String(zIndexTokens.dropdown)],
  ["--z-index-dialog-overlay", String(zIndexTokens.dialogOverlay)],
  ["--z-index-dialog", String(zIndexTokens.dialog)],
  ["--z-index-toast", String(zIndexTokens.toast)],
  ["--touch-target-min-size", touchTargetTokens.minSize],
];

describe("design tokens stay in sync between tokens.ts and tokens.css", () => {
  it.each(expectedPairs)("%s matches tokens.ts", (cssVariable, expectedValue) => {
    expect(cssValueOf(cssVariable)).toBe(expectedValue);
  });

  it("defines a :focus-visible rule using the focus-ring tokens", () => {
    expect(cssText).toMatch(/:focus-visible\s*\{[^}]*outline:\s*var\(--focus-ring-width\)/);
  });

  it("zeroes motion durations under prefers-reduced-motion", () => {
    expect(cssText).toMatch(/prefers-reduced-motion:\s*reduce/);
  });
});
