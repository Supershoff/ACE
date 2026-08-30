import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { createAuthApi, type AuthApi } from "../api/authApi";
import { createHttpClient } from "../api/httpClient";
import type { CloudServiceAvailabilityMode } from "../api/types";

/**
 * `"unknown"` means the client has no proof either way (e.g. a fresh page load): the session
 * cookie is HttpOnly and there is no `/auth/session` probe endpoint yet, so guards must treat
 * `"unknown"` the same as `"unauthenticated"` (fail closed) rather than optimistically granting
 * access.
 */
export type SessionStatus = "unknown" | "authenticating" | "authenticated" | "unauthenticated";

/**
 * The server does not yet expose Main/Linked account status over HTTP (AUTH-004's identity
 * endpoints land with issue #33). `"Unknown"` is the only honest default until then, and
 * Main-only route guards must treat it as a denial, never as an implicit "Main".
 */
export type AccountKind = "Main" | "Linked" | "Unknown";

export interface AdminAccessStatus {
  readonly checked: boolean;
  readonly isAdmin: boolean;
  readonly accessLevel: number | null;
}

export interface SessionContextValue {
  readonly status: SessionStatus;
  readonly csrfToken: string | null;
  readonly accountKind: AccountKind;
  readonly serviceAvailability: CloudServiceAvailabilityMode | "unknown";
  login(accountName: string, password: string): Promise<{ ok: boolean }>;
  logout(): Promise<void>;
  /** ADM-001: always revalidates against the server; never trust a cached client-side claim. */
  checkAdminAccess(): Promise<AdminAccessStatus>;
}

export const SessionContext = createContext<SessionContextValue | null>(null);

export function useSession(): SessionContextValue {
  const value = useContext(SessionContext);
  if (!value) {
    throw new Error("useSession must be used within a SessionProvider");
  }
  return value;
}

export interface SessionProviderProps {
  readonly children: ReactNode;
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly authApi?: AuthApi;
}

export function SessionProvider({ children, authApi }: SessionProviderProps) {
  const [status, setStatus] = useState<SessionStatus>("unknown");
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [accountKind, setAccountKind] = useState<AccountKind>("Unknown");
  const [serviceAvailability, setServiceAvailability] = useState<CloudServiceAvailabilityMode | "unknown">("unknown");

  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultAuthApiRef = useRef<AuthApi | null>(null);
  if (!defaultAuthApiRef.current) {
    defaultAuthApiRef.current = createAuthApi(
      createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }),
    );
  }
  const resolvedAuthApi = authApi ?? defaultAuthApiRef.current;

  useEffect(() => {
    let cancelled = false;
    resolvedAuthApi.fetchHealthReady().then((result) => {
      if (!cancelled && result.ok && result.data) {
        setServiceAvailability(result.data.mode);
      }
    });
    return () => {
      cancelled = true;
    };
    // Intentionally runs once per mount: this probes the shell's own service-availability
    // banner, not a per-render-dependent value.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(
    async (accountName: string, password: string): Promise<{ ok: boolean }> => {
      setStatus("authenticating");
      const result = await resolvedAuthApi.login(accountName, password);
      if (result.ok && result.data) {
        setCsrfToken(result.data.csrfToken);
        setStatus("authenticated");
        return { ok: true };
      }
      setCsrfToken(null);
      setStatus("unauthenticated");
      return { ok: false };
    },
    [resolvedAuthApi],
  );

  const logout = useCallback(async (): Promise<void> => {
    await resolvedAuthApi.logout();
    setCsrfToken(null);
    setAccountKind("Unknown");
    setStatus("unauthenticated");
  }, [resolvedAuthApi]);

  const checkAdminAccess = useCallback(async (): Promise<AdminAccessStatus> => {
    const result = await resolvedAuthApi.fetchAdminWhoAmI();
    if (result.ok && result.data) {
      return { checked: true, isAdmin: true, accessLevel: result.data.accessLevel };
    }
    return { checked: true, isAdmin: false, accessLevel: null };
  }, [resolvedAuthApi]);

  const value: SessionContextValue = {
    status,
    csrfToken,
    accountKind,
    serviceAvailability,
    login,
    logout,
    checkAdminAccess,
  };

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
