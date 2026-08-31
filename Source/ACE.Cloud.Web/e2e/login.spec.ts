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

  test("logging out returns to Login and a subsequent login works normally", async ({ page }) => {
    await loginAsUiFlow(page, mainAccount());

    await page.getByRole("button", { name: "Log out" }).click();

    await expect(page.getByRole("button", { name: "Log in" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Notifications" })).toHaveCount(0);

    // The session actually cleared server-side too, not just the client route -- re-login from the
    // same page must work exactly like a fresh visit.
    await loginAsUiFlow(page, mainAccount());
    await expect(page.getByRole("button", { name: "Notifications" })).toBeVisible();
  });
});
