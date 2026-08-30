import { describe, expect, it } from "vitest";
import { contrastPairs } from "./tokens";
import { contrastRatio } from "./contrast";

describe("contrastRatio", () => {
  it("returns 21 for black against white", () => {
    expect(contrastRatio("#000000", "#ffffff")).toBeCloseTo(21, 1);
  });

  it("returns 1 for identical colors", () => {
    expect(contrastRatio("#241c14", "#241c14")).toBeCloseTo(1, 5);
  });

  it("is symmetric", () => {
    expect(contrastRatio("#ece1c8", "#141019")).toBeCloseTo(contrastRatio("#141019", "#ece1c8"), 10);
  });
});

describe("every declared token contrast pair meets its WCAG minimum", () => {
  it.each(contrastPairs)("$label meets a $minimumRatio:1 ratio", ({ foreground, background, minimumRatio }) => {
    expect(contrastRatio(foreground, background)).toBeGreaterThanOrEqual(minimumRatio);
  });
});
