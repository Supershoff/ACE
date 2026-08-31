import { expect, test } from "@playwright/test";
import { mainAccount } from "./support/env";
import { loginAsUiFlow } from "./support/auth";

test.describe("login", () => {
  test("logs in with a valid synthetic test account and reaches the authenticated shell", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());

    await expect(page.getByRole("button", { name: "Notifications" })).toBeVisible();
  });

  test("shows an error and stays logged out for an invalid password", async ({ page }) => {
    const account = mainAccount();

    await page.goto("/login");
    await page.getByLabel("ACE account name").fill(account.accountName);
    await page.getByLabel("Password").fill(`${account.password}-wrong`);
    await page.getByRole("button", { name: "Log in" }).click();

    await expect(page.getByText("Could not log in")).toBeVisible();
    await expect(page.getByRole("button", { name: "Notifications" })).toHaveCount(0);
  });
});
