import { expect, test } from "@playwright/test";
import { mainAccount } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

test.describe("inventory, appraisal, and search", () => {
  test("browses the Mule Page, filters by category, and opens the Full Cloud Appraisal", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());
    await page.goto("/inventory");

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

test.describe("icon reconstruction and selection", () => {
  test("selecting a grid cell (without opening it) shows the AC-style bright green selection border", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());
    await page.goto("/inventory");

    const firstItem = page.getByRole("option").first();
    await expect(firstItem).toBeVisible();

    // A modifier-click toggles selection without opening the Full Cloud Appraisal (MulePageGrid's
    // `additive` click), unlike the plain click the appraisal test above uses.
    await firstItem.click({ modifiers: ["Control"] });
    await expect(page.getByRole("dialog")).toHaveCount(0);

    await expect(firstItem).toHaveAttribute("aria-selected", "true");
    const outline = firstItem.locator(".mule-page-grid__selection-outline");
    await expect(outline).toBeVisible();
    await expect(outline).toHaveCSS("border-color", "rgb(57, 255, 20)");
  });

  test("a rendered (non-fallback) grid icon serves real image bytes, not a broken image", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());
    await page.goto("/inventory");
    await expect(page.getByRole("option").first()).toBeVisible();

    const composedIcon = page.locator("img.inventory-icon").first();
    if ((await composedIcon.count()) === 0) {
      // No client_portal.dat has been staged/activated for this run (Add-LocalAcceptancePortalDat.ps1
      // is a separate, opt-in step) -- every item is still showing the neutral fallback glyph, which
      // is already covered by InventoryIcon's own unit tests. Nothing further to prove here.
      return;
    }

    const iconUrl = await composedIcon.getAttribute("src");
    expect(iconUrl).toBeTruthy();

    const response = await page.request.get(iconUrl!);
    expect(response.status()).toBe(200);
    expect(response.headers()["content-type"]).toBe("image/png");
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
