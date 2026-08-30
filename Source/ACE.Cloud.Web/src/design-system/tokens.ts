/**
 * The single source of truth for every design-system primitive value.
 *
 * Colors, borders, and typography approximate Asheron's Call's own dark bevelled-panel
 * interface (UI-007) for shell chrome. They are deliberate approximations for
 * non-fidelity surfaces -- the pixel-exact palette for the Full Cloud Appraisal panel and
 * reconstructed icons is owned by the fidelity corpus (issues #24/#28/#31) and must not be
 * confused with these shell tokens.
 *
 * Every component in `design-system/` and `shell/` MUST reference these tokens (via the
 * generated CSS custom properties in `tokens.css`) instead of hard-coding colors or spacing.
 * `tokens.sync.test.ts` and `noHardcodedValues.test.ts` enforce this.
 */

export const colorTokens = {
  backgroundCanvas: "#141019",
  backgroundPanel: "#241c14",
  backgroundPanelRaised: "#2f2418",
  borderBevelLight: "#8a7550",
  borderBevelDark: "#0c0904",
  textPrimary: "#ece1c8",
  textSecondary: "#c9b990",
  textOnAccent: "#1c1408",
  accentBrass: "#c9a227",
  accentBrassHover: "#e0b93a",
  focusRing: "#5fd0ff",
  statusDanger: "#e2574c",
  statusDangerText: "#1f0503",
  statusSuccess: "#4caf6a",
  statusSuccessText: "#08210f",
  statusInfo: "#4fa3d1",
  statusInfoText: "#04141f",
  overlayScrim: "rgba(10, 7, 3, 0.72)",
} as const;

export const spacingTokens = {
  xs: "4px",
  sm: "8px",
  md: "12px",
  lg: "16px",
  xl: "24px",
  xxl: "32px",
  xxxl: "48px",
} as const;

export const typographyTokens = {
  fontFamilyFidelity: '"Book Antiqua", "Palatino Linotype", Georgia, serif',
  fontFamilyUi: '"Segoe UI", system-ui, -apple-system, sans-serif',
  fontSizeSm: "13px",
  fontSizeMd: "15px",
  fontSizeLg: "18px",
  fontSizeXl: "22px",
  lineHeightBase: "1.4",
} as const;

export const borderTokens = {
  widthThin: "1px",
  widthBevel: "2px",
  radiusPanel: "2px",
  radiusControl: "3px",
} as const;

export const breakpointTokens = {
  narrowMaxWidth: "640px",
} as const;

export const focusTokens = {
  ringWidth: "3px",
  ringOffset: "2px",
  ringColor: colorTokens.focusRing,
} as const;

export const motionTokens = {
  durationFast: "120ms",
  durationBase: "200ms",
  durationSlow: "320ms",
  easingStandard: "cubic-bezier(0.2, 0, 0, 1)",
} as const;

export const zIndexTokens = {
  dropdown: 1000,
  dialogOverlay: 1100,
  dialog: 1101,
  toast: 1200,
} as const;

export const touchTargetTokens = {
  minSize: "44px",
} as const;

/**
 * Token pairs that a fidelity or shell surface is allowed to combine for text/background or
 * UI-component/background contrast, and the minimum WCAG ratio each pairing must meet.
 * `contrast.test.ts` computes the real ratio for every entry so a future token edit that
 * silently breaks contrast fails CI instead of shipping.
 */
export const contrastPairs: ReadonlyArray<{
  readonly label: string;
  readonly foreground: string;
  readonly background: string;
  readonly minimumRatio: number;
}> = [
  { label: "body text on canvas", foreground: colorTokens.textPrimary, background: colorTokens.backgroundCanvas, minimumRatio: 4.5 },
  { label: "body text on panel", foreground: colorTokens.textPrimary, background: colorTokens.backgroundPanel, minimumRatio: 4.5 },
  { label: "secondary text on panel", foreground: colorTokens.textSecondary, background: colorTokens.backgroundPanel, minimumRatio: 4.5 },
  { label: "button text on accent", foreground: colorTokens.textOnAccent, background: colorTokens.accentBrass, minimumRatio: 4.5 },
  { label: "focus ring on canvas", foreground: colorTokens.focusRing, background: colorTokens.backgroundCanvas, minimumRatio: 3 },
  { label: "focus ring on panel", foreground: colorTokens.focusRing, background: colorTokens.backgroundPanel, minimumRatio: 3 },
  { label: "error banner text", foreground: colorTokens.statusDangerText, background: colorTokens.statusDanger, minimumRatio: 4.5 },
  { label: "success banner text", foreground: colorTokens.statusSuccessText, background: colorTokens.statusSuccess, minimumRatio: 4.5 },
  { label: "info banner text", foreground: colorTokens.statusInfoText, background: colorTokens.statusInfo, minimumRatio: 4.5 },
  { label: "accent text on canvas", foreground: colorTokens.accentBrass, background: colorTokens.backgroundCanvas, minimumRatio: 4.5 },
];
