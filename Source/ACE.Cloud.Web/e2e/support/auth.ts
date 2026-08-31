import type { Page } from "@playwright/test";
import type { AcceptanceTestAccount } from "./env";

/** Logs in through the real UI (never a shortcut through cookies/localStorage) so every spec exercises the actual auth flow. */
export async function loginAsUiFlow(page: Page, account: AcceptanceTestAccount): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("ACE account name").fill(account.accountName);
  await page.getByLabel("Password").fill(account.password);
  await page.getByRole("button", { name: "Log in" }).click();
  // The Notification Center only renders once `status === "authenticated"` (NotificationCenter.tsx),
  // so waiting for it is a real signal of a completed login rather than a static nav link.
  await page.getByRole("button", { name: "Notifications" }).waitFor();
}
