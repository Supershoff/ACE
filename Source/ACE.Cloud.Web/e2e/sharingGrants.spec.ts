import { expect, test } from "@playwright/test";
import { mainAccount, secondaryAccount, secondaryAccountCharacterName } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

/**
 * Issue #39's browser E2E evidence for SHARE-001..004: setting a personal Sharing Grant through the
 * web form, and the revoked live-view behavior when that grant later changes -- the grantee's own
 * already-open Sharing Grants page reconciles from the server without a manual reload (EVT-007),
 * exactly like `inventoryActivityNotifications.spec.ts`'s own two-tab live-update test proves for the
 * Activity Ledger.
 */
test.describe("Sharing Grants", () => {
  test("granting View & Withdraw to a typed grantee character appears in that grantee's received list", async ({ browser }) => {
    const owner = mainAccount();
    const granteeCharacterName = secondaryAccountCharacterName();

    const ownerContext = await browser.newContext();
    const ownerPage = await ownerContext.newPage();
    await loginAsUiFlow(ownerPage, owner);
    await ownerPage.goto("/sharing-grants");

    await ownerPage.getByLabel("Grantee character name").fill(granteeCharacterName);
    await ownerPage.getByLabel("Access level").selectOption("ViewAndWithdraw");
    await ownerPage.getByRole("button", { name: "Save" }).click();

    await expect(ownerPage.getByText("ViewAndWithdraw").first()).toBeVisible();

    const granteeContext = await browser.newContext();
    const granteePage = await granteeContext.newPage();
    await loginAsUiFlow(granteePage, secondaryAccount());
    await granteePage.goto("/sharing-grants");

    await expect(granteePage.getByText("ViewAndWithdraw").first()).toBeVisible();

    await ownerContext.close();
    await granteeContext.close();
  });

  test("revoking a grant to None reconciles the grantee's already-open list without a manual reload", async ({ browser }) => {
    const owner = mainAccount();
    const granteeCharacterName = secondaryAccountCharacterName();

    const ownerContext = await browser.newContext();
    const ownerPage = await ownerContext.newPage();
    await loginAsUiFlow(ownerPage, owner);
    await ownerPage.goto("/sharing-grants");
    await ownerPage.getByLabel("Grantee character name").fill(granteeCharacterName);
    await ownerPage.getByLabel("Access level").selectOption("ViewOnly");
    await ownerPage.getByRole("button", { name: "Save" }).click();
    await expect(ownerPage.getByText("ViewOnly").first()).toBeVisible();

    const granteeContext = await browser.newContext();
    const granteePage = await granteeContext.newPage();
    await loginAsUiFlow(granteePage, secondaryAccount());
    await granteePage.goto("/sharing-grants");
    await expect(granteePage.getByText("ViewOnly").first()).toBeVisible();

    // No reload/navigation on granteePage from here: this is the revoked-access live view EVT-007
    // and SHARE-004 require, delivered by the same resumable Live State Stream
    // `inventoryActivityNotifications.spec.ts` proves for the Activity Ledger.
    await ownerPage.getByLabel("Grantee character name").fill(granteeCharacterName);
    await ownerPage.getByLabel("Access level").selectOption("None");
    await ownerPage.getByRole("button", { name: "Save" }).click();

    await expect(granteePage.getByText("None").first()).toBeVisible({ timeout: 15_000 });

    await ownerContext.close();
    await granteeContext.close();
  });
});
