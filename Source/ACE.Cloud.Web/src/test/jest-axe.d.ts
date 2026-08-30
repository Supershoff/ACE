// jest-axe ships no type declarations. This is a minimal ambient shape for the two exports this
// project actually uses (`vitest-jest-axe.d.ts` separately augments vitest's `Assertion` type).
declare module "jest-axe" {
  export interface AxeViolation {
    readonly id: string;
    readonly description: string;
  }

  export interface AxeResults {
    readonly violations: readonly AxeViolation[];
  }

  export function axe(container: Element | Document, options?: Record<string, unknown>): Promise<AxeResults>;

  export const toHaveNoViolations: {
    toHaveNoViolations(received: unknown): { pass: boolean; message: () => string };
  };
}
