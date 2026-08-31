import { defineConfig, devices } from "@playwright/test";

/**
 * Runs against the disposable local acceptance stack started by
 * `Tools/LocalAcceptance/Start-LocalAcceptance.ps1` (issue #34's testability companion pass), never
 * against a real ACE installation. `@playwright/test` is intentionally not a committed
 * `package.json` dependency (see `vite.config.ts`'s test `include` comment) -- the launcher installs
 * it on demand so this stays test tooling, not a permanent addition to the shipped app's dependency
 * tree.
 */
const baseURL = process.env.ACE_CLOUD_ACCEPTANCE_BASE_URL ?? "http://127.0.0.1:4173";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: [["list"]],
  timeout: 30_000,
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "desktop",
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "mobile",
      use: { ...devices["Pixel 7"] },
    },
  ],
});
