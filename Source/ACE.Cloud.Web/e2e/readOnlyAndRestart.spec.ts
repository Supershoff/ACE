import { expect, test } from "@playwright/test";
import { mainAccount } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

/**
 * The documented restart/read-only scenario (issue #34). A true Backend/Worker process restart is
 * exercised manually per the acceptance checklist in `Tools/LocalAcceptance/README.md` (a Playwright
 * spec cannot itself stop and restart the launcher's background dotnet processes without depending on
 * the very connection it is trying to observe). This spec deterministically reproduces the two
 * client-visible effects of that scenario -- ReadOnly mode and a stale/disconnected Live State Stream
 * -- by intercepting the health probe and the SSE connection, so the read-only banner and stale
 * indicator are still covered by an automated, repeatable check.
 */
test.describe("read-only mode and a stale Live State Stream connection", () => {
  test("shows the read-only banner when /health/ready reports ReadOnly", async ({ page }) => {
    await page.route("**/health/ready", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ mode: "ReadOnly", results: [] }),
      });
    });

    await loginAsUiFlow(page, mainAccount());

    await expect(page.getByRole("status").filter({ hasText: /read-only/i })).toBeVisible();
  });

  test("shows the stale Live State Stream notice when the connection cannot be established", async ({ page }) => {
    await page.route("**/live-stream", async (route) => {
      await route.abort("connectionrefused");
    });

    await loginAsUiFlow(page, mainAccount());

    await expect(page.getByRole("status").filter({ hasText: /live updates are temporarily paused/i })).toBeVisible({
      timeout: 15_000,
    });
  });
});
