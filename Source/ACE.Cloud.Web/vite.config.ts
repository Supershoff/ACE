/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    // `e2e/` holds Playwright specs against a live disposable stack (issue #34's acceptance
    // launcher runs them separately via `npm run test:e2e`); Vitest's default include glob would
    // otherwise also pick up `e2e/*.spec.ts` and fail importing `@playwright/test`, which is
    // installed on demand by the launcher rather than committed to this project's lockfile.
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
  },
});
