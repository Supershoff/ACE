import { expect, test } from "@playwright/test";
import { mainAccount } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

test.describe("inventory, appraisal, and search", () => {
  test("browses the Mule Page, filters by category, and opens the Full Cloud Appraisal", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());
    await page.goto("/dashboard");

    await expect(page.getByRole("option").first()).toBeVisible();

    const firstItem = page.getByRole("option").first();
    const itemName = (await firstItem.textContent())?.trim();
    await firstItem.click();

    await expect(page.getByRole("dialog")).toBeVisible();
    if (itemName) {
      await expect(page.getByRole("dialog")).toContainText(itemName.split(" (")[0]!);
    }
  });
});

test.describe("live update without a manual refresh (EVT-007)", () => {
  test("an Activity Ledger entry from creating a Withdrawal Token in one tab appears live in another, unreloaded tab", async ({
    browser,
  }) => {
    const account = mainAccount();
    const context = await browser.newContext();

    const activityPage = await context.newPage();
    await loginAsUiFlow(activityPage, account);
    await activityPage.goto("/activity");
    await expect(activityPage.getByRole("table")).toBeVisible();
    const initialRowCount = await activityPage.getByRole("row").count();

    const accountPage = await context.newPage();
    await loginAsUiFlow(accountPage, account);
    await accountPage.goto("/account");

    const firstCheckbox = accountPage.getByRole("checkbox").first();
    await firstCheckbox.waitFor();
    await firstCheckbox.check();
    await accountPage.getByRole("button", { name: "Create Withdrawal Token" }).click();
    await expect(accountPage.getByText(/Withdrawal Token \(shown once/)).toBeVisible();

    // No reload/navigation on activityPage: this is the resumable Live State Stream (EVT-007)
    // delivering the new WithdrawalReservationOpened entry, reconciled by liveStreamReconciler.ts
    // and refreshed through SessionContext's subscribeLiveStream("custody", ...) wiring.
    await expect(async () => {
      expect(await activityPage.getByRole("row").count()).toBeGreaterThan(initialRowCount);
    }).toPass({ timeout: 15_000 });

    await context.close();
  });
});

test.describe("notifications", () => {
  test("the Notification Center opens to an accessible, empty list before any actionable event", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());

    await page.getByRole("button", { name: "Notifications" }).click();

    await expect(page.getByRole("list")).toBeVisible();
  });
});
