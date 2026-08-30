import type { ReactNode } from "react";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { useSession } from "../session/SessionContext";

export interface RequireMainAccountProps {
  readonly children: ReactNode;
}

/**
 * AUTH-004: Linked Account credentials cannot view or mutate the Main Account's unified Cloud
 * Inventory. `"Unknown"` (before `/account/identity` resolves, or if it fails) also fails closed
 * rather than defaulting to "Main" -- see `SessionContext`'s `AccountKind` doc comment.
 */
export function RequireMainAccount({ children }: RequireMainAccountProps) {
  const { accountKind } = useSession();

  if (accountKind === "Main") {
    return <>{children}</>;
  }

  const description =
    accountKind === "Linked"
      ? "Linked account credentials cannot view or manage the unified Cloud Inventory. Log in with the Main Account to continue."
      : "This account's Main/Linked status could not be confirmed, so Main Account-only assets stay hidden.";

  return <ErrorState title="Main Account required" description={description} />;
}
