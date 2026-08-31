import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { createAccountApi, type AccountApi } from "../api/accountApi";
import { createAuthApi, type AuthApi } from "../api/authApi";
import { createHttpClient } from "../api/httpClient";
import type { CloudLiveStreamStatus } from "../api/liveStream";
import { createResumableCloudLiveStreamClient, type CloudResumableLiveStreamClient, type CloudResumableLiveStreamOptions } from "../api/liveStreamClient";
import { createCloudLiveStreamReconciler, type CloudLiveStreamRefreshScope } from "../api/liveStreamReconciler";
import type { CloudServiceAvailabilityMode } from "../api/types";

const LIVE_STREAM_URL = "/live-stream";

/**
 * `"unknown"` means the client has no proof either way (e.g. a fresh page load): the session
 * cookie is HttpOnly and there is no `/auth/session` probe endpoint yet, so guards must treat
 * `"unknown"` the same as `"unauthenticated"` (fail closed) rather than optimistically granting
 * access.
 */
export type SessionStatus = "unknown" | "authenticating" | "authenticated" | "unauthenticated";

/**
 * AUTH-004: resolved from `GET /account/identity` right after a successful login. `"Unknown"` is
 * the fail-closed default before that resolution completes (or if it fails), and every Main-only
 * route guard must treat it as a denial, never as an implicit "Main".
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
  /** The Main Account name the user themself just typed to log in (AUTH-005's "type your Main account name to confirm" linking safeguard); null before login resolves. */
  readonly accountName: string | null;
  readonly serviceAvailability: CloudServiceAvailabilityMode | "unknown";
  /** EVT-007: the shared Live State Stream connection's own transport state, not the service-availability mode it carries. */
  readonly liveStream: {
    readonly status: CloudLiveStreamStatus;
    /** True whenever the connection is not currently open, so views built from a live subscription can flag their data as possibly out of date. */
    readonly stale: boolean;
  };
  login(accountName: string, password: string): Promise<{ ok: boolean }>;
  logout(): Promise<void>;
  /** ADM-001: always revalidates against the server; never trust a cached client-side claim. */
  checkAdminAccess(): Promise<AdminAccessStatus>;
  /**
   * Subscribes to coalesced, deduplicated Live State Stream refresh signals for one scope
   * (issue #34: inventory/Activity Ledger both react to "custody" events, Notification Center to
   * "notification" events). Returns an unsubscribe function.
   */
  subscribeLiveStream(scope: CloudLiveStreamRefreshScope, listener: () => void): () => void;
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
  /** Overridable for tests; production code lets this default to the real Cloud backend client. */
  readonly accountApi?: AccountApi;
  /** Overridable for tests; production code lets this default to a real resumable Live State Stream connection. */
  readonly createLiveStreamClient?: (options: CloudResumableLiveStreamOptions) => CloudResumableLiveStreamClient;
}

export function SessionProvider({ children, authApi, accountApi, createLiveStreamClient }: SessionProviderProps) {
  const [status, setStatus] = useState<SessionStatus>("unknown");
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [accountKind, setAccountKind] = useState<AccountKind>("Unknown");
  const [accountName, setAccountName] = useState<string | null>(null);
  const [serviceAvailability, setServiceAvailability] = useState<CloudServiceAvailabilityMode | "unknown">("unknown");
  const [liveStreamStatus, setLiveStreamStatus] = useState<CloudLiveStreamStatus>("idle");
  const [liveStreamStale, setLiveStreamStale] = useState(true);

  const csrfTokenRef = useRef<string | null>(null);
  csrfTokenRef.current = csrfToken;

  const defaultAuthApiRef = useRef<AuthApi | null>(null);
  if (!defaultAuthApiRef.current) {
    defaultAuthApiRef.current = createAuthApi(
      createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }),
    );
  }
  const resolvedAuthApi = authApi ?? defaultAuthApiRef.current;

  const defaultAccountApiRef = useRef<AccountApi | null>(null);
  if (!defaultAccountApiRef.current) {
    defaultAccountApiRef.current = createAccountApi(
      createHttpClient({ baseUrl: "", getCsrfToken: () => csrfTokenRef.current }),
    );
  }
  const resolvedAccountApi = accountApi ?? defaultAccountApiRef.current;

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

  const liveStreamSubscribersRef = useRef<Map<CloudLiveStreamRefreshScope, Set<() => void>>>(new Map());

  const subscribeLiveStream = useCallback((scope: CloudLiveStreamRefreshScope, listener: () => void): (() => void) => {
    let listeners = liveStreamSubscribersRef.current.get(scope);
    if (!listeners) {
      listeners = new Set();
      liveStreamSubscribersRef.current.set(scope, listeners);
    }
    listeners.add(listener);
    return () => {
      listeners!.delete(listener);
    };
  }, []);

  useEffect(() => {
    if (status !== "authenticated") {
      return;
    }

    const reconciler = createCloudLiveStreamReconciler({
      onModeChange: (mode) => setServiceAvailability(mode),
      onRefresh: (scope) => {
        liveStreamSubscribersRef.current.get(scope)?.forEach((listener) => listener());
      },
    });

    const resolvedCreateLiveStreamClient = createLiveStreamClient ?? createResumableCloudLiveStreamClient;
    const client = resolvedCreateLiveStreamClient({
      url: LIVE_STREAM_URL,
      onMessage: (message) => reconciler.handleMessage(message),
      onStatusChange: (nextStatus, nextStale) => {
        setLiveStreamStatus(nextStatus);
        setLiveStreamStale(nextStale);
      },
    });
    client.connect();

    return () => {
      client.disconnect();
      reconciler.dispose();
      setLiveStreamStatus("idle");
      setLiveStreamStale(true);
    };
    // Reconnects only when authentication status itself changes; createLiveStreamClient is a
    // test-only override that is stable in production.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  const login = useCallback(
    async (typedAccountName: string, password: string): Promise<{ ok: boolean }> => {
      setStatus("authenticating");
      const result = await resolvedAuthApi.login(typedAccountName, password);
      if (result.ok && result.data) {
        setCsrfToken(result.data.csrfToken);
        setAccountName(typedAccountName);
        setStatus("authenticated");

        // AUTH-004: resolve Main/Linked status immediately so Main-only route guards never have to
        // fall back to the fail-closed "Unknown" default for a genuinely logged-in session.
        const identity = await resolvedAccountApi.fetchIdentity();
        setAccountKind(identity.ok && identity.data ? identity.data.accountKind : "Unknown");

        return { ok: true };
      }
      setCsrfToken(null);
      setStatus("unauthenticated");
      return { ok: false };
    },
    [resolvedAuthApi, resolvedAccountApi],
  );

  const logout = useCallback(async (): Promise<void> => {
    await resolvedAuthApi.logout();
    setCsrfToken(null);
    setAccountKind("Unknown");
    setAccountName(null);
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
    accountName,
    serviceAvailability,
    liveStream: { status: liveStreamStatus, stale: liveStreamStale },
    login,
    logout,
    checkAdminAccess,
    subscribeLiveStream,
  };

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
