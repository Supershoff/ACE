import "@testing-library/jest-dom/vitest";
import { expect } from "vitest";
import { toHaveNoViolations } from "jest-axe";

expect.extend(toHaveNoViolations);

/**
 * jsdom does not implement `matchMedia`. Every test gets a default "nothing matches" stub so
 * components that read viewport/motion-preference media queries do not crash; individual tests
 * override this with `vi.spyOn(window, "matchMedia")` to simulate a narrow viewport or a
 * reduced-motion preference.
 */
{
  // Always override rather than checking `!window.matchMedia` first: jsdom defines a
  // `matchMedia` function that throws "Not implemented" when called, so the property already
  // being present does not mean it is safe to use.
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}
