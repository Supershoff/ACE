function srgbChannelToLinear(channel: number): number {
  const normalized = channel / 255;
  return normalized <= 0.03928 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance(hexColor: string): number {
  const hex = hexColor.replace("#", "");
  const r = parseInt(hex.slice(0, 2), 16);
  const g = parseInt(hex.slice(2, 4), 16);
  const b = parseInt(hex.slice(4, 6), 16);
  return 0.2126 * srgbChannelToLinear(r) + 0.7152 * srgbChannelToLinear(g) + 0.0722 * srgbChannelToLinear(b);
}

/** WCAG 2.x contrast ratio between two sRGB hex colors, in the range [1, 21]. */
export function contrastRatio(hexColorA: string, hexColorB: string): number {
  const luminanceA = relativeLuminance(hexColorA);
  const luminanceB = relativeLuminance(hexColorB);
  const lighter = Math.max(luminanceA, luminanceB);
  const darker = Math.min(luminanceA, luminanceB);
  return (lighter + 0.05) / (darker + 0.05);
}
