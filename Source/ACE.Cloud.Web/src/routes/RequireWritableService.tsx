import type { ReactNode } from "react";
import { isServiceWritable } from "../api/types";
import { ReadOnlyBanner } from "../design-system/primitives/ReadOnlyBanner";
import { useSession } from "../session/SessionContext";

export interface RequireWritableServiceProps {
  readonly children: ReactNode;
}

/**
 * ARCH-009: blocks mutation-only UI (e.g. deposits, withdrawals) while the database is ReadOnly
 * or versions are incompatible. Renders optimistically before the first `/health/ready` result
 * arrives -- the server remains the authoritative gate regardless of this client-side check.
 */
export function RequireWritableService({ children }: RequireWritableServiceProps) {
  const { serviceAvailability } = useSession();

  if (serviceAvailability === "unknown" || isServiceWritable(serviceAvailability)) {
    return <>{children}</>;
  }

  return <ReadOnlyBanner mode={serviceAvailability} />;
}
