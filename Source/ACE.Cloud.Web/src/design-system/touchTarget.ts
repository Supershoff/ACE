import type { CSSProperties } from "react";

/** UI-008: the shared inline style every interactive control applies to meet the minimum touch-target size. */
export const touchTargetStyle: CSSProperties = {
  minHeight: "var(--touch-target-min-size)",
  minWidth: "var(--touch-target-min-size)",
};
