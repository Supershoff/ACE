/**
 * The pixel-exact palette/spacing for AC-authentic inventory/appraisal fidelity surfaces (issue #31,
 * UI-004/UI-005/UI-007), deliberately separate from the shell's own approximated `tokens.ts` per that
 * module's own doc comment: "the pixel-exact palette for the Full Cloud Appraisal panel and
 * reconstructed icons is owned by the fidelity corpus (issues #24/#28/#31)." Every literal color/size
 * value in the `inventory/` surface must route through this module -- never a raw hex or pixel
 * literal inline -- so `noHardcodedValues.test.ts` can enforce the same UI-007 discipline here that
 * it already enforces for shell chrome.
 *
 * Exact pixel-for-pixel matching against real ACE client rendering is the protected/human-gated
 * fidelity corpus's job (docs/agents/automation.md's human gates), not this synthetic, always-on
 * approximation; these values are chosen to be visually close to ACE's own dark parchment/brass ID
 * panel and inventory bag styling without depending on any extracted client asset.
 */

export const appraisalColorTokens = {
  panelBackground: "#171008",
  panelBorderLight: "#8a6a2f",
  panelBorderDark: "#0a0602",
  titleText: "#f0d98a",
  bodyText: "#e9dcb8",
  mutedText: "#a8946a",
  positiveText: "#66d17a",
  negativeText: "#e2574c",
} as const;

/**
 * The Full Cloud Appraisal panel's own compact AC-style typography and layout, kept separate from
 * `appraisalColorTokens` (which every renderable `CloudAppraisalLine` color routes through) since
 * these govern structure/spacing rather than per-line color.
 */
export const appraisalLayoutTokens = {
  fontFamily: '"Book Antiqua", "Palatino Linotype", Georgia, serif',
  titleFontSize: "15px",
  bodyFontSize: "13px",
  /** The brass double-edge treatment: an outer brass border plus an inset dark ring drawn via box-shadow. */
  doubleEdgeBorderWidth: "2px",
  doubleEdgeInsetWidth: "2px",
  sectionSpacing: "10px",
  bodyPadding: "12px",
  bodyMaxHeight: "70vh",
} as const;

export const iconGridTokens = {
  cellBackground: "#241c14",
  cellBorderLight: "#5a4a30",
  cellBorderDark: "#0c0904",
  cellSize: "48px",
  /** ACE's native reconstructed icon resolution: icons render at this size, centered within the larger cell frame, never stretched to fill it. */
  iconNativeSize: "32px",
  cellGap: "2px",
  desktopColumns: 6,
  desktopRows: 17,
  /** The classic AC bright-green selection outline, drawn as a separate overlay around the icon (UI-006) -- never the icon's own generic/blue cell highlight. */
  selectionOutlineColor: "#39ff14",
  selectionOutlineWidth: "2px",
  reservedBadgeBackground: "#8a3a2f",
  reservedBadgeText: "#f5e6d8",
  quantityBadgeBackground: "#0a0602",
  quantityBadgeText: "#f0d98a",
  fallbackGlyphBackground: "#2c2c2c",
  fallbackGlyphText: "#9a9a9a",
} as const;
