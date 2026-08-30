import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * UI-007 / the issue #29 acceptance criteria require fidelity and shell surfaces to draw every
 * color and spacing value from the shared design tokens rather than generic hard-coded defaults.
 * This walks every component source file outside the tokens module themselves and fails if it
 * finds a raw hex color or a raw pixel literal that isn't flowing through `var(--...)`.
 */

const srcRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

const scannedDirectories = ["design-system/primitives", "shell", "pages", "public"];

const allowedFileSuffixes = [".tsx", ".ts"];
const excludedSuffixes = [".test.ts", ".test.tsx", ".d.ts"];

function listSourceFiles(directory: string): string[] {
  let entries: string[];
  try {
    entries = readdirSync(directory);
  } catch {
    return [];
  }

  const files: string[] = [];
  for (const entry of entries) {
    const fullPath = join(directory, entry);
    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      files.push(...listSourceFiles(fullPath));
      continue;
    }
    if (
      allowedFileSuffixes.some((suffix) => entry.endsWith(suffix)) &&
      !excludedSuffixes.some((suffix) => entry.endsWith(suffix))
    ) {
      files.push(fullPath);
    }
  }
  return files;
}

const hexColorPattern = /#[0-9a-fA-F]{3,8}\b/g;
const rawPixelInStylePattern = /:\s*["'`]?-?\d+(?:\.\d+)?px\b(?!\s*\)\s*var)/g;

describe("fidelity and shell surfaces contain no unapproved hard-coded colors or spacing", () => {
  const files = scannedDirectories.flatMap((directory) => listSourceFiles(join(srcRoot, directory)));

  it("scanned at least one component file", () => {
    expect(files.length).toBeGreaterThan(0);
  });

  it.each(files)("%s has no raw hex colors", (file) => {
    const content = readFileSync(file, "utf-8");
    const matches = content.match(hexColorPattern) ?? [];
    expect(matches).toEqual([]);
  });

  it.each(files)("%s has no raw pixel literals outside token references", (file) => {
    const content = readFileSync(file, "utf-8");
    const matches = content.match(rawPixelInStylePattern) ?? [];
    expect(matches).toEqual([]);
  });
});
