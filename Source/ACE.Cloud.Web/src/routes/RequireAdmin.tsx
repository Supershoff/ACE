import { useEffect, useState, type ReactNode } from "react";
import { ErrorState } from "../design-system/primitives/ErrorState";
import { LoadingState } from "../design-system/primitives/LoadingState";
import { useSession } from "../session/SessionContext";

export interface RequireAdminProps {
  readonly children: ReactNode;
}

type CheckState = "checking" | "granted" | "denied";

/** ADM-001: revalidates access level 5 against the server on every mount; never trusts a cached claim. */
export function RequireAdmin({ children }: RequireAdminProps) {
  const { checkAdminAccess } = useSession();
  const [state, setState] = useState<CheckState>("checking");

  useEffect(() => {
    let cancelled = false;
    setState("checking");

    checkAdminAccess().then((result) => {
      if (!cancelled) {
        setState(result.isAdmin ? "granted" : "denied");
      }
    });

    return () => {
      cancelled = true;
    };
  }, [checkAdminAccess]);

  if (state === "checking") {
    return <LoadingState label="Confirming administrator access…" />;
  }

  if (state === "denied") {
    return (
      <ErrorState
        title="Administrator access required"
        description="Your account does not currently have the required ACE access level."
      />
    );
  }

  return <>{children}</>;
}
