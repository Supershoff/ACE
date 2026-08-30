import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useSession } from "../session/SessionContext";

export interface RequireAuthProps {
  readonly children: ReactNode;
}

/** Fails closed: an "unknown" session (no proof of login yet) is treated as unauthenticated. */
export function RequireAuth({ children }: RequireAuthProps) {
  const { status } = useSession();

  if (status !== "authenticated") {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}
