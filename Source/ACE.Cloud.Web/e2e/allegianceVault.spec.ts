import { expect, test } from "@playwright/test";
import { mainAccount } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

/**
 * Issue #39's browser E2E evidence for VAULT-001..003: Acting Character selection and
 * contribute/take through the web client. Requires the synthetic test account to already have a
 * current character sworn to a live ACE allegiance (VAULT-001 revalidates this against ace_shard,
 * never a fixture) -- the runbook documents creating one on the disposable test world. Mirrors
 * `inventoryActivityNotifications.spec.ts`'s own established pattern of skipping with an explicit,
 * logged reason rather than silently passing when an opt-in prerequisite has not been staged.
 */
test.describe("Allegiance Vault", () => {
  test("selects an Acting Character and contributes, then takes, one item", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());
    await page.goto("/allegiance-vault");

    const characterSelect = page.getByLabel("Acting Character");
    const hasAllegiance = (await characterSelect.locator("option").count()) > 0 && !(await characterSelect.getByText("No current allegiance").isVisible().catch(() => false));
    test.skip(!hasAllegiance, "The synthetic test account has no character currently sworn to a live allegiance yet.");

    const inventoryResponse = await page.request.get("/inventory/pages");
    const inventoryBody = await inventoryResponse.json();
    const itemId: number | undefined = inventoryBody.page?.items?.[0]?.itemId;
    test.skip(!itemId, "The test account has no personal Cloud Inventory item to contribute yet.");

    await page.getByLabel("Contribute item ID").fill(String(itemId));
    await page.getByRole("button", { name: "Contribute" }).click();

    await expect(page.getByText("This Allegiance Vault is empty.")).toHaveCount(0);

    await page.getByLabel("Take item ID").fill(String(itemId));
    await page.getByRole("button", { name: "Take" }).click();

    // A successful take must not error; the item leaves this vault view (it is back in personal
    // inventory, proven separately by the persistence suite's own ContributeAndTake round-trip).
    await expect(page.getByRole("alert")).toHaveCount(0);
  });
});
