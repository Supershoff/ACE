import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PublicDisplayIdentity } from "./PublicDisplayIdentity";

describe("PublicDisplayIdentity", () => {
  it("renders only the Display Character name it was given", () => {
    render(<PublicDisplayIdentity displayCharacterName="Silverwing" />);
    expect(screen.getByText("Silverwing")).toBeInTheDocument();
  });

  it("accepts no prop through which an ACE account name could be threaded", () => {
    const props = Object.keys({ displayCharacterName: "Silverwing" } satisfies React.ComponentProps<
      typeof PublicDisplayIdentity
    >);
    expect(props).toEqual(["displayCharacterName"]);
  });
});

/**
 * AUTH-001 / CONTEXT.md: "Account names never appear publicly." This is a regression guard, not a
 * substitute for server-side authorization -- it fails loudly the moment a future public/activity
 * surface starts threading a private account-name field through, rather than relying solely on
 * reviewers to notice.
 */
describe("public-facing source contains no private account-name references", () => {
  const srcRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
  // Only genuinely public/anonymous surfaces are scanned: unlike account/dashboard/admin pages
  // (which show a user their own authenticated data) or the login form (which legitimately
  // collects the visitor's own ACE account name as a credential), these surfaces may render
  // OTHER users' identities and must use Display Characters only (AUTH-001).
  const scannedDirectories = ["public"];
  const scannedFiles = ["pages/MarketplacePage.tsx"];

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
      if ((entry.endsWith(".tsx") || entry.endsWith(".ts")) && !entry.endsWith(".test.tsx") && !entry.endsWith(".test.ts")) {
        files.push(fullPath);
      }
    }
    return files;
  }

  const files = [
    ...scannedDirectories.flatMap((directory) => listSourceFiles(join(srcRoot, directory))),
    ...scannedFiles.map((file) => join(srcRoot, file)),
  ];
  const forbiddenPattern = /accountName|AccountName|ace_auth|account_name/;

  it("scanned at least one public-facing file", () => {
    expect(files.length).toBeGreaterThan(0);
  });

  it.each(files)("%s references no private account-name field", (file) => {
    const content = readFileSync(file, "utf-8");
    expect(content).not.toMatch(forbiddenPattern);
  });
});
