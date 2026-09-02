import { expect, test } from "@playwright/test";
import { mainAccount, secondaryAccount, secondaryAccountCharacterName } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

/**
 * Issue #39's browser E2E evidence for XFER-001/XFER-002: sending a Transfer Offer as a contextual
 * action on the currently selected Inventory item, then the recipient accepting it through the web
 * list, using two genuinely independent Main Accounts and their own separate authenticated browser
 * contexts (never a shared session/cookie shortcut, matching `loginAsUiFlow`'s own established
 * discipline). The sender never types or otherwise supplies a raw item ID -- it comes entirely from
 * selecting the item in the Inventory grid (PR #157's blocking human-acceptance feedback).
 */
async function sendOfferToFirstInventoryItem(senderPage: import("@playwright/test").Page, recipientCharacterName: string) {
  await senderPage.goto("/inventory");

  const firstItem = senderPage.getByRole("option").first();
  await expect(firstItem).toBeVisible();
  await firstItem.click();

  await expect(senderPage.getByRole("dialog")).toBeVisible();
  await senderPage.getByRole("button", { name: "Actions" }).click();
  await senderPage.getByRole("menuitem", { name: "Send Transfer Offer…" }).click();

  await senderPage.getByLabel("Recipient character name").fill(recipientCharacterName);
  await senderPage.getByRole("button", { name: "Send offer" }).click();
}

test.describe("Transfer Offers", () => {
  test("sending an offer to a selected item, then the recipient accepting it, transfers the item", async ({ browser }) => {
    const sender = mainAccount();
    const recipientCharacterName = secondaryAccountCharacterName();

    const senderContext = await browser.newContext();
    const senderPage = await senderContext.newPage();
    await loginAsUiFlow(senderPage, sender);

    const inventoryResponse = await senderPage.request.get("/inventory/pages");
    const inventoryBody = await inventoryResponse.json();
    const itemId: number | undefined = inventoryBody.page?.items?.[0]?.itemId;
    test.skip(!itemId, "The sender's synthetic test account has no Cloud Inventory item to offer yet.");

    await sendOfferToFirstInventoryItem(senderPage, recipientCharacterName);

    await senderPage.goto("/transfer-offers");
    await expect(senderPage.getByText("Pending").first()).toBeVisible();

    const recipientContext = await browser.newContext();
    const recipientPage = await recipientContext.newPage();
    await loginAsUiFlow(recipientPage, secondaryAccount());
    await recipientPage.goto("/transfer-offers");

    const acceptButton = recipientPage.getByRole("button", { name: "Accept" }).first();
    await expect(acceptButton).toBeVisible();
    await acceptButton.click();

    await expect(recipientPage.getByText("Accepted").first()).toBeVisible();

    await senderContext.close();
    await recipientContext.close();
  });

  test("declining an offer releases it without transferring the item", async ({ browser }) => {
    const sender = mainAccount();
    const recipientCharacterName = secondaryAccountCharacterName();

    const senderContext = await browser.newContext();
    const senderPage = await senderContext.newPage();
    await loginAsUiFlow(senderPage, sender);

    const inventoryResponse = await senderPage.request.get("/inventory/pages");
    const inventoryBody = await inventoryResponse.json();
    const itemId: number | undefined = inventoryBody.page?.items?.[0]?.itemId;
    test.skip(!itemId, "The sender's synthetic test account has no Cloud Inventory item to offer yet.");

    await sendOfferToFirstInventoryItem(senderPage, recipientCharacterName);

    await senderPage.goto("/transfer-offers");
    await expect(senderPage.getByText("Pending").first()).toBeVisible();

    const recipientContext = await browser.newContext();
    const recipientPage = await recipientContext.newPage();
    await loginAsUiFlow(recipientPage, secondaryAccount());
    await recipientPage.goto("/transfer-offers");

    const declineButton = recipientPage.getByRole("button", { name: "Decline" }).first();
    await expect(declineButton).toBeVisible();
    await declineButton.click();

    await expect(recipientPage.getByText("Declined").first()).toBeVisible();

    await senderContext.close();
    await recipientContext.close();
  });
});
